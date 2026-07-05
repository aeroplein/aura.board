using DigitalVisionBoard.Data;
using DigitalVisionBoard.Models;
using DigitalVisionBoard.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                var response = await _authService.RegisterAsync(request);
                SetAuthCookie(response);
                return Created("/api/auth/login", response);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogInformation(ex, "Registration rejected for {Email}", request.Email);
                if (ex is AdvancedEmailValidationException)
                {
                    return BadRequest(new { error = ex.Message });
                }

                return Conflict(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registration failed for {Email}", request.Email);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Accounts are temporarily unavailable. Check the database configuration and try again." });
            }
        }

        [HttpPost("login")]
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login failed for {Email}", request.Email);
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

            var preferencesDto = new UserPreferencesDto(user.DarkMode, user.NotificationsEnabled, user.HighContrast);
            return Ok(new { user = new UserResponse(user.Id, user.Email, user.Name, preferencesDto) });
        }

        [HttpGet("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string? email, [FromQuery] string? token)
        {
            try
            {
                var result = await _authService.VerifyEmailAsync(email, token);
                return result switch
                {
                    EmailVerificationResult.Success => Ok(new { success = true, message = "Email verified successfully." }),
                    EmailVerificationResult.AlreadyVerified => Conflict(new { error = "Email address is already verified." }),
                    EmailVerificationResult.MissingParameters => BadRequest(new { error = "Email and token are required." }),
                    EmailVerificationResult.Expired => BadRequest(new { error = "Email verification token has expired." }),
                    EmailVerificationResult.InvalidToken => BadRequest(new { error = "Invalid email verification token." }),
                    EmailVerificationResult.UserNotFound => NotFound(new { error = "User not found." }),
                    _ => BadRequest(new { error = "Email verification failed." })
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email verification failed for {Email}", email);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Email verification is temporarily unavailable." });
            }
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
