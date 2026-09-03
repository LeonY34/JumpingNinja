namespace JumpingNinja.Api.Auth;

public sealed class AuthCredentials
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public sealed record AuthUserResponse(Guid Id, string Username);

public sealed record AuthResponse(
    AuthUserResponse User,
    string AccessToken,
    DateTimeOffset ExpiresAt);

public sealed record ErrorResponse(
    string Code,
    string Message,
    string? Field = null);
