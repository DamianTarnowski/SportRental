using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportRental.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketplaceOrdersAndRentalTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "DepositAmount",
                table: "Rentals",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AddColumn<Guid>(
                name: "MarketplaceOrderId",
                table: "Rentals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrderSequence",
                table: "Rentals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegulationsHash",
                table: "Rentals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegulationsSource",
                table: "Rentals",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegulationsTextSnapshot",
                table: "Rentals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegulationsVersion",
                table: "Rentals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MarketplaceOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckoutSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StripeSessionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PaymentIntentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DepositAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundedDepositAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PaymentStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedTermsVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AcknowledgedPrivacyVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LegalAcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplaceOrders_CheckoutSessions_CheckoutSessionId",
                        column: x => x.CheckoutSessionId,
                        principalTable: "CheckoutSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceOrders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Rentals_MarketplaceOrderId",
                table: "Rentals",
                column: "MarketplaceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Rentals_MarketplaceOrderId_OrderSequence",
                table: "Rentals",
                columns: new[] { "MarketplaceOrderId", "OrderSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rentals_MarketplaceOrderId_TenantId",
                table: "Rentals",
                columns: new[] { "MarketplaceOrderId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceOrders_CheckoutSessionId",
                table: "MarketplaceOrders",
                column: "CheckoutSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceOrders_CustomerId_CreatedAtUtc",
                table: "MarketplaceOrders",
                columns: new[] { "CustomerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceOrders_IdempotencyKey",
                table: "MarketplaceOrders",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceOrders_OrderNumber",
                table: "MarketplaceOrders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Rentals_MarketplaceOrders_MarketplaceOrderId",
                table: "Rentals",
                column: "MarketplaceOrderId",
                principalTable: "MarketplaceOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Rentals_MarketplaceOrders_MarketplaceOrderId",
                table: "Rentals");

            migrationBuilder.DropTable(
                name: "MarketplaceOrders");

            migrationBuilder.DropIndex(
                name: "IX_Rentals_MarketplaceOrderId",
                table: "Rentals");

            migrationBuilder.DropIndex(
                name: "IX_Rentals_MarketplaceOrderId_OrderSequence",
                table: "Rentals");

            migrationBuilder.DropIndex(
                name: "IX_Rentals_MarketplaceOrderId_TenantId",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "MarketplaceOrderId",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "OrderSequence",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "RegulationsHash",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "RegulationsSource",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "RegulationsTextSnapshot",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "RegulationsVersion",
                table: "Rentals");

            migrationBuilder.AlterColumn<decimal>(
                name: "DepositAmount",
                table: "Rentals",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);
        }
    }
}
