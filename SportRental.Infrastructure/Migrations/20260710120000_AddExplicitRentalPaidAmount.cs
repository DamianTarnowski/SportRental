using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportRental.Infrastructure.Data;

#nullable disable

namespace SportRental.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260710120000_AddExplicitRentalPaidAmount")]
    public partial class AddExplicitRentalPaidAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "CheckoutSessions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundedAtUtc",
                table: "CheckoutSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DepositPaidAtUtc",
                table: "Rentals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidAmount",
                table: "Rentals",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Historyczne płatności manualne były błędnie zapisywane jako DepositPaid.
            // Online oznacza pobrany przez Stripe zwrotny depozyt (nie przychód z najmu);
            // pozostałe potwierdzone statusy reprezentowały w panelu pełną zapłatę.
            migrationBuilder.Sql("""
                UPDATE "Rentals"
                SET "PaidAmount" = CASE
                    WHEN lower(COALESCE("PaymentStatus", '')) IN ('refunded', 'depositrefunded') THEN 0
                    WHEN lower(COALESCE("PaymentStatus", '')) = 'depositpaid'
                         AND lower(COALESCE("PaymentMethod", '')) = 'online'
                        THEN 0
                    WHEN lower(COALESCE("PaymentStatus", '')) IN ('depositpaid', 'succeeded', 'paid')
                        THEN GREATEST("TotalAmount", 0)
                    ELSE 0
                END,
                "DepositPaidAtUtc" = CASE
                    WHEN lower(COALESCE("PaymentStatus", '')) = 'depositpaid'
                         AND lower(COALESCE("PaymentMethod", '')) = 'online'
                        THEN "PaidAtUtc"
                    ELSE NULL
                END;
                """);

            // Publiczna rejestracja historycznie ustawiała EmailConfirmed=true bez
            // potwierdzenia. Konta Google mają wpis w AspNetUserLogins i pozostają
            // potwierdzone; konta klienckie tylko z hasłem wracają do prawdziwego stanu.
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" AS u
                SET "EmailConfirmed" = FALSE
                WHERE u."TenantId" IS NULL
                AND u."EmailConfirmed" = TRUE
                AND EXISTS (
                    SELECT 1
                    FROM "AspNetUserRoles" ur
                    JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
                    WHERE ur."UserId" = u."Id" AND r."NormalizedName" = 'CLIENT'
                )
                AND NOT EXISTS (
                    SELECT 1
                    FROM "AspNetUserRoles" ur
                    JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
                    WHERE ur."UserId" = u."Id"
                      AND r."NormalizedName" IN ('OWNER', 'EMPLOYEE', 'SUPERADMIN')
                )
                AND NOT EXISTS (
                    SELECT 1 FROM "AspNetUserLogins" ul WHERE ul."UserId" = u."Id"
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "RefundedAtUtc",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "DepositPaidAtUtc",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "PaidAmount",
                table: "Rentals");
        }
    }
}
