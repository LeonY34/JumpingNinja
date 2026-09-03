using System;
using System.Collections.Generic;

namespace JumpingNinja
{
    public interface ILegacyNinjaSource
    {
        IReadOnlyList<UserProfile> Users { get; }
    }

    [Serializable]
    public sealed class UserProfile
    {
        public string id;
        public string name;
        public int bestScore;
    }
}
