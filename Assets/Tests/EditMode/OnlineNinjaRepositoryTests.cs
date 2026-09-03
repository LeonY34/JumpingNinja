using System.Collections.Generic;
using NUnit.Framework;
using JumpingNinja;

namespace JumpingNinja.Tests
{
    public sealed class OnlineNinjaRepositoryTests
    {
        private const string OnlineCacheKey = "JumpingNinja.OnlineNinjas.v1";
        private const string LegacyCacheKey = "JumpingNinja.Users.v1";

        private bool hadOnlineCache;
        private string onlineCache;
        private bool hadLegacyCache;
        private string legacyCache;

        [SetUp]
        public void SetUp()
        {
            hadOnlineCache = UnityEngine.PlayerPrefs.HasKey(OnlineCacheKey);
            onlineCache = UnityEngine.PlayerPrefs.GetString(OnlineCacheKey, string.Empty);
            hadLegacyCache = UnityEngine.PlayerPrefs.HasKey(LegacyCacheKey);
            legacyCache = UnityEngine.PlayerPrefs.GetString(LegacyCacheKey, string.Empty);
            UnityEngine.PlayerPrefs.DeleteKey(OnlineCacheKey);
            UnityEngine.PlayerPrefs.DeleteKey(LegacyCacheKey);
            UnityEngine.PlayerPrefs.Save();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.PlayerPrefs.DeleteKey(OnlineCacheKey);
            UnityEngine.PlayerPrefs.DeleteKey(LegacyCacheKey);
            if (hadOnlineCache)
            {
                UnityEngine.PlayerPrefs.SetString(OnlineCacheKey, onlineCache);
            }

            if (hadLegacyCache)
            {
                UnityEngine.PlayerPrefs.SetString(LegacyCacheKey, legacyCache);
            }

            UnityEngine.PlayerPrefs.Save();
        }

        [Test]
        public void AccountCachesAreIsolated()
        {
            OnlineNinjaRepository repository = NewRepository();
            repository.SetAccount("account-a");
            repository.ApplyServerSnapshot(Snapshot(
                Ninja("ninja-a", "Red", 12), 12, "ninja-a", "Red"));

            repository.SetAccount("account-b");
            repository.ApplyServerSnapshot(Snapshot(
                Ninja("ninja-b", "Blue", 4), 4, "ninja-b", "Blue"));

            Assert.That(repository.Ninjas, Has.Count.EqualTo(1));
            Assert.That(repository.ActiveNinja.id, Is.EqualTo("ninja-b"));
            Assert.That(repository.AccountBestScore, Is.EqualTo(4));

            repository.SetAccount("account-a");
            Assert.That(repository.Ninjas, Has.Count.EqualTo(1));
            Assert.That(repository.ActiveNinja.id, Is.EqualTo("ninja-a"));
            Assert.That(repository.AccountBestScore, Is.EqualTo(12));
        }

        [Test]
        public void ServerSnapshotMergesWithoutLoweringPendingLocalScore()
        {
            OnlineNinjaRepository repository = NewRepository();
            repository.SetAccount("account-a");
            repository.ApplyServerSnapshot(Snapshot(
                Ninja("ninja-a", "Red", 5), 5, "ninja-a", "Red"));

            OnlineScoreRecord local = repository.RecordLocalScore(10);
            Assert.That(local.ninjaImproved, Is.True);
            Assert.That(repository.GetPendingScore("ninja-a"), Is.EqualTo(10));

            repository.ApplyServerSnapshot(Snapshot(
                Ninja("ninja-a", "Red", 5), 5, "ninja-a", "Red"));

            Assert.That(repository.ActiveNinja.bestScore, Is.EqualTo(10));
            Assert.That(repository.GetPendingScore("ninja-a"), Is.EqualTo(10));
        }

        [Test]
        public void AccountMilestoneRequiresStrictlyHigherScore()
        {
            OnlineNinjaRepository repository = NewRepository();
            repository.SetAccount("account-a");
            repository.ApplyServerSnapshot(Snapshot(
                Ninja("ninja-a", "Red", 10), 10, "ninja-a", "Red"));

            OnlineScoreRecord tie = repository.RecordLocalScore(10);
            Assert.That(tie.ninjaImproved, Is.False);
            Assert.That(tie.accountImproved, Is.False);

            repository.ApplyCreatedNinja(Ninja("ninja-b", "Blue", 0));
            repository.SetActiveNinja("ninja-b");
            OnlineScoreRecord newNinjaTie = repository.RecordLocalScore(10);
            Assert.That(newNinjaTie.ninjaImproved, Is.True);
            Assert.That(newNinjaTie.accountImproved, Is.False);
        }

        [Test]
        public void LegacyProfileIsUnclaimedUntilImported()
        {
            var legacy = new FakeLegacySource(new UserProfile
            {
                id = "legacy-profile",
                name = "Old Ninja",
                bestScore = 7
            });
            OnlineNinjaRepository repository = NewRepository(legacy);
            repository.SetAccount("account-a");
            Assert.That(repository.GetUnclaimedLegacyProfiles(), Has.Count.EqualTo(1));

            repository.ApplyImportedNinja(
                "legacy-profile",
                new NinjaImportResponsePayload
                {
                    ninja = Ninja("cloud-ninja", "Old Ninja", 7),
                    accountBest = new AccountBestPayload
                    {
                        bestScore = 7,
                        bestNinjaId = "cloud-ninja",
                        bestNinjaName = "Old Ninja"
                    }
                });

            Assert.That(repository.GetUnclaimedLegacyProfiles(), Is.Empty);
            Assert.That(repository.ActiveNinja.id, Is.EqualTo("cloud-ninja"));
        }

        [Test]
        public void LegacyProfileIdRulesNormalizeCompactAndHyphenatedGuids()
        {
            System.Guid id = System.Guid.NewGuid();
            string compact = id.ToString("N");
            string hyphenated = id.ToString("D");

            Assert.That(LegacyProfileIdRules.Normalize(compact), Is.EqualTo(hyphenated));
            Assert.That(LegacyProfileIdRules.Normalize(hyphenated.ToUpperInvariant()), Is.EqualTo(hyphenated));
            Assert.That(LegacyProfileIdRules.TryNormalize(compact, out string normalized), Is.True);
            Assert.That(normalized, Is.EqualTo(hyphenated));
        }

        [Test]
        public void LegacyClaimsTreatCompactAndHyphenatedGuidsAsTheSameProfile()
        {
            System.Guid id = System.Guid.NewGuid();
            var legacy = new FakeLegacySource(new UserProfile
            {
                id = id.ToString("N"),
                name = "Old Ninja",
                bestScore = 8
            });
            OnlineNinjaRepository repository = NewRepository(legacy);
            repository.SetAccount("account-a");

            repository.ApplyImportedNinja(
                id.ToString("D"),
                new NinjaImportResponsePayload
                {
                    ninja = Ninja("cloud-ninja", "Old Ninja", 8),
                    accountBest = new AccountBestPayload
                    {
                        bestScore = 8,
                        bestNinjaId = "cloud-ninja",
                        bestNinjaName = "Old Ninja"
                    }
                });

            Assert.That(repository.GetUnclaimedLegacyProfiles(), Is.Empty);
        }

        [Test]
        public void InvalidLegacyProfileIdIsNotMarkedAsMigratedByNormalization()
        {
            Assert.That(LegacyProfileIdRules.Normalize(" broken-id "), Is.EqualTo("broken-id"));
            Assert.That(LegacyProfileIdRules.TryNormalize("broken-id", out _), Is.False);
        }

        private static OnlineNinjaRepository NewRepository(ILegacyNinjaSource legacy = null)
        {
            return new OnlineNinjaRepository(legacy ?? new FakeLegacySource());
        }

        private static NinjaListPayload Snapshot(
            OnlineNinjaPayload ninja,
            int accountScore,
            string bestNinjaId,
            string bestNinjaName)
        {
            return new NinjaListPayload
            {
                ninjas = new[] { ninja },
                maxNinjas = 20,
                accountBest = new AccountBestPayload
                {
                    bestScore = accountScore,
                    bestNinjaId = bestNinjaId,
                    bestNinjaName = bestNinjaName
                }
            };
        }

        private static OnlineNinjaPayload Ninja(string id, string name, int bestScore)
        {
            return new OnlineNinjaPayload
            {
                id = id,
                name = name,
                bestScore = bestScore
            };
        }

        private sealed class FakeLegacySource : ILegacyNinjaSource
        {
            public FakeLegacySource(params UserProfile[] profiles)
            {
                Users = profiles ?? new UserProfile[0];
            }

            public IReadOnlyList<UserProfile> Users { get; }
        }
    }
}
