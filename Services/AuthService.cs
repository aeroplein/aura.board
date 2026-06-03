using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalVisionBoard.Data;
using DigitalVisionBoard.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DigitalVisionBoard.Services
{
    public class AuthService
    {
        public const string AuthCookieName = "vision_board_auth";
        public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(8);

        private const int LegacyIterations = 1_000;
        private const int CurrentIterations = 210_000;
        private const int SaltBytesLength = 16;
        private const int HashBytesLength = 64;

        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthService(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
        {
            var email = NormalizeEmail(request.Email);

            if (await _context.Users.AnyAsync(u => u.Email == email))
            {
                throw new InvalidOperationException("An account with this email already exists.");
            }

            var (salt, passwordHash) = HashPassword(request.Password);
            var user = new User
            {
                Email = email,
                Name = request.Name.Trim(),
                PasswordHash = passwordHash,
                Salt = salt
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            _context.Boards.Add(CreateDefaultUserBoard(user.Id));
            await _context.SaveChangesAsync();

            return CreateAuthResponse(user);
        }

        public async Task<AuthResponse?> LoginAsync(LoginRequest request)
        {
            var email = NormalizeEmail(request.Email);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null || !VerifyPassword(request.Password, user, out var needsRehash))
            {
                return null;
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
            var userResponse = new UserResponse(user.Id, user.Email, user.Name, preferencesDto);

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
