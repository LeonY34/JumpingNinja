namespace JumpingNinja.Api.Data;

public sealed class AccountLeaderboardEntry
{
    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;

    public Guid BestNinjaId { get; set; }

    public NinjaProfile BestNinja { get; set; } = null!;

    public int BestScore { get; set; }

    public DateTimeOffset? BestAchievedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
