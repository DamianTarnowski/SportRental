using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportRental.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewRequestTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastReviewRequestSentAtUtc",
                table: "Rentals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewRequestCount",
                table: "Rentals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ReviewEmailsOptOut",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastReviewRequestSentAtUtc",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "ReviewRequestCount",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "ReviewEmailsOptOut",
                table: "Customers");
        }
    }
}
