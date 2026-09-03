using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace JumpingNinja.Api.Data;

public sealed class ApplicationDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<NinjaProfile> NinjaProfiles => Set<NinjaProfile>();

    public DbSet<AccountLeaderboardEntry> AccountLeaderboardEntries =>
        Set<AccountLeaderboardEntry>();

    public DbSet<LegacyNinjaImport> LegacyNinjaImports => Set<LegacyNinjaImport>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<NinjaProfile>(entity =>
        {
            entity.ToTable("NinjaProfiles", table => table.HasCheckConstraint(
                "CK_NinjaProfiles_BestScore_NonNegative",
                "\"BestScore\" >= 0"));
            entity.HasKey(profile => profile.Id);
            entity.Property(profile => profile.Name).IsRequired();
            entity.Property(profile => profile.NormalizedName).IsRequired();
            entity.HasIndex(profile => new { profile.OwnerUserId, profile.NormalizedName })
                .IsUnique();
            entity.HasIndex(profile => profile.OwnerUserId);
            entity.HasOne(profile => profile.OwnerUser)
                .WithMany()
                .HasForeignKey(profile => profile.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AccountLeaderboardEntry>(entity =>
        {
            entity.ToTable("AccountLeaderboardEntries", table => table.HasCheckConstraint(
                "CK_AccountLeaderboardEntries_BestScore_NonNegative",
                "\"BestScore\" >= 0"));
            entity.HasKey(entry => entry.UserId);
            entity.HasOne(entry => entry.User)
                .WithOne()
                .HasForeignKey<AccountLeaderboardEntry>(entry => entry.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(entry => entry.BestNinja)
                .WithOne(ninja => ninja.AccountLeaderboardEntry)
                .HasForeignKey<AccountLeaderboardEntry>(entry => entry.BestNinjaId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(entry => new
            {
                entry.BestScore,
                entry.BestAchievedAt,
                entry.UserId
            });
        });

        builder.Entity<LegacyNinjaImport>(entity =>
        {
            entity.ToTable("LegacyNinjaImports");
            entity.HasKey(import => import.LegacyProfileId);
            entity.HasOne(import => import.Ninja)
                .WithMany(ninja => ninja.LegacyImports)
                .HasForeignKey(import => import.NinjaId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(import => import.OwnerUser)
                .WithMany()
                .HasForeignKey(import => import.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(import => new { import.OwnerUserId, import.NinjaId })
                .IsUnique();
        });
    }
}
