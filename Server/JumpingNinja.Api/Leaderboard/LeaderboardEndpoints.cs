using System.Security.Claims;
using JumpingNinja.Api.Auth;
using JumpingNinja.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

namespace JumpingNinja.Api.Leaderboard;

public static class LeaderboardEndpoints
{
    public static IEndpointRouteBuilder MapLeaderboardEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var authenticated = endpoints.MapGroup("/api/v1")
            .RequireAuthorization(new AuthorizeAttribute());

        authenticated.MapGet("/ninjas", ListNinjasAsync)
            .RequireRateLimiting("online-read");
        authenticated.MapPost("/ninjas", CreateNinjaAsync)
            .RequireRateLimiting("ninja-write");
        authenticated.MapPost("/ninjas/import", ImportNinjaAsync)
            .RequireRateLimiting("ninja-write");
        authenticated.MapPut("/ninjas/{ninjaId:guid}/best-score", SubmitBestScoreAsync)
            .RequireRateLimiting("score-submit");
        authenticated.MapGet("/leaderboard", GetLeaderboardAsync)
            .RequireRateLimiting("online-read");
        authenticated.MapGet("/leaderboard/targets", GetTargetsAsync)
            .RequireRateLimiting("online-read");

        return endpoints;
    }

    private static async Task<IResult> ListNinjasAsync(
        ClaimsPrincipal principal,
        ApplicationDbContext database,
        LeaderboardService service,
        CancellationToken cancellationToken)
    {
        var userId = await ResolveUserIdAsync(principal, database, cancellationToken);
        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var response = await service.ListNinjasAsync(userId.Value, cancellationToken);
        return response is null
            ? Results.Ok(new NinjaListResponse(
                Array.Empty<NinjaResponse>(),
                LeaderboardRules.MaximumNinjasPerAccount,
                new AccountBestResponse(0, Guid.Empty, string.Empty)))
            : Results.Ok(response);
    }

    private static async Task<IResult> CreateNinjaAsync(
        ClaimsPrincipal principal,
        CreateNinjaRequest? input,
        ApplicationDbContext database,
        LeaderboardService service,
        CancellationToken cancellationToken)
    {
        var userId = await ResolveUserIdAsync(principal, database, cancellationToken);
        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var result = await service.CreateNinjaAsync(userId.Value, input?.Name, cancellationToken);
        return result.Succeeded
            ? Results.Created($"/api/v1/ninjas/{result.Value!.Id}", result.Value)
            : Results.Json(result.Error, statusCode: result.StatusCode);
    }

    private static async Task<IResult> ImportNinjaAsync(
        ClaimsPrincipal principal,
        ImportNinjaRequest? input,
        ApplicationDbContext database,
        LeaderboardService service,
        CancellationToken cancellationToken)
    {
        var userId = await ResolveUserIdAsync(principal, database, cancellationToken);
        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var rawLegacyProfileId = input?.LegacyProfileId?.Trim();
        var legacyProfileId = Guid.TryParse(rawLegacyProfileId, out var parsedLegacyProfileId)
            ? parsedLegacyProfileId
            : Guid.Empty;
        var result = await service.ImportNinjaAsync(
            userId.Value,
            legacyProfileId,
            input?.Name,
            input?.BestScore ?? 0,
            cancellationToken);
        return result.Succeeded
            ? Results.Ok(result.Value)
            : Results.Json(result.Error, statusCode: result.StatusCode);
    }

    private static async Task<IResult> SubmitBestScoreAsync(
        ClaimsPrincipal principal,
        Guid ninjaId,
        SubmitBestScoreRequest? input,
        ApplicationDbContext database,
        LeaderboardService service,
        CancellationToken cancellationToken)
    {
        var userId = await ResolveUserIdAsync(principal, database, cancellationToken);
        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var result = await service.SubmitBestScoreAsync(
            userId.Value,
            ninjaId,
            input?.BestScore ?? -1,
            cancellationToken);
        return result.Succeeded
            ? Results.Ok(result.Value)
            : Results.Json(result.Error, statusCode: result.StatusCode);
    }

    private static async Task<IResult> GetLeaderboardAsync(
        ClaimsPrincipal principal,
        int? limit,
        ApplicationDbContext database,
        LeaderboardService service,
        CancellationToken cancellationToken)
    {
        var userId = await ResolveUserIdAsync(principal, database, cancellationToken);
        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        return Results.Ok(await service.GetLeaderboardAsync(
            userId.Value,
            limit ?? LeaderboardRules.DefaultLeaderboardLimit,
            cancellationToken));
    }

    private static async Task<IResult> GetTargetsAsync(
        ClaimsPrincipal principal,
        int? fromScore,
        int? limit,
        ApplicationDbContext database,
        LeaderboardService service,
        CancellationToken cancellationToken)
    {
        var userId = await ResolveUserIdAsync(principal, database, cancellationToken);
        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        return Results.Ok(await service.GetTargetsAsync(
            userId.Value,
            fromScore ?? 0,
            limit ?? LeaderboardRules.DefaultTargetLimit,
            cancellationToken));
    }

    private static async Task<Guid?> ResolveUserIdAsync(
        ClaimsPrincipal principal,
        ApplicationDbContext database,
        CancellationToken cancellationToken)
    {
        var claim = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(claim, out var userId) ||
            !await database.Users.AnyAsync(user => user.Id == userId, cancellationToken))
        {
            return null;
        }

        return userId;
    }

    private static IResult UnauthorizedResponse() =>
        Results.Json(
            new ErrorResponse("unauthorized", "Authentication is required."),
            statusCode: StatusCodes.Status401Unauthorized);
}
