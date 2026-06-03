using System;

namespace DigitalVisionBoard.Models
{
    public class ActivityLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid BoardId { get; set; }
        
        public required string UserEmail { get; set; }
        public required string ActionDescription { get; set; }
        
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
