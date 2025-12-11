using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportRental.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHourlyRentalSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HoursRented",
                table: "Rentals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RentalType",
                table: "Rentals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "PricePerHour",
                table: "RentalItems",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HourlyPrice",
                table: "Products",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HoursRented",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "RentalType",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "PricePerHour",
                table: "RentalItems");

            migrationBuilder.DropColumn(
                name: "HourlyPrice",
                table: "Products");
        }
    }
}
