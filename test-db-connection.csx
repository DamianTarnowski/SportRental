#r "nuget: Npgsql, 9.0.2"

using Npgsql;

var connectionString = "Host=eduedu.postgres.database.azure.com;Database=sr;Username=synapsis;Password=HasloHaslo122@@@@;SSL Mode=Require";

Console.WriteLine("🔍 SPRAWDZAM POŁĄCZENIE DO BAZY SR...\n");

try
{
    using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    
    Console.WriteLine("✅ POŁĄCZENIE OK!\n");
    Console.WriteLine($"📊 Baza danych: {conn.Database}");
    Console.WriteLine($"👤 Użytkownik: {conn.UserName}");
    Console.WriteLine($"🖥️  Host: {conn.Host}\n");
    
    Console.WriteLine("📋 LISTA TABEL W BAZIE:\n");
    
    using var cmd = new NpgsqlCommand(@"
        SELECT table_name 
        FROM information_schema.tables 
        WHERE table_schema = 'public' 
        ORDER BY table_name", conn);
    
    using var reader = await cmd.ExecuteReaderAsync();
    int count = 0;
    while (await reader.ReadAsync())
    {
        count++;
        Console.WriteLine($"   {count}. {reader.GetString(0)}");
    }
    
    Console.WriteLine($"\n✅ Znaleziono {count} tabel w bazie 'sr'");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ BŁĄD: {ex.Message}\n");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}



