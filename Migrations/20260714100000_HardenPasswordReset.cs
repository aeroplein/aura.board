using System;
using DigitalVisionBoard.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalVisionBoard.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260714100000_HardenPasswordReset")]
    public partial class HardenPasswordReset : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Users_PasswordResetToken", table: "Users");
            migrationBuilder.RenameColumn(name: "PasswordResetToken", table: "Users", newName: "PasswordResetTokenHash");
            // Previously-issued plaintext reset values cannot safely be treated as hashes.
            migrationBuilder.Sql("UPDATE \"Users\" SET \"PasswordResetTokenHash\" = NULL, \"PasswordResetExpires\" = NULL;");

            migrationBuilder.AddColumn<int>(name: "SessionVersion", table: "Users", type: "integer", nullable: false, defaultValue: 0);

            migrationBuilder.CreateIndex(name: "IX_Users_PasswordResetTokenHash", table: "Users", column: "PasswordResetTokenHash", unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Users_PasswordResetTokenHash", table: "Users");
            migrationBuilder.DropColumn(name: "SessionVersion", table: "Users");
            migrationBuilder.RenameColumn(name: "PasswordResetTokenHash", table: "Users", newName: "PasswordResetToken");
            migrationBuilder.CreateIndex(name: "IX_Users_PasswordResetToken", table: "Users", column: "PasswordResetToken", unique: true);
        }
    }
}
