using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using JumpingNinja.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace JumpingNinja.Tests;

public sealed class AuthApiTests : IClassFixture<AuthApiFactory>
{
    private readonly HttpClient client;

    public AuthApiTests(AuthApiFactory factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task InvalidRegistrationDetailsReturnFieldValidationErrors()
    {
        HttpResponseMessage invalidUsername = await PostJsonAsync(
            "/api/v1/auth/register",
            "x",
            "TestPassword123");
        Assert.Equal(HttpStatusCode.BadRequest, invalidUsername.StatusCode);

        HttpResponseMessage weakPassword = await PostJsonAsync(
            "/api/v1/auth/register",
            "valid_name",
            "password");
        Assert.Equal(HttpStatusCode.BadRequest, weakPassword.StatusCode);
    }

    [Fact]
    public async Task RegisterLoginAndMeUseTheSameIdentity()
    {
        string username = "api_" + Guid.NewGuid().ToString("N")[..8];
        const string password = "TestPassword123";

        HttpResponseMessage register = await PostJsonAsync("/api/v1/auth/register", username, password);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        JsonDocument registerBody = await ReadJsonAsync(register);
        string token = registerBody.RootElement.GetProperty("accessToken").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(username, registerBody.RootElement.GetProperty("user").GetProperty("username").GetString());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpResponseMessage me = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        HttpResponseMessage missingToken = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, missingToken.StatusCode);
        JsonDocument missingTokenBody = await ReadJsonAsync(missingToken);
        Assert.Equal("unauthorized", missingTokenBody.RootElement.GetProperty("code").GetString());

        HttpResponseMessage login = await PostJsonAsync(
            "/api/v1/auth/login",
            username.ToUpperInvariant(),
            password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        HttpResponseMessage wrongPassword = await PostJsonAsync(
            "/api/v1/auth/login",
            username,
            "WrongPassword123");
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);

        HttpResponseMessage duplicate = await PostJsonAsync(
            "/api/v1/auth/register",
            username.ToUpperInvariant(),
            password);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task ExpiredAndMalformedTokensAreRejected()
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");
        HttpResponseMessage malformed = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, malformed.StatusCode);
        JsonDocument malformedBody = await ReadJsonAsync(malformed);
        Assert.Equal("unauthorized", malformedBody.RootElement.GetProperty("code").GetString());

        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(AuthApiFactory.TestSigningKey));
        var expiredToken = new JwtSecurityToken(
            issuer: "JumpingNinja.Auth",
            audience: "JumpingNinja.Client",
            claims: new[] { new Claim("sub", Guid.NewGuid().ToString()) },
            expires: DateTime.UtcNow.AddMinutes(-5),
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            new JwtSecurityTokenHandler().WriteToken(expiredToken));
        HttpResponseMessage expired = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);

        var missingSubjectToken = new JwtSecurityToken(
            issuer: "JumpingNinja.Auth",
            audience: "JumpingNinja.Client",
            claims: Array.Empty<Claim>(),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            new JwtSecurityTokenHandler().WriteToken(missingSubjectToken));
        HttpResponseMessage missingSubject = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, missingSubject.StatusCode);
        JsonDocument missingSubjectBody = await ReadJsonAsync(missingSubject);
        Assert.Equal("unauthorized", missingSubjectBody.RootElement.GetProperty("code").GetString());
        client.DefaultRequestHeaders.Authorization = null;
    }

    private async Task<HttpResponseMessage> PostJsonAsync(
        string path,
        string username,
        string password)
    {
        var payload = new
        {
            username,
            password
        };
        using var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");
        return await client.PostAsync(path, content);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using Stream body = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(body);
    }
}

public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    public const string TestSigningKey = "test-only-jumping-ninja-signing-key-32-bytes-minimum";
    private readonly string databaseName;
    private readonly string[] knownProxies;

    public AuthApiFactory()
        : this("jumping-ninja-auth-tests", Array.Empty<string>())
    {
    }

    private AuthApiFactory(string databaseName, string[] knownProxies)
    {
        this.databaseName = databaseName;
        this.knownProxies = knownProxies;
    }

    public static AuthApiFactory CreateIsolated(string databaseName) =>
        new(databaseName, Array.Empty<string>());

    public static AuthApiFactory CreateIsolated(string databaseName, params string[] knownProxies) =>
        new(databaseName, knownProxies);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=unused;Database=unused");
        builder.UseSetting("Jwt:SigningKey", TestSigningKey);
        builder.UseSetting("Jwt:AccessTokenMinutes", "120");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=unused;Database=unused",
                ["Jwt:SigningKey"] = TestSigningKey,
                ["Jwt:AccessTokenMinutes"] = "120"
            };

            foreach (var (proxy, index) in knownProxies.Select((proxy, index) => (proxy, index)))
            {
                settings[$"ReverseProxy:KnownProxies:{index}"] = proxy;
            }

            configuration.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
        });
    }
}

public sealed class AuthRateLimitTests
{
    [Fact]
    public async Task RegistrationRateLimitReturns429()
    {
        using var factory = AuthApiFactory.CreateIsolated("jumping-ninja-register-rate-limit-" + Guid.NewGuid().ToString("N"));
        using HttpClient client = factory.CreateClient();

        for (int index = 0; index < 5; index++)
        {
            HttpResponseMessage response = await PostJsonAsync(
                client,
                "/api/v1/auth/register",
                "invalid",
                "weak");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        HttpResponseMessage limited = await PostJsonAsync(
            client,
            "/api/v1/auth/register",
            "invalid",
            "weak");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task LoginRateLimitReturns429()
    {
        using var factory = AuthApiFactory.CreateIsolated("jumping-ninja-login-rate-limit-" + Guid.NewGuid().ToString("N"));
        using HttpClient client = factory.CreateClient();

        for (int index = 0; index < 10; index++)
        {
            HttpResponseMessage response = await PostJsonAsync(
                client,
                "/api/v1/auth/login",
                "missing_user",
                "TestPassword123");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        HttpResponseMessage limited = await PostJsonAsync(
            client,
            "/api/v1/auth/login",
            "missing_user",
            "TestPassword123");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task TrustedForwardedForAddressesUseSeparateRateLimitBuckets()
    {
        using var factory = AuthApiFactory.CreateIsolated(
            "jumping-ninja-forwarded-rate-limit-" + Guid.NewGuid().ToString("N"),
            "127.0.0.1",
            "::1");
        using HttpClient client = factory.CreateClient();

        foreach (string address in new[] { "203.0.113.10", "203.0.113.11" })
        {
            for (int index = 0; index < 5; index++)
            {
                HttpResponseMessage response = await PostJsonAsync(
                    client,
                    "/api/v1/auth/register",
                    "invalid",
                    "weak",
                    address);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }

            HttpResponseMessage limited = await PostJsonAsync(
                client,
                "/api/v1/auth/register",
                "invalid",
                "weak",
                address);
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        }
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(
        HttpClient client,
        string path,
        string username,
        string password,
        string? forwardedFor = null)
    {
        var payload = new
        {
            username,
            password
        };
        using var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = content
        };
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        }

        return await client.SendAsync(request);
    }
}
