namespace JumpingNinja.Api.Leaderboard;

public static class LeaderboardRules
{
    public const int MaximumNinjasPerAccount = 20;
    public const int MinimumNinjaNameLength = 1;
    public const int MaximumNinjaNameLength = 16;
    public const int DefaultLeaderboardLimit = 50;
    public const int MaximumLeaderboardLimit = 100;
    public const int DefaultTargetLimit = 20;
    public const int MaximumTargetLimit = 20;

    public static string? NormalizeNinjaName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToUpperInvariant();
    }

    public static string? ValidateNinjaName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return "Ninja name is required.";
        }

        if (trimmed.Length is < MinimumNinjaNameLength or > MaximumNinjaNameLength)
        {
            return $"Ninja name must be {MinimumNinjaNameLength}-{MaximumNinjaNameLength} characters.";
        }

        return trimmed.Any(char.IsControl)
            ? "Ninja name cannot contain control characters."
            : null;
    }

    public static string NormalizeDisplayName(string name) => name.Trim();
}
