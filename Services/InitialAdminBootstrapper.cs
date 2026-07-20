using DigitalVisionBoard.Data;
using DigitalVisionBoard.Models;
using Microsoft.EntityFrameworkCore;

namespace DigitalVisionBoard.Services
{
    public static class InitialAdminBootstrapper
    {
        public static async Task TryPromoteAsync(
            AppDbContext context,
            IConfiguration configuration,
            ILogger logger,
            CancellationToken cancellationToken = default)
        {
            var configuredEmail = configuration["INITIAL_ADMIN_EMAIL"];
            if (string.IsNullOrWhiteSpace(configuredEmail) ||
                await context.Users.AnyAsync(user => user.IsAdmin, cancellationToken))
            {
                return;
            }

            var email = AuthService.NormalizeEmail(configuredEmail);
            if (!StrictEmailValidator.IsValid(email))
            {
                logger.LogWarning("INITIAL_ADMIN_EMAIL is not a valid email address.");
                return;
            }

            var user = await context.Users.SingleOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);
            if (user == null)
            {
                logger.LogWarning("Initial admin account was not found for the configured email.");
                return;
            }

            if (!user.IsEmailVerified || user.IsSuspended)
            {
                logger.LogWarning("Initial admin account must be verified and active before promotion.");
                return;
            }

            user.IsAdmin = true;
            user.SessionVersion++;
            context.AdminAuditLogs.Add(new AdminAuditLog
            {
                AdminUserId = null,
                AdminEmail = "system-bootstrap",
                TargetUserId = user.Id,
                TargetEmail = user.Email,
                Action = "initial_admin_promoted",
                Details = "Promoted the first verified administrator from INITIAL_ADMIN_EMAIL."
            });

            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Initial administrator was promoted successfully.");
        }
    }
}
