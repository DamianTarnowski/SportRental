using Npgsql;

var connStr = "Host=eduedu.postgres.database.azure.com;Database=sr;Username=synapsis;Password=HasloHaslo122@@@@;SSL Mode=Require";
await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();

Console.WriteLine("=== SPRAWDZANIE DANYCH W BAZIE ===\n");

// Liczba produktów
await using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Products\"", conn))
{
    var count = await cmd.ExecuteScalarAsync();
    Console.WriteLine($"Produkty: {count}");
}

// Liczba klientów
await using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Customers\"", conn))
{
    var count = await cmd.ExecuteScalarAsync();
    Console.WriteLine($"Klienci: {count}");
}

// Liczba wynajmów
await using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Rentals\"", conn))
{
    var count = await cmd.ExecuteScalarAsync();
    Console.WriteLine($"Wynajmy: {count}");
}

// Tenanty
Console.WriteLine("\n=== TENANTY ===");
await using (var cmd = new NpgsqlCommand("SELECT \"Id\", \"Name\" FROM \"Tenants\"", conn))
await using (var reader = await cmd.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
    {
        Console.WriteLine($"  {reader.GetGuid(0)}: {reader.GetString(1)}");
    }
}

// Użytkownik hdtdtr@gmail.com
Console.WriteLine("\n=== UŻYTKOWNIK hdtdtr@gmail.com ===");
await using (var cmd = new NpgsqlCommand("SELECT \"Id\", \"Email\", \"TenantId\" FROM \"AspNetUsers\" WHERE \"Email\" = 'hdtdtr@gmail.com'", conn))
await using (var reader = await cmd.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
    {
        var tenantId = reader.IsDBNull(2) ? "NULL" : reader.GetGuid(2).ToString();
        Console.WriteLine($"  UserId: {reader.GetGuid(0)}");
        Console.WriteLine($"  Email: {reader.GetString(1)}");
        Console.WriteLine($"  TenantId: {tenantId}");
    }
}

// Produkty per tenant
Console.WriteLine("\n=== PRODUKTY PER TENANT ===");
await using (var cmd = new NpgsqlCommand(@"
    SELECT t.""Name"", COUNT(p.""Id"") 
    FROM ""Tenants"" t 
    LEFT JOIN ""Products"" p ON p.""TenantId"" = t.""Id"" 
    GROUP BY t.""Id"", t.""Name""
    ORDER BY COUNT(p.""Id"") DESC", conn))
await using (var reader = await cmd.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
    {
        Console.WriteLine($"  {reader.GetString(0)}: {reader.GetInt64(1)} produktów");
    }
}

