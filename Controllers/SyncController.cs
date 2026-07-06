using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DigitalVisionBoard.Data;
using DigitalVisionBoard.Models;
using DigitalVisionBoard.Services;

namespace DigitalVisionBoard.Controllers
{
    [Route("api/sync")]
    public class SyncController : BaseApiController
    {
        private readonly BoardService _boardService;
        private readonly ILogger<SyncController> _logger;

        public SyncController(AppDbContext context, AuthService authService, BoardService boardService, ILogger<SyncController> logger)
            : base(context, authService)
        {
            _boardService = boardService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Sync([FromBody] SyncRequest request)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { error = "Unauthorized: Invalid or expired token." });
            }

            try
            {
                if (request.Queue == null)
                {
                    return BadRequest(new { error = "Sync requires a list queue of actions." });
                }

                var emailLower = _boardService.NormalizeEmail(user.Email);
                var activityLogsToSave = new List<ActivityLog>();
                var skippedActions = new List<SyncSkippedActionDto>();
                var warnings = new List<string>();
                var appliedCount = 0;

                void Skip(SyncActionDto actionItem, string reason, bool addActivityLog = false)
                {
                    skippedActions.Add(new SyncSkippedActionDto(actionItem.Action, actionItem.BoardId, actionItem.ItemId, reason));
                    warnings.Add(reason);
                    if (addActivityLog)
                    {
                        activityLogsToSave.Add(new ActivityLog
                        {
                            BoardId = actionItem.BoardId,
                            UserEmail = BuildActivityActor(user),
                            ActionDescription = $"Skipped sync action: {reason}",
                            Timestamp = actionItem.Timestamp == default ? DateTime.UtcNow : actionItem.Timestamp
                        });
                    }

                    _logger.LogWarning(
                        "Skipped sync action {Action} for board {BoardId}, item {ItemId}, user {UserId}: {Reason}",
                        actionItem.Action,
                        actionItem.BoardId,
                        actionItem.ItemId,
                        user.Id,
                        reason);
                }

                // Sort queue by action sequence timestamp to ensure chronological updates
                var sortedQueue = request.Queue
                    .OrderBy(a => a.Timestamp)
                    .ToList();

                _logger.LogInformation("Processing {Count} sync actions for user {UserId}", sortedQueue.Count, user.Id);

                foreach (var actionItem in sortedQueue)
                {
                    if (!IsSupportedSyncAction(actionItem.Action))
                    {
                        Skip(actionItem, $"Unsupported sync action '{actionItem.Action}'.");
                        continue;
                    }

                    if (actionItem.Timestamp == default)
                    {
                        Skip(actionItem, "Sync action has no valid timestamp.");
                        continue;
                    }

                    var board = await _context.Boards
                        .Include(b => b.Collaborators)
                        .Include(b => b.Items)
                        .FirstOrDefaultAsync(b => b.Id == actionItem.BoardId);

                    // Guard update permissions: if board exists, verify access
                    if (board != null && !_boardService.CanAccess(board, user))
                    {
                        Skip(actionItem, "Current user does not have access to this board.");
                        continue;
                    }

                    if (actionItem.Action == "create")
                    {
                        if (board != null)
                        {
                            Skip(actionItem, "Board already exists; create action was ignored.");
                            continue;
                        }

                        if (!actionItem.Payload.HasValue)
                        {
                            Skip(actionItem, "Create action is missing a board payload.");
                            continue;
                        }

                        try
                        {
                            // Deserialize payload as board creation details
                            var payloadJson = actionItem.Payload.Value.GetRawText();
                            var createReq = JsonSerializer.Deserialize<CreateBoardRequest>(payloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (createReq == null || !IsValidCreateBoardRequest(createReq))
                            {
                                Skip(actionItem, "Create action has an invalid board payload.");
                                continue;
                            }

                            var newBoard = new Board
                            {
                                Id = actionItem.BoardId,
                                Title = createReq.Title,
                                Description = createReq.Description ?? "",
                                Category = createReq.Category ?? "Personal",
                                OwnerId = user.Id,
                                IsShared = createReq.IsShared,
                                CreatedAt = actionItem.Timestamp,
                                UpdatedAt = actionItem.Timestamp
                            };

                            if (createReq.IsShared && createReq.Collaborators != null)
                            {
                                newBoard.Collaborators = _boardService.BuildCollaborators(newBoard.Id, createReq.Collaborators);
                            }

                            _context.Boards.Add(newBoard);
                            await _context.SaveChangesAsync(); // save initial create to establish relations
                            appliedCount++;

                            activityLogsToSave.Add(new ActivityLog
                            {
                                BoardId = newBoard.Id,
                                UserEmail = BuildActivityActor(user),
                                ActionDescription = "Created a board.",
                                Timestamp = actionItem.Timestamp
                            });

                            _logger.LogInformation("Applied sync create for board {BoardId}, user {UserId}", newBoard.Id, user.Id);
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogWarning(ex, "Malformed create payload for board {BoardId}, user {UserId}", actionItem.BoardId, user.Id);
                            Skip(actionItem, "Create action has malformed JSON payload.");
                        }
                    }
                    else if (actionItem.Action == "update")
                    {
                        if (board == null)
                        {
                            Skip(actionItem, "Board does not exist; update action was ignored.");
                            continue;
                        }

                        if (!actionItem.Payload.HasValue)
                        {
                            Skip(actionItem, "Update action is missing a board payload.");
                            continue;
                        }

                        // Verify client change is more recent than database Baseline
                        if (actionItem.Timestamp <= board.UpdatedAt)
                        {
                            Skip(actionItem, "Board update is stale and was not applied.", addActivityLog: true);
                            continue;
                        }

                        try
                        {
                            var payloadJson = actionItem.Payload.Value.GetRawText();
                            var updateReq = JsonSerializer.Deserialize<UpdateBoardRequest>(payloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (updateReq == null || !IsValidUpdateBoardRequest(updateReq))
                            {
                                Skip(actionItem, "Update action has an invalid board payload.");
                                continue;
                            }

                            if (!_boardService.IsOwner(board, user) && _boardService.HasBoardSettingsChanges(updateReq))
                            {
                                Skip(actionItem, "Collaborators cannot update board settings or collaborators.", addActivityLog: true);
                                continue;
                            }

                            if (_boardService.IsOwner(board, user))
                            {
                                if (updateReq.Title != null) board.Title = updateReq.Title;
                                if (updateReq.Description != null) board.Description = updateReq.Description;
                                if (updateReq.Category != null) board.Category = updateReq.Category;
                                if (updateReq.IsShared != null) board.IsShared = updateReq.IsShared.Value;
                            }

                            board.UpdatedAt = actionItem.Timestamp;

                            if (_boardService.IsOwner(board, user) && updateReq.Collaborators != null)
                            {
                                _context.BoardCollaborators.RemoveRange(board.Collaborators);
                                if (board.IsShared)
                                {
                                    board.Collaborators = _boardService.BuildCollaborators(board.Id, updateReq.Collaborators);
                                }
                            }

                            appliedCount++;
                            activityLogsToSave.Add(new ActivityLog
                            {
                                BoardId = board.Id,
                                UserEmail = BuildActivityActor(user),
                                ActionDescription = "Updated board settings.",
                                Timestamp = actionItem.Timestamp
                            });

                            _logger.LogInformation("Applied sync update for board {BoardId}, user {UserId}", board.Id, user.Id);
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogWarning(ex, "Malformed update payload for board {BoardId}, user {UserId}", actionItem.BoardId, user.Id);
                            Skip(actionItem, "Update action has malformed JSON payload.");
                        }
                    }
                    else if (actionItem.Action == "delete")
                    {
                        if (board == null)
                        {
                            Skip(actionItem, "Board does not exist; delete action was ignored.");
                            continue;
                        }

                        if (board.OwnerId != user.Id)
                        {
                            Skip(actionItem, "Only the board owner can delete the board.");
                            continue;
                        }

                        _logger.LogInformation("Applied sync delete for board {BoardId}, user {UserId}", board.Id, user.Id);
                        _context.Boards.Remove(board);
                        appliedCount++;
                    }
                    else if (actionItem.Action == "upsert_item")
                    {
                        if (board == null)
                        {
                            Skip(actionItem, "Board does not exist; item upsert was ignored.");
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(actionItem.ItemId))
                        {
                            Skip(actionItem, "Item upsert is missing an item id.");
                            continue;
                        }

                        if (!actionItem.Payload.HasValue)
                        {
                            Skip(actionItem, "Item upsert is missing an item payload.");
                            continue;
                        }

                        try
                        {
                            var payloadJson = actionItem.Payload.Value.GetRawText();
                            var itemDto = JsonSerializer.Deserialize<BoardItemResponse>(payloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (itemDto == null || !IsValidBoardItem(itemDto))
                            {
                                Skip(actionItem, "Item upsert has an invalid item payload.", addActivityLog: true);
                                continue;
                            }

                            var existingItem = board.Items.FirstOrDefault(it => it.Id == actionItem.ItemId);

                            if (existingItem != null && actionItem.Timestamp < existingItem.UpdatedAt)
                            {
                                Skip(actionItem, "Item update is stale and was not applied.", addActivityLog: true);
                                continue;
                            }

                            string desc;
                            if (existingItem != null)
                            {
                                existingItem.Type = _boardService.NormalizeBoardItemType(itemDto.Type);
                                existingItem.Title = itemDto.Title;
                                existingItem.Content = itemDto.Content;
                                existingItem.Caption = itemDto.Caption;
                                existingItem.Color = itemDto.Color;
                                existingItem.X = itemDto.X;
                                existingItem.Y = itemDto.Y;
                                existingItem.Width = itemDto.Width;
                                existingItem.Height = itemDto.Height;
                                existingItem.ZIndex = itemDto.ZIndex;
                                existingItem.UpdatedAt = actionItem.Timestamp;
                                existingItem.IsEncrypted = itemDto.IsEncrypted;

                                desc = $"Updated a {itemDto.Type} card.";
                            }
                            else
                            {
                                var newItem = new BoardItem
                                {
                                    Id = actionItem.ItemId,
                                    BoardId = board.Id,
                                    Type = _boardService.NormalizeBoardItemType(itemDto.Type),
                                    Title = itemDto.Title,
                                    Content = itemDto.Content,
                                    Caption = itemDto.Caption,
                                    Color = itemDto.Color,
                                    X = itemDto.X,
                                    Y = itemDto.Y,
                                    Width = itemDto.Width,
                                    Height = itemDto.Height,
                                    ZIndex = itemDto.ZIndex,
                                    CreatedAt = actionItem.Timestamp,
                                    UpdatedAt = actionItem.Timestamp,
                                    IsEncrypted = itemDto.IsEncrypted
                                };
                                board.Items.Add(newItem);

                                desc = $"Added a {itemDto.Type} card.";
                            }

                            board.UpdatedAt = actionItem.Timestamp;
                            appliedCount++;

                            activityLogsToSave.Add(new ActivityLog
                            {
                                BoardId = board.Id,
                                UserEmail = BuildActivityActor(user),
                                ActionDescription = desc,
                                Timestamp = actionItem.Timestamp
                            });

                            _logger.LogInformation(
                                "Applied sync item upsert for board {BoardId}, item {ItemId}, user {UserId}",
                                board.Id,
                                actionItem.ItemId,
                                user.Id);
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogWarning(ex, "Malformed item payload for board {BoardId}, item {ItemId}, user {UserId}", actionItem.BoardId, actionItem.ItemId, user.Id);
                            Skip(actionItem, "Item upsert has malformed JSON payload.", addActivityLog: true);
                        }
                    }
                    else if (actionItem.Action == "delete_item")
                    {
                        if (board == null)
                        {
                            Skip(actionItem, "Board does not exist; item delete was ignored.");
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(actionItem.ItemId))
                        {
                            Skip(actionItem, "Item delete is missing an item id.");
                            continue;
                        }

                        var existingItem = board.Items.FirstOrDefault(it => it.Id == actionItem.ItemId);
                        if (existingItem == null)
                        {
                            Skip(actionItem, "Item does not exist; delete action was ignored.", addActivityLog: true);
                            continue;
                        }

                        _context.BoardItems.Remove(existingItem);
                        board.UpdatedAt = actionItem.Timestamp;
                        appliedCount++;

                        activityLogsToSave.Add(new ActivityLog
                        {
                            BoardId = board.Id,
                            UserEmail = BuildActivityActor(user),
                            ActionDescription = "Deleted a card.",
                            Timestamp = actionItem.Timestamp
                        });

                        _logger.LogInformation(
                            "Applied sync item delete for board {BoardId}, item {ItemId}, user {UserId}",
                            board.Id,
                            actionItem.ItemId,
                            user.Id);
                    }
                }

                if (activityLogsToSave.Any())
                {
                    _context.ActivityLogs.AddRange(activityLogsToSave);
                }

                await _context.SaveChangesAsync();

                // Retrieve all updated boards matching user permissions to push back to client
                var updatedBoards = await _context.Boards
                    .Include(b => b.Collaborators)
                    .Include(b => b.Items)
                    .Where(b => b.OwnerId == user.Id || b.Collaborators.Any(c => c.CollaboratorEmail == emailLower))
                    .OrderByDescending(b => b.UpdatedAt)
                    .ToListAsync();

                var responseDtos = updatedBoards.Select(_boardService.MapToDto).ToList();
                _logger.LogInformation(
                    "Completed sync for user {UserId}: {AppliedCount} applied, {SkippedCount} skipped",
                    user.Id,
                    appliedCount,
                    skippedActions.Count);

                return Ok(new SyncResponse(
                    true,
                    responseDtos,
                    DateTime.UtcNow,
                    appliedCount,
                    skippedActions.Count,
                    warnings.Distinct().ToList(),
                    skippedActions));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync failed for user {UserId}", user.Id);
                return StatusCode(500, new { error = "Failed to process database synchronization." });
            }
        }

        private static bool IsSupportedSyncAction(string? action)
        {
            return action is "create" or "update" or "delete" or "upsert_item" or "delete_item";
        }

        private static string BuildActivityActor(User user)
        {
            return user.Id.ToString();
        }

        private static bool IsValidCreateBoardRequest(CreateBoardRequest request)
        {
            return !string.IsNullOrWhiteSpace(request.Title) &&
                request.Title.Length <= 120 &&
                (request.Description == null || request.Description.Length <= 500) &&
                (request.Category == null || request.Category.Length <= 80) &&
                (request.Collaborators == null || request.Collaborators.Count <= 25) &&
                !DtoValidation.ValidateCollaboratorEmails(request.Collaborators).Any();
        }

        private static bool IsValidUpdateBoardRequest(UpdateBoardRequest request)
        {
            return (request.Title == null || (!string.IsNullOrWhiteSpace(request.Title) && request.Title.Length <= 120)) &&
                (request.Description == null || request.Description.Length <= 500) &&
                (request.Category == null || request.Category.Length <= 80) &&
                (request.Collaborators == null || request.Collaborators.Count <= 25) &&
                (request.Items == null || request.Items.Count <= 200) &&
                (request.Items == null || request.Items.All(IsValidBoardItem)) &&
                !DtoValidation.ValidateCollaboratorEmails(request.Collaborators).Any();
        }

        private static bool IsValidBoardItem(BoardItemResponse item)
        {
            return !string.IsNullOrWhiteSpace(item.Id) &&
                item.Id.Length <= 120 &&
                item.Type is "quote" or "note" or "image" or "text" &&
                !string.IsNullOrWhiteSpace(item.Title) &&
                item.Title.Length <= 120 &&
                (item.Caption == null || item.Caption.Length <= 500) &&
                (item.Color == null || item.Color.Length <= 200) &&
                item.X >= 0 && item.X <= 5000 &&
                item.Y >= 0 && item.Y <= 5000 &&
                item.Width >= 5 && item.Width <= 200 &&
                item.Height >= 5 && item.Height <= 100 &&
                item.ZIndex >= -1000 && item.ZIndex <= 10000;
        }
    }
}
