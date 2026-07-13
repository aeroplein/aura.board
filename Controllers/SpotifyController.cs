using DigitalVisionBoard.Data;
using DigitalVisionBoard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DigitalVisionBoard.Controllers
{
    public class SpotifyController : BaseApiController
    {
        private readonly SpotifyService _spotifyService;
        private readonly ILogger<SpotifyController> _logger;

        public SpotifyController(
            AppDbContext context,
            AuthService authService,
            SpotifyService spotifyService,
            ILogger<SpotifyController> logger) : base(context, authService)
        {
            _spotifyService = spotifyService;
            _logger = logger;
        }

        [HttpGet("api/spotify/search")]
        [EnableRateLimiting("provider")]
        public async Task<IActionResult> Search([FromQuery] string? q, CancellationToken cancellationToken)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { error = "Unauthorized: Invalid or expired token." });
            }

            var query = q?.Trim();
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            {
                return BadRequest(new { error = "Search for at least 2 characters." });
            }

            if (query.Length > 120)
            {
                return BadRequest(new { error = "Search query is too long." });
            }

            try
            {
                var tracks = await _spotifyService.SearchTracksAsync(query, cancellationToken);
                return Ok(new { tracks });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Spotify search could not be completed for user {UserId}.", user.Id);
                return StatusCode(503, new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Spotify search failed for user {UserId}.", user.Id);
                return StatusCode(500, new { error = "Spotify search failed." });
            }
        }
    }
}
