using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalVisionBoard.Data;
using DigitalVisionBoard.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalVisionBoard.Services
{
    public enum EmailVerificationResult
    {
        Success,
        MissingParameters,
        UserNotFound,
        AlreadyVerified,
        InvalidToken,
        Expired
    }

    public class EmailVerificationRequiredException : InvalidOperationException
    {
        public EmailVerificationRequiredException()
            : base("Please verify your email address before signing in.")
        {
        }
    }

    public class EmailVerificationDeliveryException : InvalidOperationException
    {
        public EmailVerificationDeliveryException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }

    public class AuthService
    {
        public const string AuthCookieName = "vision_board_auth";
        public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(8);
        public static readonly TimeSpan EmailVerificationTokenLifetime = TimeSpan.FromHours(24);

        private const int LegacyIterations = 1_000;
        private const int CurrentIterations = 210_000;
        private const int SaltBytesLength = 16;
        private const int HashBytesLength = 64;
        private const int EmailVerificationTokenBytesLength = 32;

        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IEmailService _emailService;
        private readonly IAdvancedEmailValidator _advancedEmailValidator;
        private readonly MailSettings _mailSettings;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            AppDbContext context,
            IConfiguration configuration,
            IEmailService emailService,
            IAdvancedEmailValidator advancedEmailValidator,
            IOptions<MailSettings> mailOptions,
            ILogger<AuthService> logger)
        {
            _context = context;
            _configuration = configuration;
            _emailService = emailService;
            _advancedEmailValidator = advancedEmailValidator;
            _mailSettings = mailOptions.Value;
            _logger = logger;
        }

        public async Task<RegistrationResponse> RegisterAsync(RegisterRequest request)
        {
            var email = NormalizeEmail(request.Email);
            var username = NormalizeUsername(request.Username);
            var emailValidation = await _advancedEmailValidator.ValidateAsync(email);
            if (!emailValidation.IsValid)
            {
                throw new AdvancedEmailValidationException(emailValidation.Error ?? "Email address is not allowed.");
            }

            if (await _context.Users.AnyAsync(u => u.Email == email))
            {
                throw new InvalidOperationException("An account with this email already exists.");
            }

            if (await _context.Users.AnyAsync(u => u.Username == username))
            {
                throw new InvalidOperationException("That username is already taken.");
            }

            var (salt, passwordHash) = HashPassword(request.Password);
            var emailVerificationToken = await GenerateUniqueEmailVerificationTokenAsync();
            var user = new User
            {
                Email = email,
                Name = request.Name.Trim(),
                Username = username,
                PasswordHash = passwordHash,
                Salt = salt,
                EmailVerificationToken = emailVerificationToken,
                EmailVerificationExpires = DateTime.UtcNow.Add(EmailVerificationTokenLifetime)
            };

            await using var transaction = await _context.Database.BeginTransactionAsync();

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            _context.Boards.Add(CreateDefaultUserBoard(user.Id));
            await _context.SaveChangesAsync();

            await SendVerificationEmailAsync(user);
            await transaction.CommitAsync();

            return new RegistrationResponse(
                user.Email,
                true,
                "Account created. Check your email and verify your address before signing in.");
        }

        public async Task<AuthResponse?> LoginAsync(LoginRequest request)
        {
            var email = NormalizeEmail(request.Email);
            if (!StrictEmailValidator.IsValid(email))
            {
                return null;
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null || !VerifyPassword(request.Password, user, out var needsRehash))
            {
                return null;
            }

            if (!user.IsEmailVerified)
            {
                throw new EmailVerificationRequiredException();
            }

            if (needsRehash)
            {
                var (salt, passwordHash) = HashPassword(request.Password);
                user.Salt = salt;
                user.PasswordHash = passwordHash;
                await _context.SaveChangesAsync();
            }

            return CreateAuthResponse(user);
        }

        public async Task<EmailVerificationResult> VerifyEmailAsync(string? email, string? token)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            {
                return EmailVerificationResult.MissingParameters;
            }

            var normalizedEmail = NormalizeEmail(email);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
            if (user == null)
            {
                return EmailVerificationResult.UserNotFound;
            }

            if (user.IsEmailVerified)
            {
                return EmailVerificationResult.AlreadyVerified;
            }

            if (string.IsNullOrWhiteSpace(user.EmailVerificationToken) ||
                !FixedTimeEquals(user.EmailVerificationToken, token.Trim()))
            {
                return EmailVerificationResult.InvalidToken;
            }

            if (!user.EmailVerificationExpires.HasValue || user.EmailVerificationExpires.Value <= DateTime.UtcNow)
            {
                return EmailVerificationResult.Expired;
            }

            user.IsEmailVerified = true;
            user.EmailVerificationToken = null;
            user.EmailVerificationExpires = null;
            await _context.SaveChangesAsync();

            return EmailVerificationResult.Success;
        }

        public async Task<User?> GetUserFromJwtAsync(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            var userId = ValidateJwtAndGetUserId(token.Trim());
            return userId.HasValue ? await _context.Users.FindAsync(userId.Value) : null;
        }

        private AuthResponse CreateAuthResponse(User user)
        {
            var expiresAt = DateTime.UtcNow.Add(TokenLifetime);
            var token = CreateJwt(user, expiresAt);
            var preferencesDto = new UserPreferencesDto(user.DarkMode, user.NotificationsEnabled, user.HighContrast);
            var userResponse = new UserResponse(user.Id, user.Email, user.Name, user.Username, user.AvatarUrl, preferencesDto, user.IsAdmin);

            return new AuthResponse(userResponse, expiresAt, token);
        }

        private string CreateJwt(User user, DateTime expiresAt)
        {
            var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
            {
                alg = "HS256",
                typ = "JWT"
            }));

            var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
            {
                sub = user.Id.ToString(),
                email = user.Email,
                name = user.Name,
                iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                exp = new DateTimeOffset(expiresAt).ToUnixTimeSeconds()
            }));

            var unsignedToken = $"{header}.{payload}";
            var signature = Sign(unsignedToken);
            return $"{unsignedToken}.{signature}";
        }

        private Guid? ValidateJwtAndGetUserId(string token)
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                return null;
            }

            var unsignedToken = $"{parts[0]}.{parts[1]}";
            var expectedSignature = Sign(unsignedToken);
            if (!FixedTimeEquals(parts[2], expectedSignature))
            {
                return null;
            }

            try
            {
                var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                using var payload = JsonDocument.Parse(payloadJson);
                var root = payload.RootElement;

                if (!root.TryGetProperty("exp", out var expElement) ||
                    expElement.GetInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                {
                    return null;
                }

                if (!root.TryGetProperty("sub", out var subElement) ||
                    !Guid.TryParse(subElement.GetString(), out var userId))
                {
                    return null;
                }

                return userId;
            }
            catch (JsonException)
            {
                return null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private string Sign(string value)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(GetJwtSecret()));
            return Base64UrlEncode(hmac.ComputeHash(Encoding.ASCII.GetBytes(value)));
        }

        private string GetJwtSecret()
        {
            var secret = Environment.GetEnvironmentVariable("JWT_SECRET") ?? _configuration["Jwt:Secret"];
            if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            {
                throw new InvalidOperationException("JWT secret must be configured with at least 32 characters.");
            }

            return secret;
        }

        private static (string Salt, string PasswordHash) HashPassword(string password)
        {
            var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(SaltBytesLength)).ToLowerInvariant();
            var hash = Pbkdf2Hex(password, salt, CurrentIterations);
            return (salt, $"pbkdf2_sha512${CurrentIterations}${salt}${hash}");
        }

        private async Task SendVerificationEmailAsync(User user)
        {
            if (string.IsNullOrWhiteSpace(user.EmailVerificationToken))
            {
                return;
            }

            var verificationUrl = BuildEmailVerificationUrl(user.Email, user.EmailVerificationToken);
            var subject = "Verify your Digital Vision Board email";
            var encodedName = WebUtility.HtmlEncode(user.Name);
            var encodedUrl = WebUtility.HtmlEncode(verificationUrl);
            var body = $"""
                <p>Hello {encodedName},</p>
                <p>Please verify your email address for Digital Vision Board.</p>
                <p><a href="{encodedUrl}">Verify email address</a></p>
                <p>This link expires in 24 hours.</p>
                """;

            try
            {
                await _emailService.SendEmailAsync(user.Email, subject, body);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("MailSettings must include", StringComparison.Ordinal))
            {
                _logger.LogWarning("Email verification message was not sent because SMTP settings are incomplete.");
                throw new EmailVerificationDeliveryException(
                    "Email verification could not be sent because SMTP settings are not configured.",
                    ex);
            }
            catch (Exception ex)
            {
                _logger.LogError("Email verification message failed for user {UserId}; ExceptionType {ExceptionType}", user.Id, ex.GetType().Name);
                throw new EmailVerificationDeliveryException(
                    "Email verification could not be sent. Please try again later.",
                    ex);
            }
        }

        private string BuildEmailVerificationUrl(string email, string token)
        {
            var baseUrl = string.IsNullOrWhiteSpace(_mailSettings.AppBaseUrl)
                ? "http://localhost:5000"
                : _mailSettings.AppBaseUrl.TrimEnd('/');

            return $"{baseUrl}/api/auth/verify-email?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        }

        private static string GenerateEmailVerificationToken()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(EmailVerificationTokenBytesLength)).ToLowerInvariant();
        }

        private async Task<string> GenerateUniqueEmailVerificationTokenAsync()
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var token = GenerateEmailVerificationToken();
                if (!await _context.Users.AnyAsync(u => u.EmailVerificationToken == token))
                {
                    return token;
                }
            }

            throw new InvalidOperationException("Could not generate a unique email verification token.");
        }

        private static bool VerifyPassword(string password, User user, out bool needsRehash)
        {
            needsRehash = false;

            if (user.PasswordHash.StartsWith("pbkdf2_sha512$", StringComparison.OrdinalIgnoreCase))
            {
                var parts = user.PasswordHash.Split('$');
                if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations))
                {
                    return false;
                }

                var expectedHash = Pbkdf2Hex(password, parts[2], iterations);
                needsRehash = iterations < CurrentIterations;
                return FixedTimeEquals(expectedHash, parts[3]);
            }

            var legacyHash = Pbkdf2Hex(password, user.Salt, LegacyIterations);
            needsRehash = true;
            return FixedTimeEquals(legacyHash, user.PasswordHash);
        }

        private static string Pbkdf2Hex(string password, string salt, int iterations)
        {
            var saltBytes = Encoding.UTF8.GetBytes(salt);
            using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, iterations, HashAlgorithmName.SHA512);
            return Convert.ToHexString(pbkdf2.GetBytes(HashBytesLength)).ToLowerInvariant();
        }

        private static string NormalizeEmail(string email)
        {
            return email.Trim().ToLowerInvariant();
        }

        public static string NormalizeUsername(string username)
        {
            return username.Trim().TrimStart('@').ToLowerInvariant();
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string value)
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            return Convert.FromBase64String(padded);
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            var leftBytes = Encoding.UTF8.GetBytes(left);
            var rightBytes = Encoding.UTF8.GetBytes(right);
            return leftBytes.Length == rightBytes.Length &&
                CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }

        private static Board CreateDefaultUserBoard(Guid ownerId)
        {
            var boardId = Guid.NewGuid();
            return new Board
            {
                Id = boardId,
                Title = "My Dream Journey",
                Description = "Welcome to your Vision Board! Arrange files, quotes, notes, and dreams. Move elements freely.",
                Category = "Personal Development",
                OwnerId = ownerId,
                IsShared = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Items = new List<BoardItem>
                {
                    new BoardItem
                    {
                        Id = Guid.NewGuid().ToString(),
                        BoardId = boardId,
                        Type = "quote",
                        Title = "Daily Inspiration",
                        Content = "The future belongs to those who believe in the beauty of their dreams.",
                        Color = "bg-indigo-50 dark:bg-indigo-950 border-indigo-200 dark:border-indigo-800",
                        X = 10,
                        Y = 10,
                        Width = 32,
                        Height = 20,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new BoardItem
                    {
                        Id = Guid.NewGuid().ToString(),
                        BoardId = boardId,
                        Type = "note",
                        Title = "Action Point",
                        Content = "- Read 1 chapter today\n- Stretch for 10 minutes\n- Drink 3L of water",
                        Color = "bg-amber-50 dark:bg-amber-950 border-amber-200 dark:border-amber-800",
                        X = 45,
                        Y = 15,
                        Width = 25,
                        Height = 28,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new BoardItem
                    {
                        Id = Guid.NewGuid().ToString(),
                        BoardId = boardId,
                        Type = "image",
                        Title = "Morning Serenity",
                        Content = "https://images.unsplash.com/photo-1506126613408-eca07ce68773?w=1000",
                        Caption = "Focusing on peace, meditation, and healthy daily habits.",
                        X = 20,
                        Y = 45,
                        Width = 35,
                        Height = 40,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }
                }
            };
        }
    }
}
