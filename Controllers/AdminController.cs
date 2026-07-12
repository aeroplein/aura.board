using DigitalVisionBoard.Data;
using DigitalVisionBoard.Services;
using Microsoft.AspNetCore.Mvc;

namespace DigitalVisionBoard.Controllers
{
    [Route("api/admin")]
    public class AdminController : BaseApiController
    {
        public AdminController(AppDbContext context, AuthService authService)
            : base(context, authService)
        {
        }

        // Future admin features must pass this server-side authorization boundary.
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var accessResult = await RequireCurrentAdminAsync();
            if (accessResult != null)
            {
                return accessResult;
            }

            return Ok(new { isAdmin = true });
        }
    }
}
