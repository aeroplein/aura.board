using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DigitalVisionBoard.Models
{
    public class User
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public required string Email { get; set; }
        public required string Name { get; set; }
        public string? Username { get; set; }
        public string? AvatarUrl { get; set; }
        public required string PasswordHash { get; set; }
        public required string Salt { get; set; }
        public bool IsAdmin { get; set; } = false;
        public bool IsEmailVerified { get; set; } = false;
        public string? EmailVerificationToken { get; set; }
        public DateTime? EmailVerificationExpires { get; set; }

        // User Preferences stored flat in the same table
        public bool DarkMode { get; set; } = false;
        public bool NotificationsEnabled { get; set; } = true;
        public bool HighContrast { get; set; } = false;

        [JsonIgnore]
        public ICollection<Board> Boards { get; set; } = new List<Board>();
    }
}
