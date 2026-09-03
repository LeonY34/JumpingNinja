using System.ComponentModel.DataAnnotations;

namespace JumpingNinja.Api.Data;

public sealed class NinjaProfile
{
    public Guid Id { get; set; }

    public Guid OwnerUserId { get; set; }

    public ApplicationUser OwnerUser { get; set; } = null!;

    [MaxLength(16)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(16)]
    public string NormalizedName { get; set; } = string.Empty;

    public int BestScore { get; set; }

    public DateTimeOffset? BestAchievedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<LegacyNinjaImport> LegacyImports { get; set; } =
        new List<LegacyNinjaImport>();

    public AccountLeaderboardEntry? AccountLeaderboardEntry { get; set; }
}
