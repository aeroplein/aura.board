using DigitalVisionBoard.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalVisionBoard.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260709090000_AddMusicBoardItemType")]
    public partial class AddMusicBoardItemType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BoardItems_Type",
                table: "BoardItems");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BoardItems_Type",
                table: "BoardItems",
                sql: "\"Type\" IN ('quote', 'note', 'image', 'text', 'music')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BoardItems_Type",
                table: "BoardItems");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BoardItems_Type",
                table: "BoardItems",
                sql: "\"Type\" IN ('quote', 'note', 'image', 'text')");
        }
    }
}
