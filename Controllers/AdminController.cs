using DigitalVisionBoard.Data;
using DigitalVisionBoard.Models;
using DigitalVisionBoard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DigitalVisionBoard.Controllers
{
    [Route("api/admin")]
    public class AdminController : BaseApiController
    {
        private readonly AdminService _adminService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            AppDbContext context,
            AuthService authService,
            AdminService adminService,
            ILogger<AdminController> logger)
            : base(context, authService)
        {
            _adminService = adminService;
            _logger = logger;
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

        [HttpGet("dashboard")]
        [EnableRateLimiting("admin")]
        public async Task<IActionResult> GetDashboard(CancellationToken cancellationToken)
        {
            var accessResult = await RequireCurrentAdminAsync();
            return accessResult ?? Ok(await _adminService.GetDashboardAsync(cancellationToken));
        }

        [HttpGet("users")]
        [EnableRateLimiting("admin")]
        public async Task<IActionResult> GetUsers(
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var accessResult = await RequireCurrentAdminAsync();
            return accessResult ?? Ok(await _adminService.GetUsersAsync(search, page, pageSize, cancellationToken));
        }

        [HttpGet("audit")]
        [EnableRateLimiting("admin")]
        public async Task<IActionResult> GetAuditLogs(
            [FromQuery] int limit = 30,
            CancellationToken cancellationToken = default)
        {
            var accessResult = await RequireCurrentAdminAsync();
            return accessResult ?? Ok(await _adminService.GetAuditLogsAsync(limit, cancellationToken));
        }

        [HttpPost("users/invite")]
        [EnableRateLimiting("admin")]
        public async Task<IActionResult> InviteUser(
            [FromBody] AdminInviteUserRequest request,
            CancellationToken cancellationToken)
        {
            return await ExecuteMutationAsync(
                admin => _adminService.InviteUserAsync(admin, request, cancellationToken),
                response => Created($"/api/admin/users/{response.Id}", response));
        }

        [HttpPost("users/{id:guid}/suspend")]
        [EnableRateLimiting("admin")]
        public async Task<IActionResult> SuspendUser(Guid id, CancellationToken cancellationToken)
        {
            return await ExecuteMutationAsync(
                admin => _adminService.SetSuspendedAsync(admin, id, true, cancellationToken),
                Ok);
        }

        [HttpPost("users/{id:guid}/reactivate")]
        [EnableRateLimiting("admin")]
        public async Task<IActionResult> ReactivateUser(Guid id, CancellationToken cancellationToken)
        {
            return await ExecuteMutationAsync(
                admin => _adminService.SetSuspendedAsync(admin, id, false, cancellationToken),
                Ok);
        }

        [HttpPost("users/{id:guid}/role")]
        [EnableRateLimiting("admin")]
        public async Task<IActionResult> SetRole(
            Guid id,
            [FromBody] AdminRoleRequest request,
            CancellationToken cancellationToken)
        {
            return await ExecuteMutationAsync(
                admin => _adminService.SetRoleAsync(admin, id, request.IsAdmin, cancellationToken),
                Ok);
        }

        [HttpDelete("users/{id:guid}")]
        [EnableRateLimiting("admin")]
        public async Task<IActionResult> DeleteUser(
            Guid id,
            [FromBody] AdminDeleteUserRequest request,
            CancellationToken cancellationToken)
        {
            var accessResult = await RequireCurrentAdminAsync();
            if (accessResult != null)
            {
                return accessResult;
            }

            var headerResult = RequireAdminMutationHeader();
            if (headerResult != null)
            {
                return headerResult;
            }

            var admin = await GetCurrentUserAsync();
            try
            {
                await _adminService.DeleteUserAsync(admin!, id, request.ConfirmationEmail, cancellationToken);
                return NoContent();
            }
            catch (Exception ex)
            {
                return MapAdminException(ex);
            }
        }

        private async Task<IActionResult> ExecuteMutationAsync<T>(
            Func<User, Task<T>> operation,
            Func<T, IActionResult> onSuccess)
        {
            var accessResult = await RequireCurrentAdminAsync();
            if (accessResult != null)
            {
                return accessResult;
            }

            var headerResult = RequireAdminMutationHeader();
            if (headerResult != null)
            {
                return headerResult;
            }

            var admin = await GetCurrentUserAsync();
            try
            {
                return onSuccess(await operation(admin!));
            }
            catch (Exception ex)
            {
                return MapAdminException(ex);
            }
        }

        private IActionResult MapAdminException(Exception exception)
        {
            return exception switch
            {
                KeyNotFoundException => NotFound(new { error = exception.Message }),
                ArgumentException => BadRequest(new { error = exception.Message }),
                AdvancedEmailValidationException => BadRequest(new { error = exception.Message }),
                InvalidOperationException => Conflict(new { error = exception.Message }),
                _ => LogUnexpectedAdminError(exception)
            };
        }

        private IActionResult LogUnexpectedAdminError(Exception exception)
        {
            _logger.LogError(exception, "Admin operation failed.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Admin operation is temporarily unavailable." });
        }
    }
}
