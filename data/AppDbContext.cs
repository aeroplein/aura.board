using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using DigitalVisionBoard.Models;

namespace DigitalVisionBoard.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Board> Boards { get; set; }
        public DbSet<BoardCollaborator> BoardCollaborators { get; set; }
        public DbSet<BoardItem> BoardItems { get; set; }
        public DbSet<ImageFile> ImageFiles { get; set; }
        public DbSet<ActivityLog> ActivityLogs { get; set; }
        public DbSet<AdminAuditLog> AdminAuditLogs { get; set; }

        public override int SaveChanges()
        {
            NormalizeCollaboratorEmails();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            NormalizeCollaboratorEmails();
            return base.SaveChangesAsync(cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(entity =>
            {
                entity.Property(u => u.Email)
                    .HasMaxLength(254);

                entity.Property(u => u.Name)
                    .HasMaxLength(80);

                entity.Property(u => u.Username)
                    .HasMaxLength(30);

                entity.Property(u => u.AvatarUrl)
                    .HasMaxLength(2048);

                entity.Property(u => u.PasswordHash)
                    .HasMaxLength(256);

                entity.Property(u => u.Salt)
                    .HasMaxLength(128);

                entity.Property(u => u.EmailVerificationToken)
                    .HasMaxLength(64);

                entity.Property(u => u.PasswordResetTokenHash)
                    .HasMaxLength(64);

                entity.HasIndex(u => u.CreatedAt);
            });

            modelBuilder.Entity<AdminAuditLog>(entity =>
            {
                entity.Property(log => log.AdminEmail)
                    .HasMaxLength(254);

                entity.Property(log => log.TargetEmail)
                    .HasMaxLength(254);

                entity.Property(log => log.Action)
                    .HasMaxLength(80);

                entity.Property(log => log.Details)
                    .HasMaxLength(500);

                entity.HasIndex(log => log.Timestamp)
                    .IsDescending(true);

                entity.HasIndex(log => new { log.TargetUserId, log.Timestamp })
                    .IsDescending(false, true);
            });

            modelBuilder.Entity<Board>(entity =>
            {
                entity.Property(b => b.Title)
                    .HasMaxLength(120);

                entity.Property(b => b.Description)
                    .HasMaxLength(500);

                entity.Property(b => b.Category)
                    .HasMaxLength(80);

                entity.HasIndex(b => new { b.OwnerId, b.UpdatedAt })
                    .IsDescending(false, true);
            });

            modelBuilder.Entity<BoardCollaborator>(entity =>
            {
                entity.Property(bc => bc.CollaboratorEmail)
                    .HasMaxLength(254);

                entity.HasIndex(bc => new { bc.CollaboratorEmail, bc.BoardId });
            });

            modelBuilder.Entity<BoardItem>(entity =>
            {
                entity.Property(bi => bi.Id)
                    .HasMaxLength(120);

                entity.Property(bi => bi.Type)
                    .HasMaxLength(20);

                entity.Property(bi => bi.Title)
                    .HasMaxLength(120);

                entity.Property(bi => bi.Content)
                    .HasMaxLength(4096);

                entity.Property(bi => bi.Caption)
                    .HasMaxLength(500);

                entity.Property(bi => bi.ImageDisplayMode)
                    .HasMaxLength(20)
                    .HasDefaultValue("card");

                entity.Property(bi => bi.Color)
                    .HasMaxLength(200);

                entity.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_BoardItems_Type", "\"Type\" IN ('quote', 'note', 'image', 'text', 'music')");
                    t.HasCheckConstraint("CK_BoardItems_ImageDisplayMode", "\"ImageDisplayMode\" IN ('card', 'plain', 'captioned')");
                    t.HasCheckConstraint("CK_BoardItems_Position", "\"X\" >= 0 AND \"Y\" >= 0");
                    t.HasCheckConstraint("CK_BoardItems_Size", "\"Width\" > 0 AND \"Height\" > 0");
                });
            });

            modelBuilder.Entity<ActivityLog>(entity =>
            {
                entity.Property(al => al.UserEmail)
                    .HasMaxLength(254);

                entity.Property(al => al.ActionDescription)
                    .HasMaxLength(500);

                entity.HasIndex(al => new { al.BoardId, al.Timestamp })
                    .IsDescending(false, true);
            });

            modelBuilder.Entity<ImageFile>(entity =>
            {
                entity.Property(img => img.MimeType)
                    .HasMaxLength(80);

                entity.HasIndex(img => img.UploaderUserId);
            });

            // Configure User Email unique constraint & index
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.EmailVerificationToken)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.PasswordResetTokenHash)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique()
                .HasFilter("\"Username\" IS NOT NULL");

            // Configure BoardCollaborator composite primary key
            modelBuilder.Entity<BoardCollaborator>()
                .HasKey(bc => new { bc.BoardId, bc.CollaboratorEmail });

            // Configure Board -> BoardCollaborator cascade delete
            modelBuilder.Entity<BoardCollaborator>()
                .HasOne(bc => bc.Board)
                .WithMany(b => b.Collaborators)
                .HasForeignKey(bc => bc.BoardId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure Board -> BoardItem cascade delete
            modelBuilder.Entity<BoardItem>()
                .HasOne(bi => bi.Board)
                .WithMany(b => b.Items)
                .HasForeignKey(bi => bi.BoardId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure User -> Board cascade delete (board ownership)
            modelBuilder.Entity<Board>()
                .HasOne(b => b.Owner)
                .WithMany(u => u.Boards)
                .HasForeignKey(b => b.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure Board -> ActivityLog cascade delete
            modelBuilder.Entity<ActivityLog>()
                .HasOne<Board>()
                .WithMany()
                .HasForeignKey(al => al.BoardId)
                .OnDelete(DeleteBehavior.Cascade);
        }

        private void NormalizeCollaboratorEmails()
        {
            foreach (var entry in ChangeTracker.Entries<BoardCollaborator>())
            {
                if (entry.State is EntityState.Added or EntityState.Modified)
                {
                    entry.Entity.CollaboratorEmail = entry.Entity.CollaboratorEmail.Trim().ToLowerInvariant();
                }
            }
        }
    }
}
