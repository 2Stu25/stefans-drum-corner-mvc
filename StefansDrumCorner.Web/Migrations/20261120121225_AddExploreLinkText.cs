using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StefansDrumCorner.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddExploreLinkText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExploreLinkText",
                table: "ContentCategories",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExploreLinkText",
                table: "ContentCategories");
        }
    }
}
