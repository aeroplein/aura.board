using System;
using System.Text.Json.Serialization;

namespace DigitalVisionBoard.Models
{
    public class BoardCollaborator
    {
        public Guid BoardId { get; set; }
        
        [JsonIgnore]
        public Board? Board { get; set; }
        
        public required string CollaboratorEmail { get; set; }
    }
}
