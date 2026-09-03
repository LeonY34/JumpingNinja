using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using JumpingNinja.Api.Auth;
using JumpingNinja.Api.Data;
using JumpingNinja.Api.Leaderboard;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
var jwtOptions = JwtOptions.FromConfiguration(builder.Configuration);
string[] knownProxyValues = builder.Configuration
    .GetSection("ReverseProxy:KnownProxies")
    .GetChildren()
    .Select(section => section.Value)
    .Where(value => !string.IsNullOrWhiteSpace(value))
    .Select(value => value!)
    .ToArray();
IPAddress[] knownProxyAddresses = knownProxyValues
    .Select(value => IPAddress.TryParse(value, out IPAddress? address)
        ? address
        : throw new InvalidOperationException($"ReverseProxy:KnownProxies contains an invalid IP address: {value}."))
    .ToArray();

if (Encoding.UTF8.GetByteCount(jwtOptions.SigningKey) < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 UTF-8 bytes.");
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_";
        options.Password.RequiredLength = AuthRules.MinimumPasswordLength;
        options.Password.RequiredUniqueChars = 0;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<LeaderboardService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(15),
            NameClaimType = "unique_name",
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ErrorResponse("unauthorized", "Authentication is required."));
            },
            OnForbidden = async context =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ErrorResponse("forbidden", "You are not allowed to access this resource."));
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    foreach (IPAddress address in knownProxyAddresses)
    {
        options.KnownProxies.Add(address);
    }
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ErrorResponse("rate_limited", "Too many requests. Please try again later."),
            cancellationToken);
    };

    options.AddPolicy<string>("register", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetClientAddress(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy<string>("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetClientAddress(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy<string>("ninja-write", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy<string>("score-submit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy<string>("online-read", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(
            new ErrorResponse("server_error", "The server could not complete the request."));
    });
});

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (database.Database.IsRelational())
    {
        await database.Database.MigrateAsync();
    }
    else
    {
        await database.Database.EnsureCreatedAsync();
    }
}

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapGet("/health", async (ApplicationDbContext database, CancellationToken cancellationToken) =>
{
    try
    {
        if (await database.Database.CanConnectAsync(cancellationToken))
        {
            return Results.Ok(new { status = "ok", database = "ok" });
        }

        return Results.Json(
            new ErrorResponse("database_unavailable", "The database is unavailable."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch
    {
        return Results.Json(
            new ErrorResponse("database_unavailable", "The database is unavailable."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();

app.MapAuthEndpoints();
app.MapLeaderboardEndpoints();

app.Run();

static string GetClientAddress(HttpContext httpContext) =>
    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

static string GetRateLimitKey(HttpContext httpContext)
{
    var subject = httpContext.User.FindFirst("sub")?.Value;
    return string.IsNullOrWhiteSpace(subject)
        ? "ip:" + GetClientAddress(httpContext)
        : "user:" + subject;
}

public partial class Program
{
}
