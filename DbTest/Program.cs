using Npgsql;

var connectionString = "Host=eduedu.postgres.database.azure.com;Database=sr;Username=synapsis;Password=HasloHaslo122@@@@;SSL Mode=Require";

Console.WriteLine("🔍 SPRAWDZAM ZAWARTOŚĆ BAZY SR...\n");

try
{
    using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    
    Console.WriteLine("✅ POŁĄCZENIE OK!\n");
    
    // Sprawdź ilość produktów
    using (var prodCmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Products\"", conn))
    {
        var prodCount = await prodCmd.ExecuteScalarAsync();
        Console.WriteLine($"📦 Produkty: {prodCount}");
    }
    
    // Sprawdź ilość tenantów
    using (var tenantCmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Tenants\"", conn))
    {
        var tenantCount = await tenantCmd.ExecuteScalarAsync();
        Console.WriteLine($"🏢 Tenanci: {tenantCount}");
    }
    
    // Sprawdź ilość klientów
    using (var customerCmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Customers\"", conn))
    {
        var customerCount = await customerCmd.ExecuteScalarAsync();
        Console.WriteLine($"👤 Klienci: {customerCount}");
    }
    
    // Sprawdź ilość wynajmów
    using (var rentalCmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Rentals\"", conn))
    {
        var rentalCount = await rentalCmd.ExecuteScalarAsync();
        Console.WriteLine($"🚀 Wynajmy: {rentalCount}\n");
    }
    
    // Lista tenantów
    Console.WriteLine("🏢 LISTA TENANTÓW:\n");
    using (var listCmd = new NpgsqlCommand("SELECT \"Id\", \"Name\" FROM \"Tenants\" ORDER BY \"Name\"", conn))
    using (var reader = await listCmd.ExecuteReaderAsync())
    {
        int i = 1;
        while (await reader.ReadAsync())
        {
            var id = reader.GetGuid(0);
            var name = reader.GetString(1);
            Console.WriteLine($"   {i}. {name} ({id})");
            i++;
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ BŁĄD: {ex.Message}");
}
