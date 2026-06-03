using DigitalVisionBoard.Data;
using DigitalVisionBoard.Models;
using DigitalVisionBoard.Services;
using Microsoft.AspNetCore.Mvc;

namespace DigitalVisionBoard.Controllers
{
    public class UploadController : BaseApiController
    {
        private readonly ImageStorageService _imageStorageService;
        private readonly ILogger<UploadController> _logger;

        public UploadController(
            AppDbContext context,
            AuthService authService,
            ImageStorageService imageStorageService,
            ILogger<UploadController> logger) : base(context, authService)
        {
            _imageStorageService = imageStorageService;
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
                var imageFile = await _imageStorageService.SaveBase64ImageAsync(request);
                return Ok(new { url = $"/api/images/{imageFile.Id}" });
            }
            catch (InvalidDataException ex)
            {
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
            try
            {
                var imageFile = await _context.ImageFiles.FindAsync(id);
                if (imageFile == null)
                {
                    return NotFound(new { error = "Image not found." });
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
    }
}
