namespace JumpingNinja.Api.Data;

public sealed class LegacyNinjaImport
{
    public Guid LegacyProfileId { get; set; }

    public Guid NinjaId { get; set; }

    public NinjaProfile Ninja { get; set; } = null!;

    public Guid OwnerUserId { get; set; }

    public ApplicationUser OwnerUser { get; set; } = null!;

    public DateTimeOffset ImportedAt { get; set; }
}
