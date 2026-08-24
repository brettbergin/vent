using UnityEngine;
using Vent.Core.Audio;
using Vent.Core.Data;
using Vent.Core.Events;
using Vent.Core.Services;
using Vent.Enemies.Spawning;

namespace Vent.Gameplay.Levels
{
    /// <summary>
    /// Owns the run: starts spawning, counts kills, advances levels, and reports the final tally.
    /// Lives in the Building scene; the persistent <see cref="Flow.GameManager"/> finds it through
    /// <see cref="GameServices"/> once the scene is loaded.
    /// </summary>
    public sealed class LevelDirector : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private DifficultyProfile difficulty;

        [Header("Scene")]
        [SerializeField] private ZombieSpawner spawner;

        [Header("Events in")]
        [SerializeField] private KillEventChannel kills;

        [Header("Events out")]
        [SerializeField] private LevelEventChannel levelChanged;
        [SerializeField] private IntEventChannel killsThisLevelChanged;

        private LevelRules rules;
        private float runStartTime;
        private int headshots;
        private bool running;

        public bool IsRunning => running;
        public int Level => rules?.Level ?? 1;
        public int KillsThisLevel => rules?.KillsThisLevel ?? 0;
        public int KillsRequired => rules?.KillsRequired ?? 0;
        public int TotalKills => rules?.TotalKills ?? 0;
        public int Headshots => headshots;
        public float ElapsedSeconds => running ? Time.time - runStartTime : 0f;

        public void Configure(DifficultyProfile profile, ZombieSpawner zombieSpawner, KillEventChannel killChannel,
            LevelEventChannel levelEvent, IntEventChannel killsEvent)
        {
            difficulty = profile;
            spawner = zombieSpawner;
            kills = killChannel;
            levelChanged = levelEvent;
            killsThisLevelChanged = killsEvent;
        }

        private void Awake()
        {
            rules = new LevelRules(level => difficulty != null ? difficulty.KillsRequired(level) : 10);
        }

        private void OnEnable()
        {
            GameServices.Register(this);
            kills?.Subscribe(OnKill);
        }

        private void OnDisable()
        {
            kills?.Unsubscribe(OnKill);
            GameServices.Unregister(this);
        }

        /// <summary>Level 1, zero kills, spawner on.</summary>
        public void StartRun()
        {
            rules.Reset();
            headshots = 0;
            runStartTime = Time.time;
            running = true;

            levelChanged?.Raise(new LevelInfo(rules.Level, rules.KillsRequired));
            killsThisLevelChanged?.Raise(0);
            spawner?.StartSpawning();
        }

        /// <summary>Stop spawning; zombies already out stay until cleared by <see cref="ClearBuilding"/>.</summary>
        public void EndRun()
        {
            running = false;
            spawner?.StopSpawning();
        }

        public void ClearBuilding() => spawner?.DespawnAll();

        private void OnKill(KillInfo info)
        {
            if (!running)
            {
                return;
            }

            if (info.Headshot)
            {
                headshots++;
            }

            bool advanced = rules.RegisterKill();
            killsThisLevelChanged?.Raise(rules.KillsThisLevel);

            if (advanced)
            {
                levelChanged?.Raise(new LevelInfo(rules.Level, rules.KillsRequired));
                SfxPlayer.TryPlay2D(SoundId.LevelUp, 0.8f);
            }
        }
    }
}
