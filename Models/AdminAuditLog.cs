namespace DigitalVisionBoard.Models
{
    public class AdminAuditLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? AdminUserId { get; set; }
        public required string AdminEmail { get; set; }
        public Guid? TargetUserId { get; set; }
        public string? TargetEmail { get; set; }
        public required string Action { get; set; }
        public string? Details { get; set; }
        public bool Success { get; set; } = true;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
