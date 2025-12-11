using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportRental.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRentalSourceAndEquipmentHandling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DamageCharge",
                table: "Rentals",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IssueNotes",
                table: "Rentals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IssuedAtUtc",
                table: "Rentals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IssuedByEmployeeId",
                table: "Rentals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReturnDepositRefund",
                table: "Rentals",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnNotes",
                table: "Rentals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnedAtUtc",
                table: "Rentals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReturnedByEmployeeId",
                table: "Rentals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "Rentals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "KierownikCanIssueEquipment",
                table: "EmployeePermissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "KierownikCanReturnEquipment",
                table: "EmployeePermissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ManagerCanIssueEquipment",
                table: "EmployeePermissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ManagerCanReturnEquipment",
                table: "EmployeePermissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PracownikCanIssueEquipment",
                table: "EmployeePermissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PracownikCanReturnEquipment",
                table: "EmployeePermissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Rentals_IssuedByEmployeeId",
                table: "Rentals",
                column: "IssuedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Rentals_ReturnedByEmployeeId",
                table: "Rentals",
                column: "ReturnedByEmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Rentals_Employees_IssuedByEmployeeId",
                table: "Rentals",
                column: "IssuedByEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Rentals_Employees_ReturnedByEmployeeId",
                table: "Rentals",
                column: "ReturnedByEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Rentals_Employees_IssuedByEmployeeId",
                table: "Rentals");

            migrationBuilder.DropForeignKey(
                name: "FK_Rentals_Employees_ReturnedByEmployeeId",
                table: "Rentals");

            migrationBuilder.DropIndex(
                name: "IX_Rentals_IssuedByEmployeeId",
                table: "Rentals");

            migrationBuilder.DropIndex(
                name: "IX_Rentals_ReturnedByEmployeeId",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "DamageCharge",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "IssueNotes",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "IssuedAtUtc",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "IssuedByEmployeeId",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "ReturnDepositRefund",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "ReturnNotes",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "ReturnedAtUtc",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "ReturnedByEmployeeId",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "KierownikCanIssueEquipment",
                table: "EmployeePermissions");

            migrationBuilder.DropColumn(
                name: "KierownikCanReturnEquipment",
                table: "EmployeePermissions");

            migrationBuilder.DropColumn(
                name: "ManagerCanIssueEquipment",
                table: "EmployeePermissions");

            migrationBuilder.DropColumn(
                name: "ManagerCanReturnEquipment",
                table: "EmployeePermissions");

            migrationBuilder.DropColumn(
                name: "PracownikCanIssueEquipment",
                table: "EmployeePermissions");

            migrationBuilder.DropColumn(
                name: "PracownikCanReturnEquipment",
                table: "EmployeePermissions");
        }
    }
}
