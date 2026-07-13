using DigitalVisionBoard.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalVisionBoard.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260713130000_BackfillExistingEmailVerification")]
    public partial class BackfillExistingEmailVerification : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Email verification was introduced after accounts already existed.
            // Preserve access for those accounts now that confirmation is not a sign-in gate.
            migrationBuilder.Sql("""
                UPDATE "Users"
                SET "IsEmailVerified" = TRUE,
                    "EmailVerificationToken" = NULL,
                    "EmailVerificationExpires" = NULL
                WHERE "IsEmailVerified" = FALSE;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This is a one-way data backfill; do not invalidate existing accounts on rollback.
        }
    }
}
