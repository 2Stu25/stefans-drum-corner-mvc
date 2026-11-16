using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StefansDrumCorner.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Section",
                table: "ContentCategories",
                newName: "Subtitle");

            migrationBuilder.AddColumn<int>(
                name: "ParentCategoryId",
                table: "ContentCategories",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentCategories_ParentCategoryId",
                table: "ContentCategories",
                column: "ParentCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_ContentCategories_ContentCategories_ParentCategoryId",
                table: "ContentCategories",
                column: "ParentCategoryId",
                principalTable: "ContentCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ContentCategories_ContentCategories_ParentCategoryId",
                table: "ContentCategories");

            migrationBuilder.DropIndex(
                name: "IX_ContentCategories_ParentCategoryId",
                table: "ContentCategories");

            migrationBuilder.DropColumn(
                name: "ParentCategoryId",
                table: "ContentCategories");

            migrationBuilder.RenameColumn(
                name: "Subtitle",
                table: "ContentCategories",
                newName: "Section");
        }
    }
}
