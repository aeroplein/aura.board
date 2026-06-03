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
                return Conflict(new { error = ex.Message });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
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
