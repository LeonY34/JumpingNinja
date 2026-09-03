using JumpingNinja.Api.Auth;

namespace JumpingNinja.Api.Leaderboard;

public sealed class CreateNinjaRequest
{
    public string? Name { get; set; }
}

public sealed class ImportNinjaRequest
{
    // Keep this as text at the HTTP boundary so older clients that send a
    // compact ("N") GUID are accepted alongside the normal ("D") format.
    // The endpoint validates and converts it before entering the service.
    public string? LegacyProfileId { get; set; }
    public string? Name { get; set; }
    public int BestScore { get; set; }
}

public sealed class SubmitBestScoreRequest
{
    public int BestScore { get; set; }
}

public sealed record NinjaResponse(
    Guid Id,
    string Name,
    int BestScore,
    DateTimeOffset? BestAchievedAt);

public sealed record AccountBestResponse(
    int BestScore,
    Guid BestNinjaId,
    string BestNinjaName);

public sealed record NinjaListResponse(
    IReadOnlyList<NinjaResponse> Ninjas,
    int MaxNinjas,
    AccountBestResponse AccountBest);

public sealed record NinjaImportResponse(
    NinjaResponse Ninja,
    bool MergedByName,
    AccountBestResponse AccountBest);

public sealed record ScoreSubmissionResponse(
    NinjaResponse Ninja,
    AccountBestResponse AccountBest,
    bool NinjaImproved,
    bool AccountImproved,
    int AccountRank);

public sealed record LeaderboardEntryResponse(
    int Rank,
    string Username,
    string NinjaName,
    int BestScore,
    bool IsCurrentUser);

public sealed record LeaderboardResponse(
    IReadOnlyList<LeaderboardEntryResponse> Entries,
    LeaderboardEntryResponse? CurrentUser,
    DateTimeOffset GeneratedAt);

public sealed record TargetMilestoneResponse(
    int Rank,
    int Score,
    string Username,
    string NinjaName,
    int AccountCount);

public sealed record LeaderboardTargetsResponse(
    IReadOnlyList<TargetMilestoneResponse> Targets,
    DateTimeOffset GeneratedAt);

public sealed record LeaderboardCommandResult<T>(
    T? Value,
    ErrorResponse? Error,
    int StatusCode)
{
    public bool Succeeded => Error is null;

    public static LeaderboardCommandResult<T> Success(T value, int statusCode = 200) =>
        new(value, null, statusCode);

    public static LeaderboardCommandResult<T> Failure(
        string code,
        string message,
        int statusCode,
        string? field = null) =>
        new(default, new ErrorResponse(code, message, field), statusCode);
}
