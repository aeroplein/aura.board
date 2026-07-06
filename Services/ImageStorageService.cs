using DigitalVisionBoard.Data;
using DigitalVisionBoard.Models;

namespace DigitalVisionBoard.Services
{
    public class ImageStorageService
    {
        private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp",
            "image/gif"
        };

        private const int MaxUploadBytes = 15 * 1024 * 1024;
        private readonly AppDbContext _context;

        public ImageStorageService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<ImageFile> SaveBase64ImageAsync(UploadRequest request, Guid uploaderUserId)
        {
            var pureBase64 = request.Base64Data;
            var mimeType = request.MimeType ?? "image/png";
            var commaIndex = pureBase64.IndexOf(',');

            if (commaIndex != -1)
            {
                var mimePart = pureBase64.Substring(0, commaIndex);
                if (mimePart.StartsWith("data:", StringComparison.OrdinalIgnoreCase) &&
                    mimePart.Contains(";base64", StringComparison.OrdinalIgnoreCase))
                {
                    var parsedMime = mimePart.Substring(5).Split(';')[0];
                    if (!string.IsNullOrWhiteSpace(parsedMime))
                    {
                        mimeType = parsedMime;
                    }
                }

                pureBase64 = pureBase64.Substring(commaIndex + 1);
            }

            if (!AllowedMimeTypes.Contains(mimeType))
            {
                throw new InvalidDataException("Only JPG, PNG, WebP, and GIF images are supported.");
            }

            byte[] buffer;
            try
            {
                buffer = Convert.FromBase64String(pureBase64);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("Invalid base64 image data.", ex);
            }

            if (buffer.Length > MaxUploadBytes)
            {
                throw new InvalidDataException("Upload exceeds 15MB limit.");
            }

            var imageFile = new ImageFile
            {
                Id = Guid.NewGuid(),
                UploaderUserId = uploaderUserId,
                Base64Data = pureBase64,
                MimeType = mimeType,
                CreatedAt = DateTime.UtcNow
            };

            _context.ImageFiles.Add(imageFile);
            await _context.SaveChangesAsync();

            return imageFile;
        }
    }
}
