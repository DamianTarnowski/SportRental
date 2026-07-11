using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportRental.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalDocumentAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcceptedTermsVersion",
                table: "CheckoutSessions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcknowledgedPrivacyVersion",
                table: "CheckoutSessions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LegalAcceptedAtUtc",
                table: "CheckoutSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcceptedTermsVersion",
                table: "AspNetUsers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcknowledgedPrivacyVersion",
                table: "AspNetUsers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LegalAcceptedAtUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptedTermsVersion",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "AcknowledgedPrivacyVersion",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "LegalAcceptedAtUtc",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "AcceptedTermsVersion",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AcknowledgedPrivacyVersion",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LegalAcceptedAtUtc",
                table: "AspNetUsers");
        }
    }
}
