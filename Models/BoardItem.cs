using System;
using System.Text.Json.Serialization;

namespace DigitalVisionBoard.Models
{
    public class BoardItem
    {
        // Custom string ID supports client-side formats (e.g. 'ai-item-xxxx', 'gallery-item-xxxx')
        public required string Id { get; set; }
        
        public Guid BoardId { get; set; }
        
        [JsonIgnore]
        public Board? Board { get; set; }

        public required string Type { get; set; } // 'quote', 'note', 'image', 'text'
        public required string Title { get; set; }
        public string? Content { get; set; }
        public string? Caption { get; set; }
        public string ImageDisplayMode { get; set; } = "card";
        public string? Color { get; set; }
        
        public double X { get; set; }
        public double Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int ZIndex { get; set; } = 10;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        public bool IsEncrypted { get; set; } = false;
    }
}
