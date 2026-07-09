using DigitalVisionBoard.Models;

namespace DigitalVisionBoard.Services
{
    public class BoardService
    {
        public BoardResponse MapToDto(Board board)
        {
            return new BoardResponse(
                board.Id,
                board.Title,
                board.Description,
                board.Category,
                board.CreatedAt,
                board.UpdatedAt,
                board.OwnerId,
                board.IsShared,
                board.Collaborators.Select(c => c.CollaboratorEmail).ToList(),
                board.Items.Select(it => new BoardItemResponse(
                    it.Id,
                    it.Type,
                    it.Title,
                    it.Content,
                    it.Caption,
                    it.Color,
                    it.X,
                    it.Y,
                    it.Width,
                    it.Height,
                    it.ZIndex,
                    it.CreatedAt,
                    it.UpdatedAt,
                    it.IsEncrypted
                )).ToList()
            );
        }

        public bool CanAccess(Board board, User user)
        {
            return IsOwner(board, user) || IsCollaborator(board, user);
        }

        public bool IsOwner(Board board, User user)
        {
            return board.OwnerId == user.Id;
        }

        public bool IsCollaborator(Board board, User user)
        {
            var emailLower = NormalizeEmail(user.Email);
            return board.Collaborators.Any(c => c.CollaboratorEmail == emailLower);
        }

        public string NormalizeBoardItemType(string? type)
        {
            var normalized = (type ?? "").Trim().ToLowerInvariant();
            return normalized is "quote" or "note" or "image" or "text" or "music" ? normalized : "note";
        }

        public bool HasBoardSettingsChanges(UpdateBoardRequest request)
        {
            return request.Title != null ||
                request.Description != null ||
                request.Category != null ||
                request.IsShared != null ||
                request.Collaborators != null;
        }

        public List<BoardCollaborator> BuildCollaborators(Guid boardId, IEnumerable<string>? emails)
        {
            return (emails ?? Enumerable.Empty<string>())
                .Select(email => email.Trim().ToLowerInvariant())
                .Where(email => !string.IsNullOrWhiteSpace(email))
                .Distinct()
                .Select(email => new BoardCollaborator
                {
                    BoardId = boardId,
                    CollaboratorEmail = email
                })
                .ToList();
        }

        public string NormalizeEmail(string email)
        {
            return email.Trim().ToLowerInvariant();
        }
    }
}
