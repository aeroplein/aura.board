using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalVisionBoard.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardItemZIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ZIndex",
                table: "BoardItems",
                type: "integer",
                nullable: false,
                defaultValue: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ZIndex",
                table: "BoardItems");
        }
    }
}
