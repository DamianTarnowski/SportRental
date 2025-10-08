using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportRental.Admin.Migrations
{
    /// <inheritdoc />
    public partial class ReservationHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Rentals",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReservationHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    StartDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationHolds", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Rentals_TenantId_IdempotencyKey",
                table: "Rentals",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReservationHolds_ExpiresAtUtc",
                table: "ReservationHolds",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationHolds_TenantId_ProductId_StartDateUtc_EndDateUtc",
                table: "ReservationHolds",
                columns: new[] { "TenantId", "ProductId", "StartDateUtc", "EndDateUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservationHolds");

            migrationBuilder.DropIndex(
                name: "IX_Rentals_TenantId_IdempotencyKey",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "Rentals");
        }
    }
}
