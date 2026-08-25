using UnityEngine;

namespace Vent.Core.Events
{
    // Domain-specific payloads for the gameplay channels (each channel class lives in its own file). Keeping these in
    // Core (rather than Enemies/Weapons) lets the UI and Gameplay assemblies depend on the
    // payload types without depending on the systems that produce them.

    /// <summary>Raised by an enemy when its health reaches zero.</summary>
    public readonly struct KillInfo
    {
        /// <summary>World position of the kill, used for VFX and damage numbers.</summary>
        public readonly Vector3 Position;

        /// <summary>The object that dealt the killing blow (a weapon instance, usually).</summary>
        public readonly Object Killer;

        /// <summary>True if the killing hit was a headshot.</summary>
        public readonly bool Headshot;

        /// <summary>Experience awarded for this kill; scaled by difficulty curves.</summary>
        public readonly int Experience;

        public KillInfo(Vector3 position, Object killer, bool headshot, int experience)
        {
            Position = position;
            Killer = killer;
            Headshot = headshot;
            Experience = experience;
        }
    }

    /// <summary>Raised by the level director whenever the level number changes (including level 1 at run start).</summary>
    public readonly struct LevelInfo
    {
        public readonly int Level;
        public readonly int KillsRequired;

        public LevelInfo(int level, int killsRequired)
        {
            Level = level;
            KillsRequired = killsRequired;
        }
    }

    /// <summary>Raised whenever the player's health changes.</summary>
    public readonly struct HealthInfo
    {
        public readonly float Current;
        public readonly float Max;
        /// <summary>Negative for damage, positive for heals.</summary>
        public readonly float Delta;
        /// <summary>Direction (world-space, XZ) damage came from, or zero. Drives the HUD damage indicator.</summary>
        public readonly Vector3 SourceDirection;

        public float Normalized => Max > 0f ? Mathf.Clamp01(Current / Max) : 0f;

        public HealthInfo(float current, float max, float delta, Vector3 sourceDirection)
        {
            Current = current;
            Max = max;
            Delta = delta;
            SourceDirection = sourceDirection;
        }
    }

    /// <summary>Snapshot of a weapon's presentation state; HUD renders this verbatim.</summary>
    public readonly struct WeaponHudInfo
    {
        public readonly string Name;
        public readonly int SlotIndex;
        public readonly int Magazine;
        public readonly int Reserve;
        public readonly int Level;
        /// <summary>0..1 progress toward the next level; 1 when capped.</summary>
        public readonly float LevelProgress;
        public readonly bool Reloading;
        /// <summary>Current spread (radians) so the crosshair can bloom.</summary>
        public readonly float Spread;

        public WeaponHudInfo(string name, int slotIndex, int magazine, int reserve, int level,
            float levelProgress, bool reloading, float spread)
        {
            Name = name;
            SlotIndex = slotIndex;
            Magazine = magazine;
            Reserve = reserve;
            Level = level;
            LevelProgress = levelProgress;
            Reloading = reloading;
            Spread = spread;
        }
    }

    /// <summary>Raised when a weapon gains a level.</summary>
    public readonly struct WeaponLevelUpInfo
    {
        public readonly string WeaponName;
        public readonly int NewLevel;

        public WeaponLevelUpInfo(string weaponName, int newLevel)
        {
            WeaponName = weaponName;
            NewLevel = newLevel;
        }
    }

    /// <summary>A noise at a point. <see cref="Loudness"/> scales the hearer's hearing radius (1 = a gunshot).</summary>
    public readonly struct NoiseInfo
    {
        public readonly Vector3 Position;
        public readonly float Loudness;

        public NoiseInfo(Vector3 position, float loudness = 1f)
        {
            Position = position;
            Loudness = loudness;
        }
    }
}
