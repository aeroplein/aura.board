using DigitalVisionBoard.Data;
using DigitalVisionBoard.Models;
using DigitalVisionBoard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigitalVisionBoard.Controllers
{
    public class UploadController : BaseApiController
    {
        private readonly ImageStorageService _imageStorageService;
        private readonly BoardService _boardService;
        private readonly ILogger<UploadController> _logger;

        public UploadController(
            AppDbContext context,
            AuthService authService,
            ImageStorageService imageStorageService,
            BoardService boardService,
            ILogger<UploadController> logger) : base(context, authService)
        {
            _imageStorageService = imageStorageService;
            _boardService = boardService;
            _logger = logger;
        }

        [HttpPost("api/upload")]
        public async Task<IActionResult> UploadImage([FromBody] UploadRequest request)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { error = "Unauthorized: Invalid or expired token." });
            }

            try
            {
                var imageFile = await _imageStorageService.SaveBase64ImageAsync(request, user.Id);
                return Ok(new { url = $"/api/images/{imageFile.Id}" });
            }
            catch (InvalidDataException ex)
            {
                _logger.LogWarning(ex, "Image upload rejected for user {UserId}", user.Id);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Image upload failed for user {UserId}", user.Id);
                return StatusCode(500, new { error = "Internal system failed to store image." });
            }
        }

        [HttpGet("api/images/{id}")]
        public async Task<IActionResult> GetImage(Guid id)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { error = "Unauthorized: Invalid or expired token." });
            }

            try
            {
                var imageFile = await _context.ImageFiles.FindAsync(id);
                if (imageFile == null)
                {
                    return NotFound(new { error = "Image not found." });
                }

                if (!await CanReadImageAsync(imageFile, user))
                {
                    _logger.LogWarning("User {UserId} attempted to read image {ImageId} without access.", user.Id, id);
                    return StatusCode(403, new { error = "Forbidden: You do not have access to this image." });
                }

                byte[] buffer;
                try
                {
                    buffer = Convert.FromBase64String(imageFile.Base64Data);
                }
                catch (FormatException ex)
                {
                    _logger.LogError(ex, "Stored image {ImageId} contains invalid base64.", id);
                    return StatusCode(500, new { error = "Image data is unavailable." });
                }

                return File(buffer, imageFile.MimeType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Image retrieval failed for image {ImageId}", id);
                return StatusCode(500, new { error = "Internal system failed to retrieve image." });
            }
        }

        private async Task<bool> CanReadImageAsync(ImageFile imageFile, User user)
        {
            if (imageFile.UploaderUserId == user.Id)
            {
                return true;
            }

            var imageUrl = $"/api/images/{imageFile.Id}";
            var emailLower = _boardService.NormalizeEmail(user.Email);

            return await _context.Boards.AnyAsync(board =>
                (board.OwnerId == user.Id || board.Collaborators.Any(c => c.CollaboratorEmail == emailLower)) &&
                board.Items.Any(item => item.Content == imageUrl || item.Caption == imageUrl));
        }
    }
}
