using System.Text.RegularExpressions;

namespace JumpingNinja.Api.Auth;

public static partial class AuthRules
{
    public const int MinimumUsernameLength = 3;
    public const int MaximumUsernameLength = 16;
    public const int MinimumPasswordLength = 8;
    public const int MaximumPasswordLength = 72;

    public static string? NormalizeUsername(string? username)
    {
        var trimmed = username?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToLowerInvariant();
    }

    public static string? ValidateUsername(string? username)
    {
        var normalized = NormalizeUsername(username);
        if (normalized is null)
        {
            return "Username is required.";
        }

        if (normalized.Length is < MinimumUsernameLength or > MaximumUsernameLength)
        {
            return $"Username must be {MinimumUsernameLength}-{MaximumUsernameLength} characters.";
        }

        return UsernamePattern().IsMatch(normalized)
            ? null
            : "Username may contain only ASCII letters, numbers, and underscores.";
    }

    public static string? ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return "Password is required.";
        }

        if (password.Length is < MinimumPasswordLength or > MaximumPasswordLength)
        {
            return $"Password must be {MinimumPasswordLength}-{MaximumPasswordLength} characters.";
        }

        if (!password.Any(char.IsLetter))
        {
            return "Password must contain at least one letter.";
        }

        return password.Any(char.IsDigit)
            ? null
            : "Password must contain at least one number.";
    }

    [GeneratedRegex("^[a-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}
