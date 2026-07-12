using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using DigitalVisionBoard.Data;
using DigitalVisionBoard.Models;
using DigitalVisionBoard.Services;

namespace DigitalVisionBoard.Controllers
{
    [ApiController]
    public abstract class BaseApiController : Controller
    {
        protected readonly AppDbContext _context;
        private readonly AuthService _authService;

        protected BaseApiController(AppDbContext context, AuthService authService)
        {
            _context = context;
            _authService = authService;
        }

        protected async Task<User?> GetCurrentUserAsync()
        {
            var token = Request.Cookies[AuthService.AuthCookieName];
            return await _authService.GetUserFromJwtAsync(token);
        }

        protected async Task<IActionResult?> RequireCurrentAdminAsync()
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { error = "Sign in to access admin tools." });
            }

            return user.IsAdmin
                ? null
                : StatusCode(StatusCodes.Status403Forbidden, new { error = "Admin access is required." });
        }
    }
}
