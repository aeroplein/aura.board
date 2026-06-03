using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DigitalVisionBoard.Models
{
    // --- AUTHENTICATION DTOS ---
    public record RegisterRequest(
        [param: Required, EmailAddress, StringLength(254)] string Email,
        [param: Required, MinLength(8), StringLength(128)] string Password,
        [param: Required, StringLength(80, MinimumLength = 2)] string Name
    );

    public record LoginRequest(
        [param: Required, EmailAddress, StringLength(254)] string Email,
        [param: Required, StringLength(128)] string Password
    );

    public record UserPreferencesDto(bool DarkMode, bool NotificationsEnabled, bool HighContrast);
    public record UserResponse(Guid Id, string Email, string Name, UserPreferencesDto Preferences);
    public record AuthResponse(UserResponse User, DateTime ExpiresAt)
    {
        [JsonIgnore]
        public string Token { get; init; } = string.Empty;

        public AuthResponse(UserResponse user, DateTime expiresAt, string token)
            : this(user, expiresAt)
        {
            Token = token;
        }
    }

    // --- BOARD DTOS ---
    public record BoardItemResponse(
        [param: Required, StringLength(120)] string Id,
        [param: Required, RegularExpression("^(quote|note|image|text)$")] string Type,
        [param: Required, StringLength(120)] string Title,
        string? Content,
        [param: StringLength(500)]
        string? Caption,
        [param: StringLength(200)]
        string? Color,
        [param: Range(0, 5000)]
        double X,
        [param: Range(0, 5000)]
        double Y,
        [param: Range(5, 100)]
        int Width,
        [param: Range(5, 100)]
        int Height,
        [param: Range(-1000, 10000)]
        int ZIndex,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        bool IsEncrypted
    );

    public record BoardResponse(
        Guid Id,
        string Title,
        string? Description,
        string? Category,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        Guid OwnerId,
        bool IsShared,
        List<string> Collaborators,
        List<BoardItemResponse> Items
    );

    public record CreateBoardRequest(
        [param: Required, StringLength(120, MinimumLength = 1)]
        string Title,
        [param: StringLength(500)]
        string? Description,
        [param: StringLength(80)]
        string? Category,
        bool IsShared,
        [param: MaxLength(25)]
        List<string>? Collaborators
    ) : IValidatableObject
    {
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            foreach (var result in DtoValidation.ValidateCollaboratorEmails(Collaborators))
            {
                yield return result;
            }
        }
    }

    public record UpdateBoardRequest(
        [param: StringLength(120, MinimumLength = 1)]
        string? Title,
        [param: StringLength(500)]
        string? Description,
        [param: StringLength(80)]
        string? Category,
        bool? IsShared,
        [param: MaxLength(25)]
        List<string>? Collaborators,
        [param: MaxLength(200)]
        List<BoardItemResponse>? Items
    ) : IValidatableObject
    {
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            foreach (var result in DtoValidation.ValidateCollaboratorEmails(Collaborators))
            {
                yield return result;
            }
        }
    }

    // --- SYNC ENGINE DTOS ---
    public record SyncActionDto(
        string Action, // 'create', 'update', 'delete', 'upsert_item', 'delete_item'
        Guid BoardId,
        string? ItemId,
        System.Text.Json.JsonElement? Payload, // Can be BoardResponse or BoardItemResponse
        DateTime Timestamp
    );

    public record SyncRequest(
        List<SyncActionDto> Queue,
        DateTime ClientTimestamp
    );

    public record SyncResponse(
        bool Success,
        List<BoardResponse> Boards,
        DateTime Timestamp
    );

    // --- AI / GEMINI DTOS ---
    public record RecommendationsRequest(
        [param: Required, StringLength(120, MinimumLength = 1)]
        string Title,
        [param: StringLength(500)]
        string? Description,
        [param: StringLength(80)]
        string? Category,
        [param: MaxLength(200)]
        List<BoardItemResponse> Items
    );

    public record InspirationRequest(
        [param: Required, StringLength(120, MinimumLength = 1)]
        string Theme
    );

    public record UploadRequest(
        [param: Required, MinLength(1)] string Base64Data,
        [param: RegularExpression("^image/(jpeg|png|webp|gif)$")] string? MimeType,
        [param: StringLength(160)] string? FileName
    );

    internal static class DtoValidation
    {
        public static IEnumerable<ValidationResult> ValidateCollaboratorEmails(List<string>? collaborators)
        {
            if (collaborators == null)
            {
                yield break;
            }

            var emailAttribute = new EmailAddressAttribute();
            foreach (var collaborator in collaborators)
            {
                if (string.IsNullOrWhiteSpace(collaborator) || !emailAttribute.IsValid(collaborator.Trim()))
                {
                    yield return new ValidationResult(
                        "Collaborators must be valid email addresses.",
                        new[] { nameof(CreateBoardRequest.Collaborators) }
                    );
                    yield break;
                }
            }
        }
    }
}
