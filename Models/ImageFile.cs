using System;

namespace DigitalVisionBoard.Models
{
    public class ImageFile
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? UploaderUserId { get; set; }
        public required string Base64Data { get; set; }
        public required string MimeType { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
