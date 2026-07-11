using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportRental.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductImageVariantMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasOriginalImage",
                table: "Products",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int[]>(
                name: "ImageVariantWidths",
                table: "Products",
                type: "integer[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasOriginalImage",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ImageVariantWidths",
                table: "Products");
        }
    }
}
