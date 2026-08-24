using System;

namespace Vent.Weapons.Progression
{
    /// <summary>
    /// Tracks experience and level for one weapon. Plain C# with no Unity dependencies:
    /// the entire "kill zombies → gun levels up" rule set lives here and is covered by
    /// edit-mode tests.
    ///
    /// Experience carries over across level-ups (a big kill can grant several levels), and
    /// experience is discarded once the cap is reached.
    /// </summary>
    public sealed class WeaponProgression
    {
        private readonly IWeaponLevelTable table;

        /// <summary>Raised once per level gained, with the new level.</summary>
        public event Action<int> LevelUp;

        public int Level { get; private set; } = 1;

        /// <summary>Experience accumulated toward the next level (resets on level-up).</summary>
        public int Experience { get; private set; }

        public int TotalExperience { get; private set; }

        public int MaxLevel => table.MaxLevel;
        public bool IsMaxLevel => Level >= table.MaxLevel;

        /// <summary>Experience needed to reach the next level, or 0 at cap.</summary>
        public int ExperienceToNext => IsMaxLevel ? 0 : table.ExperienceToNext(Level);

        /// <summary>0..1 fill toward the next level; 1 at cap.</summary>
        public float Progress01 => IsMaxLevel ? 1f : (float)Experience / ExperienceToNext;

        public WeaponProgression(IWeaponLevelTable table)
        {
            this.table = table ?? throw new ArgumentNullException(nameof(table));
        }

        /// <summary>Add experience and return how many levels were gained.</summary>
        public int AddExperience(int amount)
        {
            if (amount <= 0 || IsMaxLevel)
            {
                return 0;
            }

            TotalExperience += amount;
            Experience += amount;

            int gained = 0;
            while (!IsMaxLevel && Experience >= table.ExperienceToNext(Level))
            {
                Experience -= table.ExperienceToNext(Level);
                Level++;
                gained++;
                LevelUp?.Invoke(Level);
            }

            if (IsMaxLevel)
            {
                Experience = 0;
            }

            return gained;
        }

        /// <summary>Back to level 1 with no experience (new run).</summary>
        public void Reset()
        {
            Level = 1;
            Experience = 0;
            TotalExperience = 0;
        }
    }
}
