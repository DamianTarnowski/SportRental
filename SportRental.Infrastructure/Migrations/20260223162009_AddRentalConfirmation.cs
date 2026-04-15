using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportRental.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRentalConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RegulationsText",
                table: "CompanyInfos",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RentalConfirmations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RentalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IsConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedFromIp = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    ConfirmedUserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RegulationsHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsSmsSent = table.Column<bool>(type: "boolean", nullable: false),
                    IsEmailSent = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalConfirmations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RentalConfirmations_Rentals_RentalId",
                        column: x => x.RentalId,
                        principalTable: "Rentals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RentalConfirmations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RentalConfirmations_RentalId",
                table: "RentalConfirmations",
                column: "RentalId");

            migrationBuilder.CreateIndex(
                name: "IX_RentalConfirmations_TenantId_RentalId",
                table: "RentalConfirmations",
                columns: new[] { "TenantId", "RentalId" });

            migrationBuilder.CreateIndex(
                name: "IX_RentalConfirmations_Token",
                table: "RentalConfirmations",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RentalConfirmations");

            migrationBuilder.DropColumn(
                name: "RegulationsText",
                table: "CompanyInfos");
        }
    }
}
