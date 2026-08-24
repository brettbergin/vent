using System;

namespace Vent.Gameplay.Levels
{
    /// <summary>
    /// The level-advancement rule, isolated from Unity so it can be unit tested:
    /// count kills; when the count reaches N(level), advance and start counting again.
    /// Levels are unbounded.
    /// </summary>
    public sealed class LevelRules
    {
        private readonly Func<int, int> killsRequiredForLevel;

        public int Level { get; private set; } = 1;
        public int KillsThisLevel { get; private set; }
        public int TotalKills { get; private set; }
        public int KillsRequired => killsRequiredForLevel(Level);
        public int KillsRemaining => Math.Max(0, KillsRequired - KillsThisLevel);

        /// <param name="killsRequiredForLevel">N(level): kills needed to leave the given level.</param>
        public LevelRules(Func<int, int> killsRequiredForLevel)
        {
            this.killsRequiredForLevel = killsRequiredForLevel ?? throw new ArgumentNullException(nameof(killsRequiredForLevel));
        }

        /// <summary>Record one kill. Returns true if this kill advanced the level.</summary>
        public bool RegisterKill()
        {
            TotalKills++;
            KillsThisLevel++;
            if (KillsThisLevel < KillsRequired)
            {
                return false;
            }

            // Surplus never carries over: each level starts from zero, so N(level) is exact.
            Level++;
            KillsThisLevel = 0;
            return true;
        }

        public void Reset()
        {
            Level = 1;
            KillsThisLevel = 0;
            TotalKills = 0;
        }
    }
}
