using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace JumpingNinja
{
    [Serializable]
    internal sealed class UserDatabase
    {
        public List<UserProfile> users = new List<UserProfile>();
        public string activeUserId;
    }

    public sealed class UserRepository : ILegacyNinjaSource
    {
        private const string StorageKey = "JumpingNinja.Users.v1";
        private UserDatabase database;

        public UserRepository()
        {
            Load();
        }

        public IReadOnlyList<UserProfile> Users => database.users;

        public UserProfile ActiveUser
        {
            get
            {
                UserProfile active = database.users.FirstOrDefault(user => user.id == database.activeUserId);
                if (active == null && database.users.Count > 0)
                {
                    active = database.users[0];
                    database.activeUserId = active.id;
                    Save();
                }

                return active;
            }
        }

        public bool TryCreateUser(string rawName, out string error)
        {
            string name = (rawName ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                error = "Enter a ninja name.";
                return false;
            }

            if (name.Length > 16)
            {
                error = "Use 16 characters or fewer.";
                return false;
            }

            if (database.users.Any(user => string.Equals(user.name, name, StringComparison.OrdinalIgnoreCase)))
            {
                error = "That name already exists.";
                return false;
            }

            UserProfile profile = new UserProfile
            {
                id = Guid.NewGuid().ToString("N"),
                name = name,
                bestScore = 0
            };

            database.users.Add(profile);
            database.activeUserId = profile.id;
            Save();
            error = string.Empty;
            return true;
        }

        public void SetActiveUser(string id)
        {
            if (database.users.Any(user => user.id == id))
            {
                database.activeUserId = id;
                Save();
            }
        }

        public bool RecordScore(int score)
        {
            UserProfile active = ActiveUser;
            if (active == null || score <= active.bestScore)
            {
                return false;
            }

            active.bestScore = score;
            Save();
            return true;
        }

        public List<UserProfile> GetLeaderboard()
        {
            return database.users
                .OrderByDescending(user => user.bestScore)
                .ThenBy(user => user.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public UserProfile GetNextTarget(int score)
        {
            return database.users
                .Where(user => user.bestScore >= score)
                .OrderBy(user => user.bestScore)
                .ThenBy(user => user.name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private void Load()
        {
            string json = PlayerPrefs.GetString(StorageKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                database = new UserDatabase();
                return;
            }

            try
            {
                database = JsonUtility.FromJson<UserDatabase>(json) ?? new UserDatabase();
                database.users ??= new List<UserProfile>();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not read the local Jumping Ninja user table: {exception.Message}");
                database = new UserDatabase();
            }
        }

        private void Save()
        {
            PlayerPrefs.SetString(StorageKey, JsonUtility.ToJson(database));
            PlayerPrefs.Save();
        }
    }
}
