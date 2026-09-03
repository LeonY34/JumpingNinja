namespace JumpingNinja.Api.Auth;

public sealed class JwtOptions
{
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required string SigningKey { get; init; }
    public int AccessTokenMinutes { get; init; }

    public static JwtOptions FromConfiguration(IConfiguration configuration)
    {
        var accessTokenMinutes = configuration.GetValue("Jwt:AccessTokenMinutes", 120);

        return new JwtOptions
        {
            Issuer = configuration["Jwt:Issuer"] ?? "JumpingNinja.Auth",
            Audience = configuration["Jwt:Audience"] ?? "JumpingNinja.Client",
            SigningKey = configuration["Jwt:SigningKey"] ?? string.Empty,
            AccessTokenMinutes = Math.Clamp(accessTokenMinutes, 15, 720)
        };
    }
}
