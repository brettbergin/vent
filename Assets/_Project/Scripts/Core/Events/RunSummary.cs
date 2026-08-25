using UnityEngine;

namespace Vent.Core.Events
{
    /// <summary>End-of-run summary shown on the game-over screen and written to the high-score store.</summary>
    public readonly struct RunSummary
    {
        public readonly int LevelReached;
        public readonly int TotalKills;
        public readonly int Headshots;
        public readonly float DurationSeconds;
        public readonly int BestLevel;
        public readonly bool NewRecord;

        public RunSummary(int levelReached, int totalKills, int headshots, float durationSeconds, int bestLevel, bool newRecord)
        {
            LevelReached = levelReached;
            TotalKills = totalKills;
            Headshots = headshots;
            DurationSeconds = durationSeconds;
            BestLevel = bestLevel;
            NewRecord = newRecord;
        }
    }

}
