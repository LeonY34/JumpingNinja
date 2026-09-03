using System;
using System.Collections.Generic;
using System.Linq;

namespace JumpingNinja
{
    /// <summary>
    /// Normalizes legacy v1 profile identifiers at the client boundary.
    /// Older builds generated compact ("N") GUIDs while newer requests use
    /// the canonical hyphenated ("D") representation. Invalid identifiers
    /// intentionally remain unchanged so the import screen can report them
    /// instead of silently marking the old profile as migrated.
    /// </summary>
    public static class LegacyProfileIdRules
    {
        public static string Normalize(string raw)
        {
            string trimmed = (raw ?? string.Empty).Trim();
            return System.Guid.TryParse(trimmed, out System.Guid parsed)
                ? parsed.ToString("D")
                : trimmed;
        }

        public static bool TryNormalize(string raw, out string normalized)
        {
            normalized = Normalize(raw);
            return System.Guid.TryParse(normalized, out _);
        }
    }

    [Serializable]
    public sealed class NinjaCreatePayload
    {
        public string name;
    }

    [Serializable]
    public sealed class NinjaImportPayload
    {
        public string legacyProfileId;
        public string name;
        public int bestScore;
    }

    [Serializable]
    public sealed class BestScorePayload
    {
        public int bestScore;
    }

    [Serializable]
    public sealed class OnlineNinjaPayload
    {
        public string id;
        public string name;
        public int bestScore;
        public string bestAchievedAt;
    }

    [Serializable]
    public sealed class AccountBestPayload
    {
        public int bestScore;
        public string bestNinjaId;
        public string bestNinjaName;
    }

    [Serializable]
    public sealed class NinjaListPayload
    {
        public OnlineNinjaPayload[] ninjas;
        public int maxNinjas;
        public AccountBestPayload accountBest;
    }

    [Serializable]
    public sealed class NinjaImportResponsePayload
    {
        public OnlineNinjaPayload ninja;
        public bool mergedByName;
        public AccountBestPayload accountBest;
    }

    [Serializable]
    public sealed class ScoreSubmissionResponsePayload
    {
        public OnlineNinjaPayload ninja;
        public AccountBestPayload accountBest;
        public bool ninjaImproved;
        public bool accountImproved;
        public int accountRank;
    }

    [Serializable]
    public sealed class LeaderboardEntryPayload
    {
        public int rank;
        public string username;
        public string ninjaName;
        public int bestScore;
        public bool isCurrentUser;
    }

    [Serializable]
    public sealed class LeaderboardPayload
    {
        public LeaderboardEntryPayload[] entries;
        public LeaderboardEntryPayload currentUser;
        public string generatedAt;
    }

    [Serializable]
    public sealed class LeaderboardTargetPayload
    {
        public int rank;
        public int score;
        public string username;
        public string ninjaName;
        public int accountCount;
    }

    [Serializable]
    public sealed class LeaderboardTargetsPayload
    {
        public LeaderboardTargetPayload[] targets;
        public string generatedAt;
    }

    public sealed class OnlineNinjaProfile
    {
        public string id;
        public string name;
        public int bestScore;
        public string bestAchievedAt;
    }

    public sealed class OnlineScoreRecord
    {
        public string ninjaId;
        public int score;
        public bool ninjaImproved;
        public bool accountImproved;
    }

    [Serializable]
    internal sealed class OnlineNinjaCacheDatabase
    {
        public List<OnlineAccountCache> accounts = new List<OnlineAccountCache>();
        public List<LegacyNinjaClaim> legacyClaims = new List<LegacyNinjaClaim>();
    }

    [Serializable]
    internal sealed class OnlineAccountCache
    {
        public string accountId;
        public List<OnlineNinjaCache> ninjas = new List<OnlineNinjaCache>();
        public List<PendingScoreCache> pendingScores = new List<PendingScoreCache>();
        public string activeNinjaId;
        public bool migrationReviewed;
        public bool hasCloudSnapshot;
        public int cloudAccountBestScore;
        public string cloudBestNinjaId;
        public string cloudBestNinjaName;
    }

    [Serializable]
    internal sealed class OnlineNinjaCache
    {
        public string id;
        public string name;
        public int bestScore;
        public string bestAchievedAt;
    }

    [Serializable]
    internal sealed class PendingScoreCache
    {
        public string ninjaId;
        public int bestScore;
    }

    [Serializable]
    internal sealed class LegacyNinjaClaim
    {
        public string legacyProfileId;
        public string accountId;
        public string ninjaId;
    }

    public sealed class OnlineNinjaRepository
    {
        private const string StorageKey = "JumpingNinja.OnlineNinjas.v1";

        private readonly ILegacyNinjaSource legacyUsers;
        private OnlineNinjaCacheDatabase database;
        private OnlineAccountCache account;
        private string accountId;

        public OnlineNinjaRepository(ILegacyNinjaSource legacyRepository)
        {
            legacyUsers = legacyRepository;
            Load();
        }

        public IReadOnlyList<OnlineNinjaProfile> Ninjas =>
            account == null
                ? Array.Empty<OnlineNinjaProfile>()
                : account.ninjas.Select(ToProfile).ToList();

        public OnlineNinjaProfile ActiveNinja
        {
            get
            {
                if (account == null)
                {
                    return null;
                }

                OnlineNinjaCache active = account.ninjas.FirstOrDefault(
                    ninja => ninja.id == account.activeNinjaId);
                if (active == null && account.ninjas.Count > 0)
                {
                    active = account.ninjas
                        .OrderByDescending(ninja => ninja.bestScore)
                        .ThenBy(ninja => ninja.name, StringComparer.OrdinalIgnoreCase)
                        .First();
                    account.activeNinjaId = active.id;
                    Save();
                }

                return active == null ? null : ToProfile(active);
            }
        }

        public int AccountBestScore
        {
            get
            {
                int localBest = account == null || account.ninjas.Count == 0
                    ? 0
                    : account.ninjas.Max(ninja => ninja.bestScore);
                return Math.Max(localBest, account?.cloudAccountBestScore ?? 0);
            }
        }

        public string AccountBestNinjaName
        {
            get
            {
                if (account == null)
                {
                    return string.Empty;
                }

                OnlineNinjaCache local = account.ninjas
                    .OrderByDescending(ninja => ninja.bestScore)
                    .ThenBy(ninja => ninja.name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                return AccountBestScore > (local?.bestScore ?? 0)
                    ? account.cloudBestNinjaName ?? string.Empty
                    : local?.name ?? account.cloudBestNinjaName ?? string.Empty;
            }
        }

        public string AccountId => accountId;
        public bool HasCloudSnapshot => account != null && account.hasCloudSnapshot;
        public bool MigrationReviewed => account != null && account.migrationReviewed;
        public bool HasCachedNinjas => account != null && account.ninjas.Count > 0;
        public int MaxNinjas => 20;

        public void SetAccount(string onlineAccountId)
        {
            accountId = (onlineAccountId ?? string.Empty).Trim();
            account = database.accounts.FirstOrDefault(
                cached => string.Equals(cached.accountId, accountId, StringComparison.OrdinalIgnoreCase));
            if (account == null)
            {
                account = new OnlineAccountCache { accountId = accountId };
                database.accounts.Add(account);
                Save();
            }
        }

        public void ClearActiveAccount()
        {
            account = null;
            accountId = null;
        }

        public void ApplyServerSnapshot(NinjaListPayload response)
        {
            if (account == null || response == null)
            {
                return;
            }

            OnlineNinjaPayload[] remoteNinjas = response.ninjas ?? Array.Empty<OnlineNinjaPayload>();
            HashSet<string> remoteIds = new HashSet<string>(
                remoteNinjas.Where(ninja => ninja != null && !string.IsNullOrEmpty(ninja.id))
                    .Select(ninja => ninja.id),
                StringComparer.OrdinalIgnoreCase);
            account.ninjas.RemoveAll(ninja =>
                !remoteIds.Contains(ninja.id) &&
                !account.pendingScores.Any(pending =>
                    string.Equals(pending.ninjaId, ninja.id, StringComparison.OrdinalIgnoreCase)));

            foreach (OnlineNinjaPayload remote in remoteNinjas)
            {
                if (remote == null || string.IsNullOrEmpty(remote.id))
                {
                    continue;
                }

                OnlineNinjaCache local = account.ninjas.FirstOrDefault(
                    ninja => string.Equals(ninja.id, remote.id, StringComparison.OrdinalIgnoreCase));
                if (local == null)
                {
                    local = new OnlineNinjaCache { id = remote.id };
                    account.ninjas.Add(local);
                }

                local.name = remote.name ?? string.Empty;
                bool serverScoreApplied = remote.bestScore >= local.bestScore;
                if (serverScoreApplied)
                {
                    local.bestScore = remote.bestScore;
                    local.bestAchievedAt = remote.bestAchievedAt;
                }

                if (serverScoreApplied)
                {
                    RemovePendingIfSynced(local.id, remote.bestScore);
                }
            }

            AccountBestPayload best = response.accountBest;
            if (best != null && best.bestScore >= account.cloudAccountBestScore)
            {
                account.cloudAccountBestScore = best.bestScore;
                account.cloudBestNinjaId = best.bestNinjaId;
                account.cloudBestNinjaName = best.bestNinjaName;
            }
            account.hasCloudSnapshot = true;
            EnsureActiveNinja();
            Save();
        }

        public void ApplyCreatedNinja(OnlineNinjaPayload response)
        {
            if (account == null || response == null || string.IsNullOrEmpty(response.id))
            {
                return;
            }

            UpsertNinja(response);
            EnsureActiveNinja();
            Save();
        }

        public void ApplyImportedNinja(
            string legacyProfileId,
            NinjaImportResponsePayload response)
        {
            if (account == null || response?.ninja == null)
            {
                return;
            }

            UpsertNinja(response.ninja);
            string normalizedLegacyId = LegacyProfileIdRules.Normalize(legacyProfileId);
            LegacyNinjaClaim claim = database.legacyClaims.FirstOrDefault(
                item => string.Equals(
                    LegacyProfileIdRules.Normalize(item.legacyProfileId),
                    normalizedLegacyId,
                    StringComparison.OrdinalIgnoreCase));
            if (claim == null)
            {
                database.legacyClaims.Add(new LegacyNinjaClaim
                {
                    legacyProfileId = normalizedLegacyId,
                    accountId = accountId,
                    ninjaId = response.ninja.id
                });
            }
            else
            {
                claim.accountId = accountId;
                claim.ninjaId = response.ninja.id;
            }

            ApplyAccountBest(response.accountBest);
            EnsureActiveNinja();
            Save();
        }

        public OnlineScoreRecord RecordLocalScore(int score)
        {
            OnlineNinjaCache active = account == null
                ? null
                : account.ninjas.FirstOrDefault(ninja => ninja.id == account.activeNinjaId);
            if (active == null)
            {
                return new OnlineScoreRecord { score = score };
            }

            int previousAccountBest = AccountBestScore;
            bool ninjaImproved = score > active.bestScore;
            if (ninjaImproved)
            {
                active.bestScore = score;
                active.bestAchievedAt = null;
                PendingScoreCache pending = account.pendingScores.FirstOrDefault(
                    item => item.ninjaId == active.id);
                if (pending == null)
                {
                    account.pendingScores.Add(new PendingScoreCache
                    {
                        ninjaId = active.id,
                        bestScore = score
                    });
                }
                else
                {
                    pending.bestScore = Math.Max(pending.bestScore, score);
                }
            }

            // The account milestone is a strict improvement over the best value
            // known before this run. Equal scores keep the existing account rank
            // and should not show a second "new account best" notification.
            bool accountImproved = ninjaImproved && score > previousAccountBest;
            Save();
            return new OnlineScoreRecord
            {
                ninjaId = active.id,
                score = score,
                ninjaImproved = ninjaImproved,
                accountImproved = accountImproved
            };
        }

        public int GetPendingScore(string ninjaId)
        {
            return account?.pendingScores
                .FirstOrDefault(item => item.ninjaId == ninjaId)?.bestScore ?? -1;
        }

        public void ApplyScoreResponse(ScoreSubmissionResponsePayload response)
        {
            if (account == null || response == null)
            {
                return;
            }

            UpsertNinja(response.ninja);
            ApplyAccountBest(response.accountBest);
            if (response.ninja != null)
            {
                RemovePendingIfSynced(response.ninja.id, response.ninja.bestScore);
            }
            Save();
        }

        public void SetActiveNinja(string ninjaId)
        {
            if (account == null || account.ninjas.All(ninja => ninja.id != ninjaId))
            {
                return;
            }

            account.activeNinjaId = ninjaId;
            Save();
        }

        public void MarkMigrationReviewed()
        {
            if (account == null)
            {
                return;
            }

            account.migrationReviewed = true;
            Save();
        }

        public List<UserProfile> GetUnclaimedLegacyProfiles()
        {
            HashSet<string> claimedIds = new HashSet<string>(
                database.legacyClaims
                    .Where(claim => !string.IsNullOrEmpty(claim.legacyProfileId))
                    .Select(claim => LegacyProfileIdRules.Normalize(claim.legacyProfileId)),
                StringComparer.OrdinalIgnoreCase);
            return legacyUsers.Users
                .Where(profile =>
                    !string.IsNullOrEmpty(profile.id) &&
                    !claimedIds.Contains(LegacyProfileIdRules.Normalize(profile.id)))
                .ToList();
        }

        private void UpsertNinja(OnlineNinjaPayload response)
        {
            if (account == null || response == null || string.IsNullOrEmpty(response.id))
            {
                return;
            }

            OnlineNinjaCache local = account.ninjas.FirstOrDefault(
                ninja => string.Equals(ninja.id, response.id, StringComparison.OrdinalIgnoreCase));
            if (local == null)
            {
                local = new OnlineNinjaCache { id = response.id };
                account.ninjas.Add(local);
            }

            local.name = response.name ?? local.name ?? string.Empty;
            if (response.bestScore >= local.bestScore)
            {
                local.bestScore = response.bestScore;
                local.bestAchievedAt = response.bestAchievedAt;
            }
        }

        private void ApplyAccountBest(AccountBestPayload best)
        {
            if (best == null)
            {
                return;
            }

            // Never let a stale response lower the cached aggregate. When the
            // score is equal, the server response is authoritative for the
            // contributing Ninja identity and name.
            if (best.bestScore >= account.cloudAccountBestScore)
            {
                account.cloudAccountBestScore = best.bestScore;
                account.cloudBestNinjaId = best.bestNinjaId;
                account.cloudBestNinjaName = best.bestNinjaName;
            }
        }

        private void RemovePendingIfSynced(string ninjaId, int canonicalScore)
        {
            account.pendingScores.RemoveAll(
                pending => pending.ninjaId == ninjaId && pending.bestScore <= canonicalScore);
        }

        private void EnsureActiveNinja()
        {
            if (account.ninjas.All(ninja => ninja.id != account.activeNinjaId))
            {
                account.activeNinjaId = account.ninjas
                    .OrderByDescending(ninja => ninja.bestScore)
                    .ThenBy(ninja => ninja.name, StringComparer.OrdinalIgnoreCase)
                    .Select(ninja => ninja.id)
                    .FirstOrDefault();
            }
        }

        private static OnlineNinjaProfile ToProfile(OnlineNinjaCache cache) =>
            new OnlineNinjaProfile
            {
                id = cache.id,
                name = cache.name,
                bestScore = cache.bestScore,
                bestAchievedAt = cache.bestAchievedAt
            };

        private void Load()
        {
            string json = UnityEngine.PlayerPrefs.GetString(StorageKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                database = new OnlineNinjaCacheDatabase();
                return;
            }

            try
            {
                database = UnityEngine.JsonUtility.FromJson<OnlineNinjaCacheDatabase>(json)
                    ?? new OnlineNinjaCacheDatabase();
                database.accounts ??= new List<OnlineAccountCache>();
                database.legacyClaims ??= new List<LegacyNinjaClaim>();
                foreach (OnlineAccountCache cached in database.accounts)
                {
                    cached.ninjas ??= new List<OnlineNinjaCache>();
                    cached.pendingScores ??= new List<PendingScoreCache>();
                }
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning(
                    $"Could not read the online Ninja cache: {exception.Message}");
                database = new OnlineNinjaCacheDatabase();
            }
        }

        private void Save()
        {
            UnityEngine.PlayerPrefs.SetString(StorageKey, UnityEngine.JsonUtility.ToJson(database));
            UnityEngine.PlayerPrefs.Save();
        }
    }
}
