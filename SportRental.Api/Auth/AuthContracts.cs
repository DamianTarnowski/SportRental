namespace SportRental.Api.Auth;

public sealed record LoginRequest(string Email, string Password);

public sealed record RegisterRequest(
    string Email, 
    string Password, 
    string? FullName = null,
    string? PhoneNumber = null,
    string? DocumentNumber = null);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record RevokeTokenRequest(string RefreshToken);

public sealed record AuthResponse
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required int ExpiresIn { get; init; }
    public required string TokenType { get; init; }
    public required UserInfo User { get; init; }
}

public sealed record UserInfo
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public required Guid TenantId { get; init; }
}
