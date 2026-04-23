using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportRental.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFinalRentalReminder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FinalReminderSentAtUtc",
                table: "Rentals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFinalReminderSent",
                table: "Rentals",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinalReminderSentAtUtc",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "IsFinalReminderSent",
                table: "Rentals");
        }
    }
}
