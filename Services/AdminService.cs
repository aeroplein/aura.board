using DigitalVisionBoard.Data;
using DigitalVisionBoard.Models;
using Microsoft.EntityFrameworkCore;

namespace DigitalVisionBoard.Services
{
    public class AdminService
    {
        private readonly AppDbContext _context;
        private readonly AuthService _authService;

        public AdminService(AppDbContext context, AuthService authService)
        {
            _context = context;
            _authService = authService;
        }

        public async Task<AdminDashboardResponse> GetDashboardAsync(CancellationToken cancellationToken = default)
        {
            return new AdminDashboardResponse(
                await _context.Users.CountAsync(cancellationToken),
                await _context.Users.CountAsync(user => user.IsEmailVerified, cancellationToken),
                await _context.Users.CountAsync(user => user.InvitationPending, cancellationToken),
                await _context.Users.CountAsync(user => user.IsSuspended, cancellationToken),
                await _context.Users.CountAsync(user => user.IsAdmin, cancellationToken),
                await _context.Boards.CountAsync(cancellationToken));
        }

        public async Task<AdminUsersPageResponse> GetUsersAsync(
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 5, 50);
            var query = _context.Users.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var normalized = search.Trim().ToLowerInvariant();
                query = query.Where(user =>
                    user.Email.ToLower().Contains(normalized) ||
                    user.Name.ToLower().Contains(normalized) ||
                    (user.Username != null && user.Username.ToLower().Contains(normalized)));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var users = await query
                .OrderByDescending(user => user.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(user => new AdminUserResponse(
                    user.Id,
                    user.Email,
                    user.Name,
                    user.Username,
                    user.IsEmailVerified,
                    user.InvitationPending,
                    user.IsSuspended,
                    user.IsAdmin,
                    user.Boards.Count,
                    user.CreatedAt))
                .ToListAsync(cancellationToken);

            var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
            return new AdminUsersPageResponse(users, page, pageSize, totalCount, totalPages);
        }

        public async Task<IReadOnlyList<AdminAuditLogResponse>> GetAuditLogsAsync(
            int limit,
            CancellationToken cancellationToken = default)
        {
            return await _context.AdminAuditLogs
                .AsNoTracking()
                .OrderByDescending(log => log.Timestamp)
                .Take(Math.Clamp(limit, 1, 100))
                .Select(log => new AdminAuditLogResponse(
                    log.Id,
                    log.AdminEmail,
                    log.TargetUserId,
                    log.TargetEmail,
                    log.Action,
                    log.Details,
                    log.Success,
                    log.Timestamp))
                .ToListAsync(cancellationToken);
        }

        public async Task<AdminUserResponse> InviteUserAsync(
            User admin,
            AdminInviteUserRequest request,
            CancellationToken cancellationToken = default)
        {
            var invitedUser = await _authService.InviteUserAsync(request);
            AddAudit(admin, invitedUser, "user_invited", "Sent a secure account activation invitation.");
            await _context.SaveChangesAsync(cancellationToken);
            return await GetUserResponseAsync(invitedUser.Id, cancellationToken);
        }

        public async Task<AdminUserResponse> SetSuspendedAsync(
            User admin,
            Guid targetUserId,
            bool suspended,
            CancellationToken cancellationToken = default)
        {
            var target = await FindTargetAsync(targetUserId, cancellationToken);
            if (target.Id == admin.Id)
            {
                throw new InvalidOperationException("Administrators cannot suspend their own account.");
            }

            if (target.IsAdmin)
            {
                throw new InvalidOperationException("Remove the administrator role before suspending this account.");
            }

            if (target.IsSuspended != suspended)
            {
                target.IsSuspended = suspended;
                target.SessionVersion++;
                AddAudit(
                    admin,
                    target,
                    suspended ? "user_suspended" : "user_reactivated",
                    suspended ? "Revoked active sessions and blocked sign-in." : "Restored account access.");
                await _context.SaveChangesAsync(cancellationToken);
            }

            return await GetUserResponseAsync(target.Id, cancellationToken);
        }

        public async Task<AdminUserResponse> SetRoleAsync(
            User admin,
            Guid targetUserId,
            bool isAdmin,
            CancellationToken cancellationToken = default)
        {
            var target = await FindTargetAsync(targetUserId, cancellationToken);
            if (target.Id == admin.Id)
            {
                throw new InvalidOperationException("Administrators cannot change their own role.");
            }

            if (isAdmin && (!target.IsEmailVerified || target.IsSuspended || target.InvitationPending))
            {
                throw new InvalidOperationException("Only verified, active accounts can become administrators.");
            }

            if (!isAdmin && target.IsAdmin && await _context.Users.CountAsync(user => user.IsAdmin, cancellationToken) <= 1)
            {
                throw new InvalidOperationException("The final administrator cannot be demoted.");
            }

            if (target.IsAdmin != isAdmin)
            {
                target.IsAdmin = isAdmin;
                target.SessionVersion++;
                AddAudit(
                    admin,
                    target,
                    isAdmin ? "admin_role_granted" : "admin_role_removed",
                    "Changed the account administrator role and revoked its active sessions.");
                await _context.SaveChangesAsync(cancellationToken);
            }

            return await GetUserResponseAsync(target.Id, cancellationToken);
        }

        public async Task DeleteUserAsync(
            User admin,
            Guid targetUserId,
            string confirmationEmail,
            CancellationToken cancellationToken = default)
        {
            var target = await FindTargetAsync(targetUserId, cancellationToken);
            if (target.Id == admin.Id)
            {
                throw new InvalidOperationException("Administrators cannot delete their own account.");
            }

            if (!string.Equals(
                    AuthService.NormalizeEmail(confirmationEmail),
                    target.Email,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException("Enter the target account email exactly to confirm deletion.");
            }

            if (target.IsAdmin && await _context.Users.CountAsync(user => user.IsAdmin, cancellationToken) <= 1)
            {
                throw new InvalidOperationException("The final administrator cannot be deleted.");
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            await _context.BoardCollaborators
                .Where(collaborator => collaborator.CollaboratorEmail == target.Email)
                .ExecuteDeleteAsync(cancellationToken);

            var uploadedImages = await _context.ImageFiles
                .Where(image => image.UploaderUserId == target.Id)
                .ToListAsync(cancellationToken);
            var uploadedImageUrls = uploadedImages
                .Select(image => $"/api/images/{image.Id}")
                .ToList();
            var externallyReferencedUrls = new HashSet<string>(StringComparer.Ordinal);
            if (uploadedImageUrls.Count > 0)
            {
                var referencedContent = await _context.BoardItems
                    .Where(item => item.Board!.OwnerId != target.Id &&
                        item.Content != null && uploadedImageUrls.Contains(item.Content))
                    .Select(item => item.Content!)
                    .ToListAsync(cancellationToken);
                var referencedCaptions = await _context.BoardItems
                    .Where(item => item.Board!.OwnerId != target.Id &&
                        item.Caption != null && uploadedImageUrls.Contains(item.Caption))
                    .Select(item => item.Caption!)
                    .ToListAsync(cancellationToken);
                externallyReferencedUrls.UnionWith(referencedContent);
                externallyReferencedUrls.UnionWith(referencedCaptions);
            }

            foreach (var image in uploadedImages)
            {
                if (externallyReferencedUrls.Contains($"/api/images/{image.Id}"))
                {
                    image.UploaderUserId = null;
                }
                else
                {
                    _context.ImageFiles.Remove(image);
                }
            }

            AddAudit(admin, target, "user_deleted", "Deleted the account, owned boards, and unshared uploads; preserved images referenced by other boards.");
            _context.Users.Remove(target);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        private async Task<User> FindTargetAsync(Guid targetUserId, CancellationToken cancellationToken)
        {
            return await _context.Users.SingleOrDefaultAsync(user => user.Id == targetUserId, cancellationToken)
                ?? throw new KeyNotFoundException("User was not found.");
        }

        private async Task<AdminUserResponse> GetUserResponseAsync(Guid userId, CancellationToken cancellationToken)
        {
            return await _context.Users
                .AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => new AdminUserResponse(
                    user.Id,
                    user.Email,
                    user.Name,
                    user.Username,
                    user.IsEmailVerified,
                    user.InvitationPending,
                    user.IsSuspended,
                    user.IsAdmin,
                    user.Boards.Count,
                    user.CreatedAt))
                .SingleAsync(cancellationToken);
        }

        private void AddAudit(User admin, User target, string action, string details)
        {
            _context.AdminAuditLogs.Add(new AdminAuditLog
            {
                AdminUserId = admin.Id,
                AdminEmail = admin.Email,
                TargetUserId = target.Id,
                TargetEmail = target.Email,
                Action = action,
                Details = details
            });
        }
    }
}
