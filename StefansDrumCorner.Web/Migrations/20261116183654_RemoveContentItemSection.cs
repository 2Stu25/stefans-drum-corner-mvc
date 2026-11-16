using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StefansDrumCorner.Web.Migrations
{
    /// <inheritdoc />
    public partial class RemoveContentItemSection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Section",
                table: "ContentItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Section",
                table: "ContentItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
