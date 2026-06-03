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

                // Sort queue by action sequence timestamp to ensure chronological updates
                var sortedQueue = request.Queue
                    .OrderBy(a => a.Timestamp)
                    .ToList();

                foreach (var actionItem in sortedQueue)
                {
                    var board = await _context.Boards
                        .Include(b => b.Collaborators)
                        .Include(b => b.Items)
                        .FirstOrDefaultAsync(b => b.Id == actionItem.BoardId);

                    // Guard update permissions: if board exists, verify access
                    if (board != null)
                    {
                        if (!_boardService.CanAccess(board, user))
                        {
                            continue; // Skip unauthorized actions in sync queue
                        }
                    }

                    if (actionItem.Action == "create")
                    {
                        if (board == null && actionItem.Payload.HasValue)
                        {
                            // Deserialize payload as board creation details
                            var payloadJson = actionItem.Payload.Value.GetRawText();
                            var createReq = JsonSerializer.Deserialize<CreateBoardRequest>(payloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (createReq != null)
                            {
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

                                activityLogsToSave.Add(new ActivityLog
                                {
                                    BoardId = newBoard.Id,
                                    UserEmail = user.Email,
                                    ActionDescription = $"Created board '{newBoard.Title}'",
                                    Timestamp = actionItem.Timestamp
                                });
                            }
                        }
                    }
                    else if (actionItem.Action == "update")
                    {
                        if (board != null && actionItem.Payload.HasValue)
                        {
                            // Verify client change is more recent than database Baseline
                            if (actionItem.Timestamp > board.UpdatedAt)
                            {
                                var payloadJson = actionItem.Payload.Value.GetRawText();
                                var updateReq = JsonSerializer.Deserialize<UpdateBoardRequest>(payloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                                if (updateReq != null)
                                {
                                    if (!_boardService.IsOwner(board, user) && _boardService.HasBoardSettingsChanges(updateReq))
                                    {
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

                                    activityLogsToSave.Add(new ActivityLog
                                    {
                                        BoardId = board.Id,
                                        UserEmail = user.Email,
                                        ActionDescription = $"Updated board parameters for '{board.Title}'",
                                        Timestamp = actionItem.Timestamp
                                    });
                                }
                            }
                        }
                    }
                    else if (actionItem.Action == "delete")
                    {
                        if (board != null && board.OwnerId == user.Id)
                        {
                            _context.Boards.Remove(board);
                        }
                    }
                    else if (actionItem.Action == "upsert_item")
                    {
                        if (board != null && !string.IsNullOrEmpty(actionItem.ItemId) && actionItem.Payload.HasValue)
                        {
                            var payloadJson = actionItem.Payload.Value.GetRawText();
                            var itemDto = JsonSerializer.Deserialize<BoardItemResponse>(payloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (itemDto != null)
                            {
                                var existingItem = board.Items.FirstOrDefault(it => it.Id == actionItem.ItemId);
                                bool shouldApply = true;

                                if (existingItem != null)
                                {
                                    // Conflict-Resolution: client changes override database if timestamp is newer
                                    if (actionItem.Timestamp < existingItem.UpdatedAt)
                                    {
                                        shouldApply = false;
                                    }
                                }

                                if (shouldApply)
                                {
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

                                        desc = $"Updated card '{itemDto.Title}' ({itemDto.Type})";
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

                                        desc = $"Added card '{itemDto.Title}' ({itemDto.Type})";
                                    }

                                    board.UpdatedAt = actionItem.Timestamp;

                                    activityLogsToSave.Add(new ActivityLog
                                    {
                                        BoardId = board.Id,
                                        UserEmail = user.Email,
                                        ActionDescription = desc,
                                        Timestamp = actionItem.Timestamp
                                    });
                                }
                            }
                        }
                    }
                    else if (actionItem.Action == "delete_item")
                    {
                        if (board != null && !string.IsNullOrEmpty(actionItem.ItemId))
                        {
                            var existingItem = board.Items.FirstOrDefault(it => it.Id == actionItem.ItemId);
                            if (existingItem != null)
                            {
                                _context.BoardItems.Remove(existingItem);
                                board.UpdatedAt = actionItem.Timestamp;

                                activityLogsToSave.Add(new ActivityLog
                                {
                                    BoardId = board.Id,
                                    UserEmail = user.Email,
                                    ActionDescription = $"Deleted card '{existingItem.Title}'",
                                    Timestamp = actionItem.Timestamp
                                });
                            }
                        }
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
                return Ok(new SyncResponse(true, responseDtos, DateTime.UtcNow));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync failed for user {UserId}", user.Id);
                return StatusCode(500, new { error = "Failed to process database synchronization." });
            }
        }
    }
}
