using System;
using System.IO;
using UnityEngine;

namespace Vent.Gameplay.Persistence
{
    /// <summary>What persists between sessions. Kept tiny and versioned so it can grow safely.</summary>
    [Serializable]
    public sealed class HighScoreData
    {
        public int Version = 1;
        public int BestLevel;
        public int BestKills;
        public int TotalRuns;
        public int TotalKills;
        public float LongestRunSeconds;
    }

    /// <summary>
    /// JSON file persistence in <see cref="Application.persistentDataPath"/>. Uses JsonUtility
    /// (no third-party serializer) and writes atomically via a temp file + rename so a crash
    /// mid-write never corrupts the save.
    /// </summary>
    public sealed class HighScoreStore
    {
        private const string FileName = "highscores.json";

        private readonly string path;

        public HighScoreData Data { get; private set; } = new();

        public HighScoreStore(string directory = null)
        {
            path = Path.Combine(directory ?? Application.persistentDataPath, FileName);
            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(path))
                {
                    Data = JsonUtility.FromJson<HighScoreData>(File.ReadAllText(path)) ?? new HighScoreData();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"HighScoreStore: could not read {path}: {e.Message}");
                Data = new HighScoreData();
            }
        }

        /// <summary>Fold a finished run into the record. Returns true if the best level improved.</summary>
        public bool Record(int levelReached, int kills, float seconds)
        {
            bool newRecord = levelReached > Data.BestLevel;
            Data.BestLevel = Mathf.Max(Data.BestLevel, levelReached);
            Data.BestKills = Mathf.Max(Data.BestKills, kills);
            Data.LongestRunSeconds = Mathf.Max(Data.LongestRunSeconds, seconds);
            Data.TotalRuns++;
            Data.TotalKills += kills;
            Save();
            return newRecord;
        }

        public void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(Data, prettyPrint: true));
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temp, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"HighScoreStore: could not write {path}: {e.Message}");
            }
        }
    }
}
