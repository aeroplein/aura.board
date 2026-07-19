using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DigitalVisionBoard.Data;
using DigitalVisionBoard.Models;
using DigitalVisionBoard.Services;

namespace DigitalVisionBoard.Controllers
{
    [Route("api/boards")]
    public class BoardsController : BaseApiController
    {
        private readonly IInviteEmailService _inviteEmailService;
        private readonly IConfiguration _configuration;
        private readonly BoardService _boardService;
        private readonly ILogger<BoardsController> _logger;

        public BoardsController(
            AppDbContext context,
            AuthService authService,
            IInviteEmailService inviteEmailService,
            IConfiguration configuration,
            BoardService boardService,
            ILogger<BoardsController> logger) : base(context, authService)
        {
            _inviteEmailService = inviteEmailService;
            _configuration = configuration;
            _boardService = boardService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetBoards([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { error = "Unauthorized: Invalid or expired token." });
            }

            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 50);
            var emailLower = _boardService.NormalizeEmail(user.Email);

            // Fetch boards where user is the owner or listed as a collaborator
            var query = _context.Boards
                .Where(b => b.OwnerId == user.Id || b.Collaborators.Any(c => c.CollaboratorEmail == emailLower));

            var totalCount = await query.CountAsync();
            Response.Headers["X-Total-Count"] = totalCount.ToString();
            Response.Headers["X-Page"] = page.ToString();
            Response.Headers["X-Page-Size"] = pageSize.ToString();

            var boards = await query
                .OrderByDescending(b => b.UpdatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Include(b => b.Collaborators)
                .Include(b => b.Items)
                .ToListAsync();

            var response = boards.Select(_boardService.MapToDto).ToList();
            return Ok(response);
        }

        [HttpPost]
        public async Task<IActionResult> CreateBoard([FromBody] CreateBoardRequest request)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { error = "Unauthorized: Invalid or expired token." });
            }

            var newBoard = new Board
            {
                Id = Guid.NewGuid(),
                Title = request.Title,
                Description = request.Description ?? "",
                Category = request.Category ?? "Personal",
                OwnerId = user.Id,
                IsShared = request.IsShared,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            if (request.IsShared && request.Collaborators != null)
            {
                newBoard.Collaborators = _boardService.BuildCollaborators(newBoard.Id, request.Collaborators);
            }

            _context.Boards.Add(newBoard);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created board {BoardId} for user {UserId}", newBoard.Id, user.Id);

            // Return board response mapping (no items initially)
            var response = _boardService.MapToDto(newBoard);
            return CreatedAtAction(nameof(GetBoardById), new { id = newBoard.Id }, response);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetBoardById(Guid id)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { error = "Unauthorized: Invalid or expired token." });
            }

            var board = await _context.Boards
                .Include(b => b.Collaborators)
                .Include(b => b.Items)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (board == null)
            {
                return NotFound(new { error = "Board not found." });
            }

            if (!_boardService.CanAccess(board, user))
            {
                return StatusCode(403, new { error = "Forbidden: You do not have access to this board." });
            }

            return Ok(_boardService.MapToDto(board));
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateBoard(Guid id, [FromBody] UpdateBoardRequest request)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { error = "Unauthorized: Invalid or expired token." });
            }

            var board = await _context.Boards
                .Include(b => b.Collaborators)
                .Include(b => b.Items)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (board == null)
            {
                return NotFound(new { error = "Board not found." });
            }

            var isOwner = _boardService.IsOwner(board, user);
            var isCollaborator = _boardService.IsCollaborator(board, user);

            if (!isOwner && !isCollaborator)
            {
                _logger.LogWarning("User {UserId} attempted to modify inaccessible board {BoardId}", user.Id, id);
                return StatusCode(403, new { error = "Forbidden: You cannot modify this board." });
            }

            // Permission model: owners manage board settings/collaborators; collaborators edit board items only.
            if (!isOwner && _boardService.HasBoardSettingsChanges(request))
            {
                _logger.LogWarning("Collaborator {UserId} attempted board settings update for board {BoardId}", user.Id, id);
                return StatusCode(403, new { error = "Forbidden: Only the board owner can update settings or collaborators." });
            }

            if (isOwner)
            {
                if (request.Title != null) board.Title = request.Title;
                if (request.Description != null) board.Description = request.Description;
                if (request.Category != null) board.Category = request.Category;
                if (request.IsShared != null) board.IsShared = request.IsShared.Value;
            }

            board.UpdatedAt = DateTime.UtcNow;

            // Sync collaborators list
            if (isOwner && request.Collaborators != null)
            {
                // Remove existing collaborators
                _context.BoardCollaborators.RemoveRange(board.Collaborators);

                if (board.IsShared)
                {
                    board.Collaborators = _boardService.BuildCollaborators(board.Id, request.Collaborators);
                }
                else
                {
                    board.Collaborators = new List<BoardCollaborator>();
                }
            }

            // Sync canvas items
            if (request.Items != null)
            {
                // Remove existing board items
                _context.BoardItems.RemoveRange(board.Items);

                board.Items = request.Items
                    .Select(it => new BoardItem
                    {
                        Id = it.Id,
                        BoardId = board.Id,
                        Type = _boardService.NormalizeBoardItemType(it.Type),
                        Title = it.Title,
                        Content = it.Content,
                        Caption = it.Caption,
                        ImageDisplayMode = _boardService.NormalizeImageDisplayMode(it.ImageDisplayMode),
                        Color = it.Color,
                        X = it.X,
                        Y = it.Y,
                        Width = it.Width,
                        Height = it.Height,
                        ZIndex = it.ZIndex,
                        CreatedAt = it.CreatedAt == default ? DateTime.UtcNow : it.CreatedAt,
                        UpdatedAt = DateTime.UtcNow,
                        IsEncrypted = it.IsEncrypted
                    })
                    .ToList();
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation(
                "Updated board {BoardId} for user {UserId}; settingsChanged={SettingsChanged}; itemCount={ItemCount}",
                board.Id,
                user.Id,
                _boardService.HasBoardSettingsChanges(request),
                board.Items.Count);
            return Ok(_boardService.MapToDto(board));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteBoard(Guid id)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { error = "Unauthorized: Invalid or expired token." });
            }

            var board = await _context.Boards.FindAsync(id);
            if (board == null)
            {
                return NotFound(new { error = "Board not found." });
            }

            if (board.OwnerId != user.Id)
            {
                _logger.LogWarning("User {UserId} attempted to delete board {BoardId} owned by {OwnerId}", user.Id, id, board.OwnerId);
                return StatusCode(403, new { error = "Forbidden: Only the owner can delete the board." });
            }

            _context.Boards.Remove(board);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted board {BoardId} for user {UserId}", id, user.Id);

            return NoContent();
        }

        [HttpPost("{id}/invite")]
        public async Task<IActionResult> SendBoardInvites(Guid id)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { error = "Unauthorized: Invalid or expired token." });
            }

            var board = await _context.Boards
                .Include(b => b.Collaborators)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (board == null)
            {
                return NotFound(new { error = "Board not found." });
            }

            if (!_boardService.IsOwner(board, user))
            {
                return StatusCode(403, new { error = "Forbidden: Only the board owner can invite collaborators." });
            }

            if (!board.IsShared || !board.Collaborators.Any())
            {
                return BadRequest(new { error = "This board is not shared with any collaborators." });
            }

            var baseUrl = Environment.GetEnvironmentVariable("APP_URL")
                ?? _configuration["APP_URL"]
                ?? $"{Request.Scheme}://{Request.Host}";

            var result = await _inviteEmailService.SendBoardInviteAsync(
                board,
                user,
                board.Collaborators.Select(c => c.CollaboratorEmail),
                baseUrl
            );

            var boardUrl = $"{baseUrl.TrimEnd('/')}/?board={Uri.EscapeDataString(board.Id.ToString())}";
            _logger.LogInformation(
                "Prepared invites for board {BoardId} by user {UserId}; configured={Configured}; sentCount={SentCount}",
                board.Id,
                user.Id,
                result.IsConfigured,
                result.SentCount);

            return Ok(new
            {
                success = result.IsConfigured && result.SentCount > 0 && result.Error == null,
                configured = result.IsConfigured,
                sentCount = result.SentCount,
                recipients = result.Recipients,
                boardUrl,
                error = result.Error
            });
        }
    }
}
