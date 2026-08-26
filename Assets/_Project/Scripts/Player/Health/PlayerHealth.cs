using UnityEngine;
using Vent.Core.Damage;
using Vent.Core.Events;

namespace Vent.Player.Health
{
    /// <summary>
    /// Player hit points with delayed regeneration (the modern-shooter convention: take cover,
    /// recover). Publishes every change through a <see cref="HealthEventChannel"/> so the HUD,
    /// audio and post-processing react without referencing the player.
    /// </summary>
    public sealed class PlayerHealth : MonoBehaviour, IDamageable
    {
        [Header("Stats")]
        [SerializeField, Min(1f)] private float maxHealth = 100f;
        [SerializeField, Min(0f), Tooltip("Seconds after the last hit before regeneration starts.")]
        private float regenDelay = 4f;
        [SerializeField, Min(0f), Tooltip("Health per second once regenerating.")]
        private float regenPerSecond = 12f;

        [Header("Events")]
        [SerializeField] private HealthEventChannel healthChanged;
        [SerializeField] private VoidEventChannel died;

        private float current;
        private float lastDamageTime = float.NegativeInfinity;
        private float invulnerableUntil = float.NegativeInfinity;

        public float Current => current;
        public float Max => maxHealth;
        public float Normalized => current / maxHealth;
        public bool IsAlive => current > 0f;

        /// <summary>When true damage is ignored (menus, the brief spawn grace window).</summary>
        public bool Invulnerable { get; set; }

        /// <summary>Multiplier on incoming damage. A car body takes most of a zombie's swing: the driver sets this below one while seated.</summary>
        public float DamageScale { get; set; } = 1f;

        /// <summary>True while either the manual flag or a timed grant (the Invulnerable perk) is in effect.</summary>
        public bool IsInvulnerable => Invulnerable || Time.time < invulnerableUntil;

        /// <summary>Seconds of timed invulnerability left (zero when none).</summary>
        public float InvulnerableSecondsLeft => Mathf.Max(0f, invulnerableUntil - Time.time);

        /// <summary>Ignore damage for <paramref name="seconds"/>; a second grant extends rather than stacks.</summary>
        public void GrantInvulnerability(float seconds)
        {
            invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + Mathf.Max(0f, seconds));
        }

        public HealthEventChannel HealthChanged
        {
            get => healthChanged;
            set => healthChanged = value;
        }

        public VoidEventChannel Died
        {
            get => died;
            set => died = value;
        }

        private void Awake() => current = maxHealth;

        private void Update()
        {
            if (!IsAlive || current >= maxHealth || Time.time - lastDamageTime < regenDelay)
            {
                return;
            }

            float before = current;
            current = Mathf.Min(maxHealth, current + regenPerSecond * Time.deltaTime);
            Publish(current - before, Vector3.zero);
        }

        public DamageResult ApplyDamage(in DamageInfo info)
        {
            if (!IsAlive || IsInvulnerable || info.Amount <= 0f)
            {
                return DamageResult.None;
            }

            float dealt = Mathf.Min(current, info.Amount * Mathf.Max(0f, DamageScale));
            current -= dealt;
            lastDamageTime = Time.time;

            // Damage direction is reported from the player toward the attacker, flattened to XZ,
            // so the HUD can draw a directional indicator.
            Vector3 fromDir = -info.Direction;
            fromDir.y = 0f;
            Publish(-dealt, fromDir.sqrMagnitude > 0.001f ? fromDir.normalized : Vector3.zero);

            bool killed = current <= 0f;
            if (killed)
            {
                died?.Raise();
            }

            return new DamageResult(dealt, killed);
        }

        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0f)
            {
                return;
            }

            float before = current;
            current = Mathf.Min(maxHealth, current + amount);
            Publish(current - before, Vector3.zero);
        }

        /// <summary>Full heal that also revives; used at run start.</summary>
        public void ResetToFull()
        {
            float before = current;
            current = maxHealth;
            lastDamageTime = float.NegativeInfinity;
            invulnerableUntil = float.NegativeInfinity;
            Publish(current - before, Vector3.zero);
        }

        private void Publish(float delta, Vector3 sourceDirection)
        {
            healthChanged?.Raise(new HealthInfo(current, maxHealth, delta, sourceDirection));
        }
    }
}
