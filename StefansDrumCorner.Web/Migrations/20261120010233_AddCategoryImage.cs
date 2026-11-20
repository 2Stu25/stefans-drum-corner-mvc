using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StefansDrumCorner.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ImageId",
                table: "ContentCategories",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentCategories_ImageId",
                table: "ContentCategories",
                column: "ImageId");

            migrationBuilder.AddForeignKey(
                name: "FK_ContentCategories_MediaAssets_ImageId",
                table: "ContentCategories",
                column: "ImageId",
                principalTable: "MediaAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ContentCategories_MediaAssets_ImageId",
                table: "ContentCategories");

            migrationBuilder.DropIndex(
                name: "IX_ContentCategories_ImageId",
                table: "ContentCategories");

            migrationBuilder.DropColumn(
                name: "ImageId",
                table: "ContentCategories");
        }
    }
}
