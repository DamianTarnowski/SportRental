using Npgsql;

var connStr = "Host=eduedu.postgres.database.azure.com;Database=sr;Username=synapsis;Password=HasloHaslo122@@@@;SSL Mode=Require";
await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();

// Przepnij do Narty & Snowboard Zakopane
var zakopaneTenantId = Guid.Parse("547f5df7-a389-44b3-bcc6-090ff2fa92e5");
Console.WriteLine("Przepinam hdtdtr@gmail.com do Narty & Snowboard Zakopane...");
await using (var cmd = new NpgsqlCommand("UPDATE \"AspNetUsers\" SET \"TenantId\" = @tenantId WHERE \"Email\" = 'hdtdtr@gmail.com'", conn))
{
    cmd.Parameters.AddWithValue("tenantId", zakopaneTenantId);
    await cmd.ExecuteNonQueryAsync();
}

// Pokaż wynik
await using (var cmd = new NpgsqlCommand(@"
    SELECT t.""Name"", (SELECT COUNT(*) FROM ""Products"" WHERE ""TenantId"" = t.""Id"") as products
    FROM ""Tenants"" t
    JOIN ""AspNetUsers"" u ON u.""TenantId"" = t.""Id""
    WHERE u.""Email"" = 'hdtdtr@gmail.com'", conn))
await using (var reader = await cmd.ExecuteReaderAsync())
{
    if (await reader.ReadAsync())
    {
        Console.WriteLine($"✅ hdtdtr@gmail.com -> '{reader.GetString(0)}' ({reader.GetInt64(1)} produktów)");
    }
}

// Lista tenantów
Console.WriteLine("\n=== TENANTY ===");
await using (var cmd = new NpgsqlCommand(@"
    SELECT t.""Id"", t.""Name"", 
           (SELECT COUNT(*) FROM ""Products"" WHERE ""TenantId"" = t.""Id"") as products,
           (SELECT COUNT(*) FROM ""Customers"" WHERE ""TenantId"" = t.""Id"") as customers
    FROM ""Tenants"" t ORDER BY products DESC", conn))
await using (var reader = await cmd.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
    {
        Console.WriteLine($"  {reader.GetGuid(0)}: {reader.GetString(1)} - {reader.GetInt64(2)} produktów, {reader.GetInt64(3)} klientów");
    }
}

Console.WriteLine("\n=== NAPRAWIANIE DANYCH ===\n");

// 1. Usuń puste "Default Tenant" (bez produktów i bez użytkowników)
Console.WriteLine("Usuwam puste 'Default Tenant'...");
await using (var cmd = new NpgsqlCommand(@"
    DELETE FROM ""Tenants"" 
    WHERE ""Name"" = 'Default Tenant' 
    AND ""Id"" NOT IN (SELECT DISTINCT ""TenantId"" FROM ""Products"" WHERE ""TenantId"" IS NOT NULL)
    AND ""Id"" NOT IN (SELECT DISTINCT ""TenantId"" FROM ""AspNetUsers"" WHERE ""TenantId"" IS NOT NULL)
    AND ""Id"" NOT IN (SELECT DISTINCT ""TenantId"" FROM ""Customers"" WHERE ""TenantId"" IS NOT NULL)", conn))
{
    var deleted = await cmd.ExecuteNonQueryAsync();
    Console.WriteLine($"  Usunięto {deleted} pustych tenantów");
}

// 2. Znajdź tenant z największą liczbą produktów (Zakopane)
Guid mainTenantId;
await using (var cmd = new NpgsqlCommand(@"
    SELECT t.""Id"" 
    FROM ""Tenants"" t 
    JOIN ""Products"" p ON p.""TenantId"" = t.""Id"" 
    GROUP BY t.""Id"" 
    ORDER BY COUNT(p.""Id"") DESC 
    LIMIT 1", conn))
{
    mainTenantId = (Guid)(await cmd.ExecuteScalarAsync())!;
    Console.WriteLine($"\n  Główny tenant (najwięcej produktów): {mainTenantId}");
}

// 3. Sprawdź aktualny TenantId użytkownika
Guid? currentUserTenantId;
await using (var cmd = new NpgsqlCommand("SELECT \"TenantId\" FROM \"AspNetUsers\" WHERE \"Email\" = 'hdtdtr@gmail.com'", conn))
{
    var result = await cmd.ExecuteScalarAsync();
    currentUserTenantId = result == DBNull.Value ? null : (Guid?)result;
    Console.WriteLine($"  Aktualny TenantId użytkownika: {currentUserTenantId}");
}

// 4. Jeśli użytkownik nie jest w głównym tenancie - przepnij go
if (currentUserTenantId != mainTenantId)
{
    Console.WriteLine($"\n  Przepinam użytkownika do głównego tenanta...");
    await using (var cmd = new NpgsqlCommand(@"
        UPDATE ""AspNetUsers"" 
        SET ""TenantId"" = @tenantId 
        WHERE ""Email"" = 'hdtdtr@gmail.com'", conn))
    {
        cmd.Parameters.AddWithValue("tenantId", mainTenantId);
        await cmd.ExecuteNonQueryAsync();
        Console.WriteLine("  ✓ Przepięto!");
    }
}
else
{
    Console.WriteLine("  ✓ Użytkownik już jest w głównym tenancie");
}

// 5. Pokaż podsumowanie
Console.WriteLine("\n=== PODSUMOWANIE ===");
await using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Tenants\"", conn))
{
    Console.WriteLine($"  Tenanty: {await cmd.ExecuteScalarAsync()}");
}
await using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Products\"", conn))
{
    Console.WriteLine($"  Produkty: {await cmd.ExecuteScalarAsync()}");
}
await using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Customers\"", conn))
{
    Console.WriteLine($"  Klienci: {await cmd.ExecuteScalarAsync()}");
}

// 6. Pokaż tenanta użytkownika
await using (var cmd = new NpgsqlCommand(@"
    SELECT t.""Name"", (SELECT COUNT(*) FROM ""Products"" WHERE ""TenantId"" = t.""Id"") as products
    FROM ""Tenants"" t
    JOIN ""AspNetUsers"" u ON u.""TenantId"" = t.""Id""
    WHERE u.""Email"" = 'hdtdtr@gmail.com'", conn))
await using (var reader = await cmd.ExecuteReaderAsync())
{
    if (await reader.ReadAsync())
    {
        Console.WriteLine($"\n  hdtdtr@gmail.com jest teraz w: '{reader.GetString(0)}' ({reader.GetInt64(1)} produktów)");
    }
}

Console.WriteLine("\n✅ Gotowe! Teraz dane powinny być widoczne.");

