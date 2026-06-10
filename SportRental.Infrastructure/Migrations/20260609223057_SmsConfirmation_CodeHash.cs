using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportRental.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SmsConfirmation_CodeHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SEC-012: MaxLength 10 -> 128 zeby pomiescic SHA-256(Id||code) base64 (~44 znaki).
            // EF nie wykryl zmiany [MaxLength] bo poprzednia migracja nie zapisala
            // .HasMaxLength(10) fluent — typ kolumny w PG zostal varchar(10).
            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "SmsConfirmations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "SmsConfirmations",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);
        }
    }
}
