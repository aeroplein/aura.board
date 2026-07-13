using System;
using DigitalVisionBoard.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalVisionBoard.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260713120000_AddPasswordResetFields")]
    public partial class AddPasswordResetFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordResetExpires",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordResetToken",
                table: "Users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_PasswordResetToken",
                table: "Users",
                column: "PasswordResetToken",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Users_PasswordResetToken", table: "Users");
            migrationBuilder.DropColumn(name: "PasswordResetExpires", table: "Users");
            migrationBuilder.DropColumn(name: "PasswordResetToken", table: "Users");
        }
    }
}
