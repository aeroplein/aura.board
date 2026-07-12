using DigitalVisionBoard.Data;
using DigitalVisionBoard.Models;
using DigitalVisionBoard.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace DigitalVisionBoard.Controllers
{
    [Route("api/auth")]
    public class AuthController : BaseApiController
    {
        private readonly AuthService _authService;
        private readonly ILogger<AuthController> _logger;
        private readonly IHostEnvironment _environment;

        public AuthController(
            AppDbContext context,
            AuthService authService,
            ILogger<AuthController> logger,
            IHostEnvironment environment)
            : base(context, authService)
        {
            _authService = authService;
            _logger = logger;
            _environment = environment;
        }

        [HttpPost("register")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                var response = await _authService.RegisterAsync(request);
                return Created("/api/auth/verify-email", response);
            }
            catch (EmailVerificationDeliveryException ex)
            {
                _logger.LogWarning(ex, "Registration could not send email verification.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogInformation(ex, "Registration rejected.");
                if (ex is AdvancedEmailValidationException)
                {
                    return BadRequest(new { error = ex.Message });
                }

                return Conflict(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registration failed.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Accounts are temporarily unavailable. Check the database configuration and try again." });
            }
        }

        [HttpPost("login")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                var response = await _authService.LoginAsync(request);
                if (response == null)
                {
                    // Keep the same response for unknown emails and wrong passwords to avoid account enumeration.
                    return BadRequest(new { error = "Invalid email or password." });
                }

                SetAuthCookie(response);
                return Ok(response);
            }
            catch (EmailVerificationRequiredException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login failed.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Accounts are temporarily unavailable. Check the database configuration and try again." });
            }
        }

        [HttpGet("session")]
        public async Task<IActionResult> GetSession()
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { error = "Unauthorized: Invalid or expired token." });
            }

            return Ok(new { user = ToUserResponse(user) });
        }

        [HttpGet("verify-email")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string? email, [FromQuery] string? token)
        {
            try
            {
                var result = await _authService.VerifyEmailAsync(email, token);
                return result switch
                {
                    EmailVerificationResult.Success => EmailVerificationPage(
                        StatusCodes.Status200OK,
                        "success",
                        "Your email is verified",
                        "Your Aura account is ready. Sign in to start creating your vision board."),
                    EmailVerificationResult.AlreadyVerified => EmailVerificationPage(
                        StatusCodes.Status409Conflict,
                        "info",
                        "This email is already verified",
                        "You can sign in and continue creating your vision board."),
                    EmailVerificationResult.MissingParameters => EmailVerificationPage(
                        StatusCodes.Status400BadRequest,
                        "error",
                        "This verification link is incomplete",
                        "Please use the complete link from your verification email."),
                    EmailVerificationResult.Expired => EmailVerificationPage(
                        StatusCodes.Status400BadRequest,
                        "error",
                        "This verification link has expired",
                        "For your security, verification links expire after 24 hours. Please register again to receive a new one."),
                    EmailVerificationResult.InvalidToken => EmailVerificationPage(
                        StatusCodes.Status400BadRequest,
                        "error",
                        "This verification link is invalid",
                        "Please use the latest verification email we sent you."),
                    EmailVerificationResult.UserNotFound => EmailVerificationPage(
                        StatusCodes.Status404NotFound,
                        "error",
                        "We couldn't find that account",
                        "Please check that you opened the link from the correct verification email."),
                    _ => EmailVerificationPage(
                        StatusCodes.Status400BadRequest,
                        "error",
                        "Email verification failed",
                        "Please try again using the link in your verification email.")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email verification failed.");
                return EmailVerificationPage(
                    StatusCodes.Status503ServiceUnavailable,
                    "error",
                    "Email verification is temporarily unavailable",
                    "Please try again in a few minutes.");
            }
        }

        private ViewResult EmailVerificationPage(int statusCode, string state, string title, string message)
        {
            Response.StatusCode = statusCode;
            ViewData["State"] = state;
            ViewData["Title"] = title;
            ViewData["Message"] = message;
            return View("~/Views/Home/EmailVerification.cshtml");
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            Response.Cookies.Delete(AuthService.AuthCookieName, CreateAuthCookieOptions(DateTimeOffset.UnixEpoch));
            return Ok(new { success = true });
        }

        [HttpPost("preferences")]
        public async Task<IActionResult> UpdatePreferences([FromBody] UserPreferencesDto request)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { error = "Unauthorized: Invalid or expired token." });
            }

            user.DarkMode = request.DarkMode;
            user.NotificationsEnabled = request.NotificationsEnabled;
            user.HighContrast = request.HighContrast;

            await _context.SaveChangesAsync();

            return Ok(new { success = true, preferences = request });
        }

        [HttpPost("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { error = "Unauthorized: Invalid or expired token." });
            }

            var cleanName = request.Name.Trim();
            var cleanUsername = string.IsNullOrWhiteSpace(request.Username)
                ? null
                : AuthService.NormalizeUsername(request.Username);
            var cleanAvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl)
                ? null
                : request.AvatarUrl.Trim();

            if (cleanUsername is { Length: > 0 } &&
                !System.Text.RegularExpressions.Regex.IsMatch(cleanUsername, "^[A-Za-z0-9_.]{3,30}$"))
            {
                return BadRequest(new { error = "Username can use 3-30 letters, numbers, underscores, or dots." });
            }

            if (cleanUsername != null &&
                await _context.Users.AnyAsync(u => u.Id != user.Id && u.Username == cleanUsername))
            {
                return Conflict(new { error = "That username is already taken." });
            }

            user.Name = cleanName;
            user.Username = cleanUsername;
            user.AvatarUrl = cleanAvatarUrl;

            await _context.SaveChangesAsync();

            return Ok(new { success = true, user = ToUserResponse(user) });
        }

        private static UserResponse ToUserResponse(User user)
        {
            var preferencesDto = new UserPreferencesDto(user.DarkMode, user.NotificationsEnabled, user.HighContrast);
            return new UserResponse(user.Id, user.Email, user.Name, user.Username, user.AvatarUrl, preferencesDto, user.IsAdmin);
        }

        private void SetAuthCookie(AuthResponse response)
        {
            Response.Cookies.Append(
                AuthService.AuthCookieName,
                response.Token,
                CreateAuthCookieOptions(response.ExpiresAt));
        }

        private CookieOptions CreateAuthCookieOptions(DateTimeOffset expires)
        {
            return new CookieOptions
            {
                HttpOnly = true,
                Secure = _environment.IsProduction(),
                SameSite = SameSiteMode.Lax,
                Expires = expires,
                Path = "/",
                IsEssential = true
            };
        }
    }
}
