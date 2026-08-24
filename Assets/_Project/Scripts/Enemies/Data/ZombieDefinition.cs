using UnityEngine;
using Vent.Core.Data;

namespace Vent.Enemies.Data
{
    /// <summary>Effective zombie numbers for one spawn: definition × difficulty snapshot.</summary>
    public readonly struct ZombieStats
    {
        public readonly float MaxHealth;
        public readonly float Damage;
        public readonly float Speed;
        public readonly int Experience;

        public ZombieStats(float maxHealth, float damage, float speed, int experience)
        {
            MaxHealth = maxHealth;
            Damage = damage;
            Speed = speed;
            Experience = experience;
        }

        public static ZombieStats From(ZombieDefinition def, in DifficultySnapshot difficulty)
        {
            return new ZombieStats(
                def.BaseHealth * difficulty.HealthMultiplier,
                def.BaseDamage * difficulty.DamageMultiplier,
                def.BaseSpeed * difficulty.SpeedMultiplier,
                Mathf.Max(1, Mathf.RoundToInt(def.BaseExperience * difficulty.ExperienceMultiplier)));
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

        [Header("Life cycle")]
        [SerializeField, Min(0.1f), Tooltip("Seconds to climb out of a vent before pathing begins.")]
        private float emergeSeconds = 1.1f;
        [SerializeField, Min(0.1f), Tooltip("Seconds a corpse stays before returning to the pool.")]
        private float corpseSeconds = 2.5f;
        [SerializeField, Min(0.1f)] private float repathInterval = 0.2f;
        [SerializeField] private Vector2 growlIntervalRange = new(3f, 7f);

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
        public float EmergeSeconds => emergeSeconds;
        public float CorpseSeconds => corpseSeconds;
        public float RepathInterval => repathInterval;
        public Vector2 GrowlIntervalRange => growlIntervalRange;
        public GameObject Prefab => prefab;

        public void SetPrefab(GameObject value) => prefab = value;
    }
}
