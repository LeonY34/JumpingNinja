using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using JumpingNinja.Api.Data;
using Microsoft.IdentityModel.Tokens;

namespace JumpingNinja.Api.Auth;

public sealed record JwtAccessToken(string Value, DateTimeOffset ExpiresAt);

public sealed class JwtTokenService
{
    private readonly JwtOptions options;
    private readonly SigningCredentials signingCredentials;

    public JwtTokenService(JwtOptions options)
    {
        this.options = options;
        signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
    }

    public JwtAccessToken Create(ApplicationUser user)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(options.AccessTokenMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName ?? string.Empty)
        };

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: signingCredentials);

        return new JwtAccessToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt);
    }
}
