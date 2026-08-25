using UnityEngine;

namespace Vent.Core.Data
{
    /// <summary>Resolved difficulty numbers for a given level. Produced by <see cref="DifficultyProfile.Evaluate"/>.</summary>
    public readonly struct DifficultySnapshot
    {
        public readonly int Level;
        public readonly int KillsRequired;
        public readonly float HealthMultiplier;
        public readonly float DamageMultiplier;
        public readonly float SpeedMultiplier;
        public readonly float SpawnInterval;
        public readonly int MaxConcurrent;
        public readonly float ExperienceMultiplier;
        /// <summary>Seconds after this level begins before the spawner resumes.</summary>
        public readonly float LevelStartGrace;
        /// <summary>0 = annoyed (must notice you), 1 = enraged (always knows where you are, strikes fast).</summary>
        public readonly float Aggression;

        public DifficultySnapshot(int level, int killsRequired, float healthMultiplier, float damageMultiplier,
            float speedMultiplier, float spawnInterval, int maxConcurrent, float experienceMultiplier, float levelStartGrace = 0f,
            float aggression = 1f)
        {
            Level = level;
            KillsRequired = killsRequired;
            HealthMultiplier = healthMultiplier;
            DamageMultiplier = damageMultiplier;
            SpeedMultiplier = speedMultiplier;
            SpawnInterval = spawnInterval;
            MaxConcurrent = maxConcurrent;
            ExperienceMultiplier = experienceMultiplier;
            LevelStartGrace = levelStartGrace;
            Aggression = Mathf.Clamp01(aggression);
        }
    }

    /// <summary>
    /// The one place where "the game gets harder" is defined. Every value is a curve over the
    /// level number so designers tune difficulty without touching code, and tests can assert
    /// properties such as monotonic growth.
    ///
    /// The environment, the zombies and the guns never change between levels; only these
    /// numbers do. That is the entire progression model of the game. Aggression is a number too:
    /// it is what the zombie's awareness, tracking and strike timing are interpolated from.
    /// </summary>
    [CreateAssetMenu(menuName = "Vent/Data/Difficulty Profile", fileName = "DifficultyProfile")]
    public sealed class DifficultyProfile : ScriptableObject
    {
        /// <summary>Highest level the default curves are sampled to; values clamp beyond it.</summary>
        public const int CurveMaxLevel = 100;

        [Header("Level Progression")]
        [SerializeField, Tooltip("X = level, Y = kills required to advance to the next level.")]
        private AnimationCurve killsToAdvance;

        [Header("Zombie Scaling (multipliers over the zombie definition's base stats)")]
        [SerializeField] private AnimationCurve zombieHealthMultiplier;
        [SerializeField] private AnimationCurve zombieDamageMultiplier;
        [SerializeField] private AnimationCurve zombieSpeedMultiplier;

        [Header("Spawning")]
        [SerializeField, Tooltip("Seconds between spawns while below the concurrent cap.")]
        private AnimationCurve spawnInterval;
        [SerializeField, Tooltip("Maximum zombies alive at once.")]
        private AnimationCurve maxConcurrent;

        [Header("Behaviour")]
        [SerializeField, Tooltip("X = level, Y = aggression 0..1. Low: zombies shamble near their vent until they see, hear or feel you. High: they always know where you are, track tighter and strike faster.")]
        private AnimationCurve aggression;

        [Header("Grace Periods")]
        [SerializeField, Min(0f), Tooltip("Seconds after a run starts before the first zombie is sent.")]
        private float runStartGrace = 8f;
        [SerializeField, Tooltip("X = level, Y = seconds after reaching that level before spawning resumes.")]
        private AnimationCurve levelStartGrace;

        [Header("Rewards")]
        [SerializeField, Tooltip("Multiplier applied to each kill's weapon experience.")]
        private AnimationCurve experienceMultiplier;

        public int KillsRequired(int level) => Mathf.Max(1, Mathf.RoundToInt(CurveUtil.EvaluateLevel(killsToAdvance, level, 10f)));
        public float HealthMultiplier(int level) => CurveUtil.EvaluateLevel(zombieHealthMultiplier, level);
        public float DamageMultiplier(int level) => CurveUtil.EvaluateLevel(zombieDamageMultiplier, level);
        public float SpeedMultiplier(int level) => CurveUtil.EvaluateLevel(zombieSpeedMultiplier, level);
        public float SpawnInterval(int level) => Mathf.Max(0.1f, CurveUtil.EvaluateLevel(spawnInterval, level, 3f));
        public int MaxConcurrent(int level) => Mathf.Max(1, Mathf.RoundToInt(CurveUtil.EvaluateLevel(maxConcurrent, level, 5f)));
        public float ExperienceMultiplier(int level) => CurveUtil.EvaluateLevel(experienceMultiplier, level);
        public float Aggression(int level) => Mathf.Clamp01(CurveUtil.EvaluateLevel(aggression, level, 1f));
        public float RunStartGrace => Mathf.Max(0f, runStartGrace);
        public float LevelStartGrace(int level) => Mathf.Max(0f, CurveUtil.EvaluateLevel(levelStartGrace, level, 5f));

        /// <summary>Resolve every curve once for a level. Systems cache the snapshot until the level changes.</summary>
        public DifficultySnapshot Evaluate(int level)
        {
            level = Mathf.Max(1, level);
            return new DifficultySnapshot(
                level,
                KillsRequired(level),
                HealthMultiplier(level),
                DamageMultiplier(level),
                SpeedMultiplier(level),
                SpawnInterval(level),
                MaxConcurrent(level),
                ExperienceMultiplier(level),
                LevelStartGrace(level),
                Aggression(level));
        }

        /// <summary>
        /// Populate the curves with the shipped tuning. Called by the asset factory and by
        /// <see cref="Reset"/> when an asset is created from the Create menu.
        /// </summary>
        public void ApplyDefaults()
        {
            killsToAdvance = CurveUtil.FromFunction(l => Mathf.Min(8 + 3 * (l - 1), 40), 1, CurveMaxLevel);
            zombieHealthMultiplier = CurveUtil.FromFunction(l => 1f + 0.18f * (l - 1), 1, CurveMaxLevel);
            zombieDamageMultiplier = CurveUtil.FromFunction(l => 1f + 0.15f * (l - 1), 1, CurveMaxLevel);
            zombieSpeedMultiplier = CurveUtil.FromFunction(l => Mathf.Min(1f + 0.03f * (l - 1), 1.6f), 1, CurveMaxLevel);
            spawnInterval = CurveUtil.FromFunction(l => Mathf.Max(3.0f - 0.12f * (l - 1), 0.8f), 1, CurveMaxLevel);
            maxConcurrent = CurveUtil.FromFunction(l => Mathf.Min(4 + l, 24), 1, CurveMaxLevel);
            experienceMultiplier = CurveUtil.FromFunction(l => 1f + 0.1f * (l - 1), 1, CurveMaxLevel);
            // Annoyed at level 1, fully enraged by level 13, a little worse every level between.
            aggression = CurveUtil.FromFunction(l => Mathf.Clamp01((l - 1) / 12f), 1, CurveMaxLevel);
            runStartGrace = 8f;
            // A breather that grows a little with the level: restocked ammo, a moment to reposition.
            levelStartGrace = CurveUtil.FromFunction(l => Mathf.Min(4f + 0.25f * (l - 1), 8f), 1, CurveMaxLevel);
        }

        private void Reset() => ApplyDefaults();
    }
}
