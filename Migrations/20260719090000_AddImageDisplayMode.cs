using DigitalVisionBoard.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalVisionBoard.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260719090000_AddImageDisplayMode")]
    public partial class AddImageDisplayMode : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageDisplayMode",
                table: "BoardItems",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "card");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BoardItems_ImageDisplayMode",
                table: "BoardItems",
                sql: "\"ImageDisplayMode\" IN ('card', 'plain', 'captioned')");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BoardItems_ImageDisplayMode",
                table: "BoardItems");

            migrationBuilder.DropColumn(
                name: "ImageDisplayMode",
                table: "BoardItems");
        }
    }
}
