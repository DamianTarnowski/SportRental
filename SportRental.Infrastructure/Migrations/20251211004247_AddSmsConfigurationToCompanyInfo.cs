using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportRental.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsConfigurationToCompanyInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SmsConfirmationEnabled",
                table: "CompanyInfos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SmsReminderEnabled",
                table: "CompanyInfos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SmsThanksEnabled",
                table: "CompanyInfos",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SmsConfirmationEnabled",
                table: "CompanyInfos");

            migrationBuilder.DropColumn(
                name: "SmsReminderEnabled",
                table: "CompanyInfos");

            migrationBuilder.DropColumn(
                name: "SmsThanksEnabled",
                table: "CompanyInfos");
        }
    }
}
