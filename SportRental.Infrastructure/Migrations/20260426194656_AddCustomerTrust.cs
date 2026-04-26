using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportRental.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerTrust : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "TrustAverageScore",
                table: "Customers",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "TrustCompletedRentalsCount",
                table: "Customers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TrustIncidentCount",
                table: "Customers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TrustLevel",
                table: "Customers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrustLevelCalculatedAtUtc",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrustLevelManualOverride",
                table: "Customers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrustLevelManualReason",
                table: "Customers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RentalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TimelinessScore = table.Column<int>(type: "integer", nullable: false),
                    ConditionScore = table.Column<int>(type: "integer", nullable: false),
                    CommunicationScore = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerReviews_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReviews_Rentals_RentalId",
                        column: x => x.RentalId,
                        principalTable: "Rentals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerReviews_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReviews_CustomerId_CreatedAtUtc",
                table: "CustomerReviews",
                columns: new[] { "CustomerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReviews_RentalId",
                table: "CustomerReviews",
                column: "RentalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReviews_TenantId_CreatedAtUtc",
                table: "CustomerReviews",
                columns: new[] { "TenantId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerReviews");

            migrationBuilder.DropColumn(
                name: "TrustAverageScore",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "TrustCompletedRentalsCount",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "TrustIncidentCount",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "TrustLevel",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "TrustLevelCalculatedAtUtc",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "TrustLevelManualOverride",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "TrustLevelManualReason",
                table: "Customers");
        }
    }
}
