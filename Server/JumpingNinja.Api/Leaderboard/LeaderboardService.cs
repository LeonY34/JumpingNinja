using System.Data;
using JumpingNinja.Api.Auth;
using JumpingNinja.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace JumpingNinja.Api.Leaderboard;

public sealed class LeaderboardService
{
    private readonly ApplicationDbContext database;

    public LeaderboardService(ApplicationDbContext database)
    {
        this.database = database;
    }

    public async Task<NinjaListResponse?> ListNinjasAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var ninjas = await database.NinjaProfiles
            .AsNoTracking()
            .Where(ninja => ninja.OwnerUserId == userId)
            .OrderByDescending(ninja => ninja.BestScore)
            .ThenBy(ninja => ninja.Name)
            .ToListAsync(cancellationToken);

        var best = await GetAccountBestAsync(userId, cancellationToken);
        if (best is null && ninjas.Count > 0)
        {
            best = await RebuildAccountBestAsync(userId, cancellationToken);
        }

        return best is null
            ? null
            : new NinjaListResponse(
                ninjas.Select(ToResponse).ToList(),
                LeaderboardRules.MaximumNinjasPerAccount,
                ToAccountBestResponse(best));
    }

    public async Task<LeaderboardCommandResult<NinjaResponse>> CreateNinjaAsync(
        Guid userId,
        string? rawName,
        CancellationToken cancellationToken)
    {
        var validationError = LeaderboardRules.ValidateNinjaName(rawName);
        if (validationError is not null)
        {
            return LeaderboardCommandResult<NinjaResponse>.Failure(
                "ninja_name_invalid",
                validationError,
                StatusCodes.Status400BadRequest,
                "name");
        }

        var name = LeaderboardRules.NormalizeDisplayName(rawName!);
        var normalizedName = LeaderboardRules.NormalizeNinjaName(name)!;
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await LockAccountAsync(userId, cancellationToken))
            {
                return LeaderboardCommandResult<NinjaResponse>.Failure(
                    "unauthorized",
                    "Authentication is required.",
                    StatusCodes.Status401Unauthorized);
            }

            var count = await database.NinjaProfiles
                .CountAsync(ninja => ninja.OwnerUserId == userId, cancellationToken);
            if (count >= LeaderboardRules.MaximumNinjasPerAccount)
            {
                return LeaderboardCommandResult<NinjaResponse>.Failure(
                    "ninja_limit_reached",
                    $"Each account can have at most {LeaderboardRules.MaximumNinjasPerAccount} Ninjas.",
                    StatusCodes.Status409Conflict);
            }

            var duplicate = await database.NinjaProfiles.AnyAsync(
                ninja => ninja.OwnerUserId == userId && ninja.NormalizedName == normalizedName,
                cancellationToken);
            if (duplicate)
            {
                return LeaderboardCommandResult<NinjaResponse>.Failure(
                    "ninja_name_taken",
                    "That Ninja name is already in use.",
                    StatusCodes.Status409Conflict,
                    "name");
            }

            var now = DateTimeOffset.UtcNow;
            var ninjaProfile = new NinjaProfile
            {
                Id = Guid.NewGuid(),
                OwnerUserId = userId,
                Name = name,
                NormalizedName = normalizedName,
                BestScore = 0,
                CreatedAt = now,
                UpdatedAt = now
            };
            database.NinjaProfiles.Add(ninjaProfile);
            await database.SaveChangesAsync(cancellationToken);
            await EnsureAccountBestAsync(userId, ninjaProfile, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return LeaderboardCommandResult<NinjaResponse>.Success(
                ToResponse(ninjaProfile),
                StatusCodes.Status201Created);
        }
        catch (DbUpdateException)
        {
            await RollbackAsync(transaction, cancellationToken);
            return LeaderboardCommandResult<NinjaResponse>.Failure(
                "ninja_name_taken",
                "That Ninja name is already in use.",
                StatusCodes.Status409Conflict,
                "name");
        }
    }

    public async Task<LeaderboardCommandResult<NinjaImportResponse>> ImportNinjaAsync(
        Guid userId,
        Guid legacyProfileId,
        string? rawName,
        int bestScore,
        CancellationToken cancellationToken)
    {
        if (legacyProfileId == Guid.Empty)
        {
            return LeaderboardCommandResult<NinjaImportResponse>.Failure(
                "legacy_profile_invalid",
                "The legacy Ninja identifier is invalid.",
                StatusCodes.Status400BadRequest,
                "legacyProfileId");
        }

        var validationError = LeaderboardRules.ValidateNinjaName(rawName);
        if (validationError is not null)
        {
            return LeaderboardCommandResult<NinjaImportResponse>.Failure(
                "ninja_name_invalid",
                validationError,
                StatusCodes.Status400BadRequest,
                "name");
        }

        if (bestScore < 0)
        {
            return LeaderboardCommandResult<NinjaImportResponse>.Failure(
                "score_invalid",
                "Best score cannot be negative.",
                StatusCodes.Status400BadRequest,
                "bestScore");
        }

        var name = LeaderboardRules.NormalizeDisplayName(rawName!);
        var normalizedName = LeaderboardRules.NormalizeNinjaName(name)!;
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await LockAccountAsync(userId, cancellationToken))
            {
                return LeaderboardCommandResult<NinjaImportResponse>.Failure(
                    "unauthorized",
                    "Authentication is required.",
                    StatusCodes.Status401Unauthorized);
            }

            var existingImport = await database.LegacyNinjaImports
                .Include(import => import.Ninja)
                .SingleOrDefaultAsync(
                    import => import.LegacyProfileId == legacyProfileId,
                    cancellationToken);
            if (existingImport is not null)
            {
                if (existingImport.OwnerUserId != userId)
                {
                    return LeaderboardCommandResult<NinjaImportResponse>.Failure(
                        "legacy_profile_claimed",
                        "That legacy Ninja is already linked to another account.",
                        StatusCodes.Status409Conflict);
                }

                var existingResult = await UpdateBestScoreCoreAsync(
                    userId,
                    existingImport.Ninja,
                    bestScore,
                    cancellationToken);
                await database.SaveChangesAsync(cancellationToken);
                await CommitAsync(transaction, cancellationToken);
                return LeaderboardCommandResult<NinjaImportResponse>.Success(
                    new NinjaImportResponse(
                        ToResponse(existingResult.Ninja),
                        false,
                        ToAccountBestResponse(existingResult.AccountBest)));
            }

            var ninjaProfile = await database.NinjaProfiles
                .SingleOrDefaultAsync(
                    ninja => ninja.OwnerUserId == userId && ninja.NormalizedName == normalizedName,
                    cancellationToken);
            var mergedByName = ninjaProfile is not null;
            if (ninjaProfile is null)
            {
                var count = await database.NinjaProfiles
                    .CountAsync(ninja => ninja.OwnerUserId == userId, cancellationToken);
                if (count >= LeaderboardRules.MaximumNinjasPerAccount)
                {
                    return LeaderboardCommandResult<NinjaImportResponse>.Failure(
                        "ninja_limit_reached",
                        $"Each account can have at most {LeaderboardRules.MaximumNinjasPerAccount} Ninjas.",
                        StatusCodes.Status409Conflict);
                }

                var now = DateTimeOffset.UtcNow;
                ninjaProfile = new NinjaProfile
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = userId,
                    Name = name,
                    NormalizedName = normalizedName,
                    BestScore = 0,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                database.NinjaProfiles.Add(ninjaProfile);
                await database.SaveChangesAsync(cancellationToken);
            }

            database.LegacyNinjaImports.Add(new LegacyNinjaImport
            {
                LegacyProfileId = legacyProfileId,
                NinjaId = ninjaProfile.Id,
                OwnerUserId = userId,
                ImportedAt = DateTimeOffset.UtcNow
            });

            var result = await UpdateBestScoreCoreAsync(
                userId,
                ninjaProfile,
                bestScore,
                cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return LeaderboardCommandResult<NinjaImportResponse>.Success(
                new NinjaImportResponse(
                    ToResponse(result.Ninja),
                    mergedByName,
                    ToAccountBestResponse(result.AccountBest)));
        }
        catch (DbUpdateException)
        {
            await RollbackAsync(transaction, cancellationToken);
            return LeaderboardCommandResult<NinjaImportResponse>.Failure(
                "legacy_profile_claimed",
                "That legacy Ninja was already imported. Refresh the Ninja list and try again.",
                StatusCodes.Status409Conflict);
        }
    }

    public async Task<LeaderboardCommandResult<ScoreSubmissionResponse>> SubmitBestScoreAsync(
        Guid userId,
        Guid ninjaId,
        int bestScore,
        CancellationToken cancellationToken)
    {
        if (bestScore < 0)
        {
            return LeaderboardCommandResult<ScoreSubmissionResponse>.Failure(
                "score_invalid",
                "Best score cannot be negative.",
                StatusCodes.Status400BadRequest,
                "bestScore");
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        if (!await LockAccountAsync(userId, cancellationToken))
        {
            return LeaderboardCommandResult<ScoreSubmissionResponse>.Failure(
                "unauthorized",
                "Authentication is required.",
                StatusCodes.Status401Unauthorized);
        }

        var ninja = await LoadNinjaForUpdateAsync(userId, ninjaId, cancellationToken);
        if (ninja is null)
        {
            return LeaderboardCommandResult<ScoreSubmissionResponse>.Failure(
                "ninja_not_found",
                "The Ninja could not be found.",
                StatusCodes.Status404NotFound);
        }

        var result = await UpdateBestScoreCoreAsync(
            userId,
            ninja,
            bestScore,
            cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        var rank = await GetAccountRankAsync(userId, result.AccountBest.BestScore, cancellationToken);
        return LeaderboardCommandResult<ScoreSubmissionResponse>.Success(
            new ScoreSubmissionResponse(
                ToResponse(result.Ninja),
                ToAccountBestResponse(result.AccountBest),
                result.NinjaImproved,
                result.AccountImproved,
                rank));
    }

    public async Task<LeaderboardResponse> GetLeaderboardAsync(
        Guid userId,
        int limit,
        CancellationToken cancellationToken)
    {
        limit = Math.Clamp(
            limit <= 0 ? LeaderboardRules.DefaultLeaderboardLimit : limit,
            1,
            LeaderboardRules.MaximumLeaderboardLimit);

        var entries = await database.AccountLeaderboardEntries
            .AsNoTracking()
            .Include(entry => entry.User)
            .Include(entry => entry.BestNinja)
            .OrderByDescending(entry => entry.BestScore)
            .ThenBy(entry => entry.BestAchievedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(entry => entry.User.UserName)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var output = entries
            .Select((entry, index) => new LeaderboardEntryResponse(
                GetRankForOrderedIndex(entries, index),
                entry.User.UserName ?? string.Empty,
                entry.BestNinja.Name,
                entry.BestScore,
                entry.UserId == userId))
            .ToList();

        var current = await database.AccountLeaderboardEntries
            .AsNoTracking()
            .Include(entry => entry.User)
            .Include(entry => entry.BestNinja)
            .SingleOrDefaultAsync(entry => entry.UserId == userId, cancellationToken);
        LeaderboardEntryResponse? currentResponse = null;
        if (current is not null)
        {
            var rank = await GetAccountRankAsync(userId, current.BestScore, cancellationToken);
            currentResponse = new LeaderboardEntryResponse(
                rank,
                current.User.UserName ?? string.Empty,
                current.BestNinja.Name,
                current.BestScore,
                true);
        }

        return new LeaderboardResponse(
            output,
            currentResponse,
            DateTimeOffset.UtcNow);
    }

    public async Task<LeaderboardTargetsResponse> GetTargetsAsync(
        Guid userId,
        int fromScore,
        int limit,
        CancellationToken cancellationToken)
    {
        fromScore = Math.Max(0, fromScore);
        limit = Math.Clamp(
            limit <= 0 ? LeaderboardRules.DefaultTargetLimit : limit,
            1,
            LeaderboardRules.MaximumTargetLimit);

        var scores = await database.AccountLeaderboardEntries
            .AsNoTracking()
            .Where(entry => entry.UserId != userId && entry.BestScore >= fromScore)
            .Select(entry => entry.BestScore)
            .Distinct()
            .OrderBy(score => score)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var targets = new List<TargetMilestoneResponse>(scores.Count);
        foreach (var score in scores)
        {
            var representative = await database.AccountLeaderboardEntries
                .AsNoTracking()
                .Include(entry => entry.User)
                .Include(entry => entry.BestNinja)
                .Where(entry => entry.BestScore == score && entry.UserId != userId)
                .OrderBy(entry => entry.BestAchievedAt ?? DateTimeOffset.MaxValue)
                .ThenBy(entry => entry.User.UserName)
                .FirstAsync(cancellationToken);
            var accountCount = await database.AccountLeaderboardEntries
                .CountAsync(entry => entry.BestScore == score && entry.UserId != userId, cancellationToken);
            var rank = await GetAccountRankAsync(representative.UserId, score, cancellationToken);
            targets.Add(new TargetMilestoneResponse(
                rank,
                score,
                representative.User.UserName ?? string.Empty,
                representative.BestNinja.Name,
                accountCount));
        }

        return new LeaderboardTargetsResponse(targets, DateTimeOffset.UtcNow);
    }

    private async Task<(NinjaProfile Ninja, AccountLeaderboardEntry AccountBest, bool NinjaImproved, bool AccountImproved)>
        UpdateBestScoreCoreAsync(
            Guid userId,
            NinjaProfile ninja,
            int candidateScore,
            CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var ninjaImproved = candidateScore > ninja.BestScore;
        if (ninjaImproved)
        {
            ninja.BestScore = candidateScore;
            ninja.BestAchievedAt = now;
            ninja.UpdatedAt = now;
        }

        var accountBest = await database.AccountLeaderboardEntries
            .Include(entry => entry.BestNinja)
            .SingleOrDefaultAsync(entry => entry.UserId == userId, cancellationToken);
        if (accountBest is null)
        {
            accountBest = new AccountLeaderboardEntry
            {
                UserId = userId,
                BestNinjaId = ninja.Id,
                BestNinja = ninja,
                BestScore = ninja.BestScore,
                BestAchievedAt = ninja.BestAchievedAt,
                UpdatedAt = now
            };
            database.AccountLeaderboardEntries.Add(accountBest);
            return (ninja, accountBest, ninjaImproved, ninjaImproved);
        }

        var accountImproved = ninja.BestScore > accountBest.BestScore;
        if (accountImproved ||
            (ninja.Id == accountBest.BestNinjaId && ninja.BestAchievedAt != accountBest.BestAchievedAt))
        {
            accountBest.BestNinjaId = ninja.Id;
            accountBest.BestNinja = ninja;
            accountBest.BestScore = ninja.BestScore;
            accountBest.BestAchievedAt = ninja.BestAchievedAt;
            accountBest.UpdatedAt = now;
        }

        return (ninja, accountBest, ninjaImproved, accountImproved);
    }

    private async Task<AccountLeaderboardEntry?> GetAccountBestAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await database.AccountLeaderboardEntries
            .AsNoTracking()
            .Include(entry => entry.BestNinja)
            .SingleOrDefaultAsync(entry => entry.UserId == userId, cancellationToken);

    private async Task<AccountLeaderboardEntry> EnsureAccountBestAsync(
        Guid userId,
        NinjaProfile ninja,
        CancellationToken cancellationToken)
    {
        var existing = await database.AccountLeaderboardEntries
            .SingleOrDefaultAsync(entry => entry.UserId == userId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var entry = new AccountLeaderboardEntry
        {
            UserId = userId,
            BestNinjaId = ninja.Id,
            BestNinja = ninja,
            BestScore = ninja.BestScore,
            BestAchievedAt = ninja.BestAchievedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        database.AccountLeaderboardEntries.Add(entry);
        return entry;
    }

    private async Task<AccountLeaderboardEntry> RebuildAccountBestAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var bestNinja = await database.NinjaProfiles
            .AsNoTracking()
            .Where(ninja => ninja.OwnerUserId == userId)
            .OrderByDescending(ninja => ninja.BestScore)
            .ThenBy(ninja => ninja.BestAchievedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(ninja => ninja.Name)
            .FirstAsync(cancellationToken);
        var entry = new AccountLeaderboardEntry
        {
            UserId = userId,
            BestNinjaId = bestNinja.Id,
            BestScore = bestNinja.BestScore,
            BestAchievedAt = bestNinja.BestAchievedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        database.Attach(bestNinja).State = EntityState.Unchanged;
        entry.BestNinja = bestNinja;
        database.AccountLeaderboardEntries.Add(entry);
        await database.SaveChangesAsync(cancellationToken);
        return entry;
    }

    private async Task<NinjaProfile?> LoadNinjaForUpdateAsync(
        Guid userId,
        Guid ninjaId,
        CancellationToken cancellationToken)
    {
        if (!database.Database.IsRelational())
        {
            return await database.NinjaProfiles
                .SingleOrDefaultAsync(
                    ninja => ninja.Id == ninjaId && ninja.OwnerUserId == userId,
                    cancellationToken);
        }

        return await database.NinjaProfiles
            .FromSqlInterpolated($"SELECT * FROM \"NinjaProfiles\" WHERE \"Id\" = {ninjaId} AND \"OwnerUserId\" = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<bool> LockAccountAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!database.Database.IsRelational())
        {
            return await database.Users.AnyAsync(user => user.Id == userId, cancellationToken);
        }

        return await database.Users
            .FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE")
            .AnyAsync(cancellationToken);
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken)
    {
        return database.Database.IsRelational()
            ? await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
    }

    private static async Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task RollbackAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private async Task<int> GetAccountRankAsync(
        Guid userId,
        int score,
        CancellationToken cancellationToken) =>
        1 + await database.AccountLeaderboardEntries
            .AsNoTracking()
            .CountAsync(entry => entry.BestScore > score, cancellationToken);

    private static int GetRankForOrderedIndex(
        IReadOnlyList<AccountLeaderboardEntry> entries,
        int index)
    {
        if (index == 0 || entries[index].BestScore != entries[index - 1].BestScore)
        {
            return index + 1;
        }

        return GetRankForOrderedIndex(entries, index - 1);
    }

    private static NinjaResponse ToResponse(NinjaProfile ninja) =>
        new(ninja.Id, ninja.Name, ninja.BestScore, ninja.BestAchievedAt);

    private static AccountBestResponse ToAccountBestResponse(AccountLeaderboardEntry entry) =>
        new(entry.BestScore, entry.BestNinjaId, entry.BestNinja.Name);
}
