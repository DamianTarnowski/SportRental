using Npgsql;

var connStr = "Host=eduedu.postgres.database.azure.com;Database=sr;Username=synapsis;Password=HasloHaslo122@@@@;SSL Mode=Require";
await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();

// Use simple alphanumeric token
var newToken = "test" + DateTime.UtcNow.Ticks.ToString();
var id = Guid.NewGuid();
var email = "sportrental.kontakt@gmail.com";
var tenantName = "Test Ski Rental";
var createdAt = DateTime.UtcNow;
var expiresAt = createdAt.AddDays(7);

// Delete old and insert new
await using var deleteCmd = new NpgsqlCommand(@"DELETE FROM ""TenantInvitations"" WHERE ""Email"" = 'sportrental.kontakt@gmail.com'", conn);
await deleteCmd.ExecuteNonQueryAsync();

await using var insertCmd = new NpgsqlCommand(@"
    INSERT INTO ""TenantInvitations"" (""Id"", ""Email"", ""TenantName"", ""Token"", ""CreatedAtUtc"", ""ExpiresAtUtc"", ""IsUsed"")
    VALUES (@id, @email, @tenantName, @token, @createdAt, @expiresAt, false)", conn);

insertCmd.Parameters.AddWithValue("id", id);
insertCmd.Parameters.AddWithValue("email", email);
insertCmd.Parameters.AddWithValue("tenantName", tenantName);
insertCmd.Parameters.AddWithValue("token", newToken);
insertCmd.Parameters.AddWithValue("createdAt", createdAt);
insertCmd.Parameters.AddWithValue("expiresAt", expiresAt);

await insertCmd.ExecuteNonQueryAsync();

Console.WriteLine($"Token: {newToken}");
Console.WriteLine($"Link: https://sradmin2.azurewebsites.net/Account/RegisterOwner?token={newToken}");
