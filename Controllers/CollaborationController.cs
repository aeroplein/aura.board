using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DigitalVisionBoard.Data;
using DigitalVisionBoard.Models;
using DigitalVisionBoard.Services;

namespace DigitalVisionBoard.Controllers
{
    [Route("api/boards/activity")]
    public class CollaborationController : BaseApiController
    {
        public CollaborationController(AppDbContext context, AuthService authService) : base(context, authService)
        {
        }

        [HttpGet]
        public async Task<IActionResult> GetActivityFeed([FromQuery] Guid? boardId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { error = "Unauthorized: Invalid or expired token." });
            }

            var emailLower = user.Email.Trim().ToLowerInvariant();

            // 1. Get the list of board IDs the user is allowed to access
            var accessibleBoardIds = await _context.Boards
                .Where(b => b.OwnerId == user.Id || b.Collaborators.Any(c => c.CollaboratorEmail == emailLower))
                .Select(b => b.Id)
                .ToListAsync();

            // 2. If a specific boardId is requested, verify the user has access to it
            if (boardId.HasValue)
            {
                if (!accessibleBoardIds.Contains(boardId.Value))
                {
                    return StatusCode(403, new { error = "Forbidden: You do not have access to this board's activity." });
                }

                // Filter down to only that board
                accessibleBoardIds = new List<Guid> { boardId.Value };
            }

            // 3. Fetch the latest 5 logs matching accessible boards
            var activityLogs = await _context.ActivityLogs
                .Where(al => accessibleBoardIds.Contains(al.BoardId))
                .OrderByDescending(al => al.Timestamp)
                .Take(5)
                .Select(al => new {
                    al.Id,
                    al.BoardId,
                    al.UserEmail,
                    al.ActionDescription,
                    al.Timestamp
                })
                .ToListAsync();

            return Ok(activityLogs);
        }
    }
}
