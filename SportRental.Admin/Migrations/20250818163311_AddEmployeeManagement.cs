using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportRental.Admin.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Telephone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Position = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    AllRentalsNumber = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Employees_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeePermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    KierownikCanAddClient = table.Column<bool>(type: "boolean", nullable: false),
                    KierownikCanEditClient = table.Column<bool>(type: "boolean", nullable: false),
                    KierownikCanDeleteClient = table.Column<bool>(type: "boolean", nullable: false),
                    KierownikCanAddProduct = table.Column<bool>(type: "boolean", nullable: false),
                    KierownikCanEditProduct = table.Column<bool>(type: "boolean", nullable: false),
                    KierownikCanDeleteProduct = table.Column<bool>(type: "boolean", nullable: false),
                    KierownikCanAddRental = table.Column<bool>(type: "boolean", nullable: false),
                    KierownikCanEditRental = table.Column<bool>(type: "boolean", nullable: false),
                    KierownikCanDeleteRental = table.Column<bool>(type: "boolean", nullable: false),
                    KierownikCanAddEmployee = table.Column<bool>(type: "boolean", nullable: false),
                    KierownikCanEditEmployee = table.Column<bool>(type: "boolean", nullable: false),
                    KierownikCanDeleteEmployee = table.Column<bool>(type: "boolean", nullable: false),
                    KierownikCanSeeReports = table.Column<bool>(type: "boolean", nullable: false),
                    KierownikCanAddMultipleRental = table.Column<bool>(type: "boolean", nullable: false),
                    KierownikCanEditMultipleRental = table.Column<bool>(type: "boolean", nullable: false),
                    KierownikCanDeleteMultipleRental = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerCanAddClient = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerCanEditClient = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerCanDeleteClient = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerCanAddProduct = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerCanEditProduct = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerCanDeleteProduct = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerCanAddRental = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerCanEditRental = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerCanDeleteRental = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerCanAddEmployee = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerCanEditEmployee = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerCanDeleteEmployee = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerCanSeeReports = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerCanAddMultipleRental = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerCanEditMultipleRental = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerCanDeleteMultipleRental = table.Column<bool>(type: "boolean", nullable: false),
                    PracownikCanAddClient = table.Column<bool>(type: "boolean", nullable: false),
                    PracownikCanEditClient = table.Column<bool>(type: "boolean", nullable: false),
                    PracownikCanDeleteClient = table.Column<bool>(type: "boolean", nullable: false),
                    PracownikCanAddProduct = table.Column<bool>(type: "boolean", nullable: false),
                    PracownikCanEditProduct = table.Column<bool>(type: "boolean", nullable: false),
                    PracownikCanDeleteProduct = table.Column<bool>(type: "boolean", nullable: false),
                    PracownikCanAddRental = table.Column<bool>(type: "boolean", nullable: false),
                    PracownikCanEditRental = table.Column<bool>(type: "boolean", nullable: false),
                    PracownikCanDeleteRental = table.Column<bool>(type: "boolean", nullable: false),
                    PracownikCanAddEmployee = table.Column<bool>(type: "boolean", nullable: false),
                    PracownikCanEditEmployee = table.Column<bool>(type: "boolean", nullable: false),
                    PracownikCanDeleteEmployee = table.Column<bool>(type: "boolean", nullable: false),
                    PracownikCanSeeReports = table.Column<bool>(type: "boolean", nullable: false),
                    PracownikCanAddMultipleRental = table.Column<bool>(type: "boolean", nullable: false),
                    PracownikCanEditMultipleRental = table.Column<bool>(type: "boolean", nullable: false),
                    PracownikCanDeleteMultipleRental = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeePermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeePermissions_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeePermissions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePermissions_EmployeeId",
                table: "EmployeePermissions",
                column: "EmployeeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePermissions_TenantId",
                table: "EmployeePermissions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId_Email",
                table: "Employees",
                columns: new[] { "TenantId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId_FullName",
                table: "Employees",
                columns: new[] { "TenantId", "FullName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeePermissions");

            migrationBuilder.DropTable(
                name: "Employees");
        }
    }
}
