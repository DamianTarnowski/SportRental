using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportRental.Admin.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexes_20250811 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageAlt",
                table: "Products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageBasePath",
                table: "Products",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rentals_TenantId_CustomerId_StartDateUtc",
                table: "Rentals",
                columns: new[] { "TenantId", "CustomerId", "StartDateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Rentals_TenantId_StartDateUtc_EndDateUtc",
                table: "Rentals",
                columns: new[] { "TenantId", "StartDateUtc", "EndDateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_Category",
                table: "Products",
                columns: new[] { "TenantId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_FullName",
                table: "Customers",
                columns: new[] { "TenantId", "FullName" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_PhoneNumber",
                table: "Customers",
                columns: new[] { "TenantId", "PhoneNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Rentals_TenantId_CustomerId_StartDateUtc",
                table: "Rentals");

            migrationBuilder.DropIndex(
                name: "IX_Rentals_TenantId_StartDateUtc_EndDateUtc",
                table: "Rentals");

            migrationBuilder.DropIndex(
                name: "IX_Products_TenantId_Category",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Customers_TenantId_FullName",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_TenantId_PhoneNumber",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "ImageAlt",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ImageBasePath",
                table: "Products");
        }
    }
}
