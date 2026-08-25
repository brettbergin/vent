using UnityEngine;
using Vent.Core.Data;

namespace Vent.Enemies.Data
{
    /// <summary>
    /// Effective zombie numbers for one spawn: definition × difficulty snapshot. The awareness and
    /// timing fields are interpolated from the snapshot's aggression between the definition's
    /// "annoyed" and "enraged" values.
    /// </summary>
    public readonly struct ZombieStats
    {
        public readonly float MaxHealth;
        public readonly float Damage;
        public readonly float Speed;
        public readonly int Experience;

        /// <summary>Notices the player within this distance if there is line of sight.</summary>
        public readonly float NoticeRadius;
        /// <summary>Knows where the player is within this distance regardless of walls.</summary>
        public readonly float SenseRadius;
        /// <summary>Hears a gunshot (loudness 1) within this distance.</summary>
        public readonly float HearingRadius;
        public readonly float AttackWindup;
        public readonly float AttackCooldown;
        public readonly float RepathInterval;
        /// <summary>Speed while wandering, before the player is noticed.</summary>
        public readonly float WanderSpeed;

        /// <summary>Fully enraged stats (always aware). Used by tests and as the level-agnostic default.</summary>
        public ZombieStats(float maxHealth, float damage, float speed, int experience)
            : this(maxHealth, damage, speed, experience, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, 0.45f, 1.1f, 0.2f, speed) { }

        public ZombieStats(float maxHealth, float damage, float speed, int experience, float noticeRadius, float senseRadius,
            float hearingRadius, float attackWindup, float attackCooldown, float repathInterval, float wanderSpeed)
        {
            MaxHealth = maxHealth;
            Damage = damage;
            Speed = speed;
            Experience = experience;
            NoticeRadius = noticeRadius;
            SenseRadius = senseRadius;
            HearingRadius = hearingRadius;
            AttackWindup = attackWindup;
            AttackCooldown = attackCooldown;
            RepathInterval = repathInterval;
            WanderSpeed = wanderSpeed;
        }

        public static ZombieStats From(ZombieDefinition def, in DifficultySnapshot difficulty)
        {
            float a = difficulty.Aggression;
            float speed = def.BaseSpeed * difficulty.SpeedMultiplier;
            return new ZombieStats(
                def.BaseHealth * difficulty.HealthMultiplier,
                def.BaseDamage * difficulty.DamageMultiplier,
                speed,
                Mathf.Max(1, Mathf.RoundToInt(def.BaseExperience * difficulty.ExperienceMultiplier)),
                noticeRadius: Mathf.Lerp(def.AnnoyedNoticeRadius, def.EnragedAwarenessRadius, a),
                senseRadius: Mathf.Lerp(0f, def.EnragedAwarenessRadius, a),
                hearingRadius: Mathf.Lerp(def.AnnoyedHearingRadius, def.EnragedAwarenessRadius, a),
                attackWindup: Mathf.Lerp(def.AnnoyedAttackWindup, def.AttackWindup, a),
                attackCooldown: Mathf.Lerp(def.AnnoyedAttackCooldown, def.AttackCooldown, a),
                repathInterval: Mathf.Lerp(def.AnnoyedRepathInterval, def.RepathInterval, a),
                wanderSpeed: speed * def.WanderSpeedFraction);
        }
    }

    /// <summary>
    /// The one zombie archetype. There is only one on purpose: the design brief is that the
    /// enemies never change, only their numbers do (see <see cref="DifficultyProfile"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "Vent/Enemies/Zombie Definition", fileName = "Zombie")]
    public sealed class ZombieDefinition : ScriptableObject
    {
        [Header("Base stats (level 1)")]
        [SerializeField, Min(1f)] private float baseHealth = 100f;
        [SerializeField, Min(0f)] private float baseDamage = 12f;
        [SerializeField, Min(0.1f)] private float baseSpeed = 3.4f;
        [SerializeField, Min(1)] private int baseExperience = 25;

        [Header("Melee")]
        [SerializeField, Min(0.1f)] private float attackRange = 1.7f;
        [SerializeField, Min(0.05f), Tooltip("Seconds between the attack starting and damage landing.")]
        private float attackWindup = 0.45f;
        [SerializeField, Min(0.1f)] private float attackCooldown = 1.1f;
        [SerializeField, Range(0f, 180f), Tooltip("Half-angle the zombie must be facing the player within to land a hit.")]
        private float attackArc = 60f;

        [Header("Hit reactions")]
        [SerializeField, Range(0f, 1f), Tooltip("A single hit removing at least this fraction of max health staggers the zombie. Any non-lethal headshot also staggers.")]
        private float staggerThreshold = 0.3f;
        [SerializeField, Min(0.05f)] private float staggerSeconds = 0.45f;
        [SerializeField, Range(0.1f, 1f), Tooltip("Damage multiplier for arm and leg hits (head and torso are set on their hitboxes).")]
        private float limbDamageMultiplier = 0.65f;

        [Header("Life cycle")]
        [SerializeField, Min(0.1f), Tooltip("Seconds to climb out of a vent before pathing begins.")]
        private float emergeSeconds = 1.1f;
        [SerializeField, Min(0.1f), Tooltip("Seconds a corpse stays before returning to the pool.")]
        private float corpseSeconds = 2.5f;
        [SerializeField, Min(0.1f)] private float repathInterval = 0.2f;
        [SerializeField] private Vector2 growlIntervalRange = new(3f, 7f);

        [Header("Aggression: annoyed end (the enraged end is the melee/life-cycle values above)")]
        [SerializeField, Min(0f), Tooltip("Sees the player within this distance (line of sight) when annoyed.")]
        private float annoyedNoticeRadius = 8f;
        [SerializeField, Min(0f), Tooltip("Hears a gunshot within this distance when annoyed.")]
        private float annoyedHearingRadius = 14f;
        [SerializeField, Min(0f), Tooltip("Enraged: notices, senses through walls and hears within this distance — the whole building.")]
        private float enragedAwarenessRadius = 60f;
        [SerializeField, Min(0.05f)] private float annoyedAttackWindup = 0.75f;
        [SerializeField, Min(0.1f)] private float annoyedAttackCooldown = 1.9f;
        [SerializeField, Min(0.1f)] private float annoyedRepathInterval = 0.5f;
        [SerializeField, Range(0.1f, 1f), Tooltip("Fraction of chase speed while shambling around before noticing the player.")]
        private float wanderSpeedFraction = 0.4f;
        [SerializeField, Min(0.5f), Tooltip("How far from its current spot a wandering zombie picks its next point.")]
        private float wanderRadius = 4f;
        [SerializeField, Tooltip("Seconds between wander destinations.")]
        private Vector2 wanderRepickRange = new(1.5f, 3.5f);

        [Header("Presentation")]
        [SerializeField] private GameObject prefab;

        public float BaseHealth => baseHealth;
        public float BaseDamage => baseDamage;
        public float BaseSpeed => baseSpeed;
        public int BaseExperience => baseExperience;
        public float AttackRange => attackRange;
        public float AttackWindup => attackWindup;
        public float AttackCooldown => attackCooldown;
        public float AttackArc => attackArc;
        public float StaggerThreshold => staggerThreshold;
        public float StaggerSeconds => staggerSeconds;
        public float LimbDamageMultiplier => limbDamageMultiplier;
        public float EmergeSeconds => emergeSeconds;
        public float CorpseSeconds => corpseSeconds;
        public float RepathInterval => repathInterval;
        public Vector2 GrowlIntervalRange => growlIntervalRange;
        public float AnnoyedNoticeRadius => annoyedNoticeRadius;
        public float AnnoyedHearingRadius => annoyedHearingRadius;
        public float EnragedAwarenessRadius => enragedAwarenessRadius;
        public float AnnoyedAttackWindup => annoyedAttackWindup;
        public float AnnoyedAttackCooldown => annoyedAttackCooldown;
        public float AnnoyedRepathInterval => annoyedRepathInterval;
        public float WanderSpeedFraction => wanderSpeedFraction;
        public float WanderRadius => wanderRadius;
        public Vector2 WanderRepickRange => wanderRepickRange;
        public GameObject Prefab => prefab;

        public void SetPrefab(GameObject value) => prefab = value;
    }
}
