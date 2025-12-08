using Npgsql;

var connStr = "Host=eduedu.postgres.database.azure.com;Database=sr;Username=synapsis;Password=HasloHaslo122@@@@;SSL Mode=Require";
await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();
await using var cmd = new NpgsqlCommand("SELECT \"Email\", \"Token\" FROM \"TenantInvitations\" ORDER BY \"CreatedAtUtc\" DESC LIMIT 5", conn);
await using var reader = await cmd.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    Console.WriteLine($"Email: {reader.GetString(0)}");
    Console.WriteLine($"Token: {reader.GetString(1)}");
    Console.WriteLine($"Link: https://sradmin2.azurewebsites.net/Account/RegisterOwner?token={reader.GetString(1)}");
    Console.WriteLine();
}
