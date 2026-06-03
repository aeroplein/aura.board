using System;
using System.Collections.Generic;

namespace DigitalVisionBoard.Models
{
    public class Board
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public required string Title { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public Guid OwnerId { get; set; }
        public User? Owner { get; set; }

        public bool IsShared { get; set; } = false;

        public ICollection<BoardCollaborator> Collaborators { get; set; } = new List<BoardCollaborator>();
        public ICollection<BoardItem> Items { get; set; } = new List<BoardItem>();
    }
}
