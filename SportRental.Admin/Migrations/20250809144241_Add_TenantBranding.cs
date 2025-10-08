using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportRental.Admin.Migrations
{
    /// <inheritdoc />
    public partial class Add_TenantBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "Tenants",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryColorHex",
                table: "Tenants",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryColorHex",
                table: "Tenants",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "PrimaryColorHex",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SecondaryColorHex",
                table: "Tenants");
        }
    }
}
