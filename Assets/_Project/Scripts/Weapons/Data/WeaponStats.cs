using UnityEngine;

namespace Vent.Weapons.Data
{
    /// <summary>
    /// A weapon's effective numbers: definition × level modifiers. Recomputed only when the
    /// level changes, so the hot path (firing) reads plain fields.
    /// </summary>
    public readonly struct WeaponStats
    {
        public readonly float Damage;
        public readonly float RoundsPerMinute;
        public readonly float SecondsBetweenShots;
        public readonly int MagazineSize;
        public readonly float ReloadSeconds;
        public readonly float EmptyReloadSeconds;
        public readonly float SpreadScale;

        public WeaponStats(WeaponDefinition def, in WeaponLevelModifiers mods)
        {
            Damage = def.Damage * mods.DamageMultiplier;
            RoundsPerMinute = def.RoundsPerMinute * mods.FireRateMultiplier;
            SecondsBetweenShots = 60f / Mathf.Max(1f, RoundsPerMinute);
            MagazineSize = Mathf.Max(1, Mathf.RoundToInt(def.MagazineSize * mods.MagazineMultiplier));
            ReloadSeconds = def.ReloadSeconds / Mathf.Max(0.01f, mods.ReloadSpeedMultiplier);
            EmptyReloadSeconds = def.EmptyReloadSeconds / Mathf.Max(0.01f, mods.ReloadSpeedMultiplier);
            SpreadScale = mods.SpreadMultiplier;
        }
    }
}
