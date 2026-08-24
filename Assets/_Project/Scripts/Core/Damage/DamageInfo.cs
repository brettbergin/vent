using UnityEngine;

namespace Vent.Core.Damage
{
    /// <summary>What kind of thing dealt the damage; used for VFX, stats and death causes.</summary>
    public enum DamageKind
    {
        Bullet,
        Melee,
        Environment,
    }

    /// <summary>
    /// Immutable description of a single damage event. Passed by <c>in</c> reference so hot paths
    /// (automatic weapons) do not copy it repeatedly.
    /// </summary>
    public readonly struct DamageInfo
    {
        /// <summary>Damage before target-side modifiers such as headshot multipliers.</summary>
        public readonly float Amount;

        public readonly DamageKind Kind;

        /// <summary>Object responsible for the damage (weapon instance, zombie, etc.). May be null.</summary>
        public readonly Object Source;

        /// <summary>World-space hit position, for impact effects.</summary>
        public readonly Vector3 Point;

        /// <summary>World-space surface normal at the hit point.</summary>
        public readonly Vector3 Normal;

        /// <summary>Direction the damage travelled (bullet direction / from-attacker vector), normalised.</summary>
        public readonly Vector3 Direction;

        /// <summary>True if this hit struck a head hitbox.</summary>
        public readonly bool Headshot;

        public DamageInfo(float amount, DamageKind kind, Object source, Vector3 point, Vector3 normal,
            Vector3 direction, bool headshot = false)
        {
            Amount = amount;
            Kind = kind;
            Source = source;
            Point = point;
            Normal = normal;
            Direction = direction;
            Headshot = headshot;
        }

        /// <summary>Copy with a different amount and headshot flag (used by hitboxes applying multipliers).</summary>
        public DamageInfo WithAmount(float amount, bool headshot)
        {
            return new DamageInfo(amount, Kind, Source, Point, Normal, Direction, headshot);
        }
    }

    /// <summary>Outcome of <see cref="IDamageable.ApplyDamage"/>.</summary>
    public readonly struct DamageResult
    {
        /// <summary>Damage actually subtracted from health (after clamping to remaining health).</summary>
        public readonly float DamageDealt;

        /// <summary>True if this event reduced health to zero.</summary>
        public readonly bool Killed;

        /// <summary>True if the target ignored the damage (already dead, invulnerable).</summary>
        public readonly bool Ignored;

        public static readonly DamageResult None = new(0f, false, true);

        public DamageResult(float damageDealt, bool killed, bool ignored = false)
        {
            DamageDealt = damageDealt;
            Killed = killed;
            Ignored = ignored;
        }
    }
}
