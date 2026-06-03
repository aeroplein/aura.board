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
        public required string PasswordHash { get; set; }
        public required string Salt { get; set; }

        // User Preferences stored flat in the same table
        public bool DarkMode { get; set; } = false;
        public bool NotificationsEnabled { get; set; } = true;
        public bool HighContrast { get; set; } = false;

        [JsonIgnore]
        public ICollection<Board> Boards { get; set; } = new List<Board>();
    }
}
