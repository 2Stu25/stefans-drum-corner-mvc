using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StefansDrumCorner.Web.Migrations
{
    /// <inheritdoc />
    public partial class RemoveContentItemContentType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "ContentItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContentType",
                table: "ContentItems",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
