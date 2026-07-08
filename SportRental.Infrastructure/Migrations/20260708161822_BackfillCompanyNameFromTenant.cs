using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportRental.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillCompanyNameFromTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Feedback #8: nazwa wypożyczalni na umowie PDF czyta CompanyInfo.Name, które było
            // ustawiane tylko RAZ przy rejestracji na placeholder "Wypożyczalnia {local-part-emaila}".
            // Formularz "Ustawienia firmy" edytuje Tenant.Name (pole "Nazwa firmy"), a to jest źródło
            // prawdy. Synchronizujemy istniejące rekordy, żeby najemcy NIE musieli ponownie zapisywać
            // ustawień, aby nazwa na umowie się poprawiła. Save() od teraz utrzymuje tę spójność.
            migrationBuilder.Sql(@"
                UPDATE ""CompanyInfos"" AS c
                SET ""Name"" = t.""Name""
                FROM ""Tenants"" AS t
                WHERE c.""TenantId"" = t.""Id""
                  AND t.""Name"" IS NOT NULL
                  AND c.""Name"" IS DISTINCT FROM t.""Name"";
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Backfill danych — brak sensownego rollbacku (nie znamy poprzednich wartości).
        }
    }
}
