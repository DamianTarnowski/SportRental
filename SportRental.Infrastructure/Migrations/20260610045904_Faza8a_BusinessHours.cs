using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportRental.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Faza8a_BusinessHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessHoursExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    CustomOpen = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    CustomClose = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessHoursExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessHoursExceptions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BusinessHoursSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessHoursSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessHoursSchedules_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BusinessHoursDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    OpenFrom = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    OpenTo = table.Column<TimeOnly>(type: "time without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessHoursDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessHoursDays_BusinessHoursSchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "BusinessHoursSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessHoursDays_ScheduleId_DayOfWeek",
                table: "BusinessHoursDays",
                columns: new[] { "ScheduleId", "DayOfWeek" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessHoursExceptions_TenantId_Date",
                table: "BusinessHoursExceptions",
                columns: new[] { "TenantId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessHoursSchedules_TenantId",
                table: "BusinessHoursSchedules",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessHoursDays");

            migrationBuilder.DropTable(
                name: "BusinessHoursExceptions");

            migrationBuilder.DropTable(
                name: "BusinessHoursSchedules");
        }
    }
}
