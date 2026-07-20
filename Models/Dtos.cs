using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DigitalVisionBoard.Models
{
    // --- AUTHENTICATION DTOS ---
    public record RegisterRequest(
        [param: Required, StrictEmailAddress, StringLength(254)] string Email,
        [param: Required, MinLength(8), StringLength(128)] string Password,
        [param: Required, StringLength(80, MinimumLength = 2)] string Name,
        [param: Required, StringLength(31, MinimumLength = 3), RegularExpression("^@?[A-Za-z0-9_.]+$")] string Username
    );

    public record LoginRequest(
        [param: Required, StrictEmailAddress, StringLength(254)] string Email,
        [param: Required, StringLength(128)] string Password
    );

    public record EmailRecoveryRequest(
        [param: Required, StrictEmailAddress, StringLength(254)] string Email
    );

    public record ResetPasswordRequest(
        [param: Required, StringLength(128)] string Token,
        [param: Required, MinLength(8), StringLength(128)] string Password
    );

    public record UserPreferencesDto(bool DarkMode, bool NotificationsEnabled, bool HighContrast);
    public record UserResponse(Guid Id, string Email, string Name, string? Username, string? AvatarUrl, UserPreferencesDto Preferences, bool IsAdmin);
    public record RegistrationResponse(string Email, bool VerificationEmailSent, string Message);
    public record UpdateProfileRequest(
        [param: Required, StringLength(80, MinimumLength = 2)] string Name,
        [param: StringLength(31, MinimumLength = 3), RegularExpression("^@?[A-Za-z0-9_.]+$")] string? Username,
        [param: StringLength(2048), OptionalAvatarUrl] string? AvatarUrl
    );
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

    // --- ADMINISTRATION DTOS ---
    public record AdminDashboardResponse(
        int TotalUsers,
        int VerifiedUsers,
        int PendingInvitations,
        int SuspendedUsers,
        int AdminUsers,
        int TotalBoards
    );

    public record AdminUserResponse(
        Guid Id,
        string Email,
        string Name,
        string? Username,
        bool IsEmailVerified,
        bool InvitationPending,
        bool IsSuspended,
        bool IsAdmin,
        int OwnedBoardCount,
        DateTime CreatedAt
    );

    public record AdminUsersPageResponse(
        IReadOnlyList<AdminUserResponse> Users,
        int Page,
        int PageSize,
        int TotalCount,
        int TotalPages
    );

    public record AdminAuditLogResponse(
        Guid Id,
        string AdminEmail,
        Guid? TargetUserId,
        string? TargetEmail,
        string Action,
        string? Details,
        bool Success,
        DateTime Timestamp
    );

    public record AdminInviteUserRequest(
        [param: Required, StrictEmailAddress, StringLength(254)] string Email,
        [param: Required, StringLength(80, MinimumLength = 2)] string Name
    );

    public record AdminRoleRequest(bool IsAdmin);

    public record AdminDeleteUserRequest(
        [param: Required, StrictEmailAddress, StringLength(254)] string ConfirmationEmail
    );

    // --- BOARD DTOS ---
    public record BoardItemResponse(
        [param: Required, StringLength(120)] string Id,
        [param: Required, RegularExpression("^(quote|note|image|text|music)$")] string Type,
        [param: Required, StringLength(120)] string Title,
        [param: StringLength(4096)]
        string? Content,
        [param: StringLength(500)]
        string? Caption,
        [param: RegularExpression("^(card|plain|captioned)$")]
        string? ImageDisplayMode,
        [param: StringLength(200)]
        string? Color,
        [param: Range(0, 5000)]
        double X,
        [param: Range(0, 5000)]
        double Y,
        [param: Range(5, 200)]
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
        [param: Required, RegularExpression("^(create|update|delete|upsert_item|delete_item)$")]
        string Action, // 'create', 'update', 'delete', 'upsert_item', 'delete_item'
        Guid BoardId,
        [param: StringLength(120)]
        string? ItemId,
        System.Text.Json.JsonElement? Payload, // Can be BoardResponse or BoardItemResponse
        DateTime Timestamp
    );

    public record SyncRequest(
        [param: Required, MaxLength(500)]
        List<SyncActionDto> Queue,
        DateTime ClientTimestamp
    );

    public record SyncResponse(
        bool Success,
        List<BoardResponse> Boards,
        DateTime Timestamp,
        int AppliedCount = 0,
        int SkippedCount = 0,
        List<string>? Warnings = null,
        List<SyncSkippedActionDto>? SkippedActions = null
    );

    public record SyncSkippedActionDto(
        string Action,
        Guid BoardId,
        string? ItemId,
        string Reason
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
        [param: Required, MinLength(1), MaxLength(21_000_000)] string Base64Data,
        [param: RegularExpression("^image/(jpeg|png|webp|gif)$")] string? MimeType,
        [param: StringLength(160)] string? FileName
    );

    public record SpotifyTrackSearchResult(
        string Id,
        string Name,
        string Artist,
        string? Album,
        string? ImageUrl,
        string SpotifyUrl,
        string EmbedUrl
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

    public sealed class StrictEmailAddressAttribute : ValidationAttribute
    {
        public StrictEmailAddressAttribute()
            : base("Use a real email address with a valid domain, such as name@example.com.")
        {
        }

        public override bool IsValid(object? value)
        {
            return value is string email && StrictEmailValidator.IsValid(email);
        }
    }

    public sealed class OptionalAvatarUrlAttribute : ValidationAttribute
    {
        public OptionalAvatarUrlAttribute()
            : base("Avatar URL must be HTTPS, HTTP for local development, or an uploaded /api/images/{id} path.")
        {
        }

        public override bool IsValid(object? value)
        {
            if (value is null)
            {
                return true;
            }

            if (value is not string raw || string.IsNullOrWhiteSpace(raw))
            {
                return true;
            }

            var avatarUrl = raw.Trim();
            if (avatarUrl.StartsWith("/api/images/", StringComparison.OrdinalIgnoreCase))
            {
                var idPart = avatarUrl["/api/images/".Length..];
                return Guid.TryParse(idPart, out _);
            }

            return Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttps ||
                    (uri.Scheme == Uri.UriSchemeHttp && IsLocalHost(uri.Host)));
        }

        private static bool IsLocalHost(string host)
        {
            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class StrictEmailValidator
    {
        private static readonly EmailAddressAttribute EmailAddress = new();

        public static bool IsValid(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

            email = email.Trim();
            if (!EmailAddress.IsValid(email))
            {
                return false;
            }

            var atIndex = email.LastIndexOf('@');
            if (atIndex <= 0 || atIndex == email.Length - 1)
            {
                return false;
            }

            var domain = email[(atIndex + 1)..].ToLowerInvariant();
            var labels = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (labels.Length < 2)
            {
                return false;
            }

            var topLevelDomain = labels[^1];
            if (topLevelDomain.Length < 2 || topLevelDomain.Any(c => !char.IsLetter(c)))
            {
                return false;
            }

            return labels.Take(labels.Length - 1).All(label =>
                label.Length >= 2 &&
                label.Length <= 63 &&
                label.All(c => char.IsLetterOrDigit(c) || c == '-') &&
                !label.StartsWith('-') &&
                !label.EndsWith('-'));
        }
    }
}
