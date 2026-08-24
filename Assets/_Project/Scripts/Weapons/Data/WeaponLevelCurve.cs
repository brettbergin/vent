using UnityEngine;
using Vent.Core.Data;
using Vent.Weapons.Progression;

namespace Vent.Weapons.Data
{
    /// <summary>Multipliers applied to a weapon's base stats at a given level.</summary>
    public readonly struct WeaponLevelModifiers
    {
        public readonly int Level;
        public readonly float DamageMultiplier;
        public readonly float FireRateMultiplier;
        public readonly float MagazineMultiplier;
        public readonly float ReloadSpeedMultiplier;
        public readonly float SpreadMultiplier;

        public WeaponLevelModifiers(int level, float damage, float fireRate, float magazine, float reloadSpeed, float spread)
        {
            Level = level;
            DamageMultiplier = damage;
            FireRateMultiplier = fireRate;
            MagazineMultiplier = magazine;
            ReloadSpeedMultiplier = reloadSpeed;
            SpreadMultiplier = spread;
        }

        public static readonly WeaponLevelModifiers Identity = new(1, 1f, 1f, 1f, 1f, 1f);
    }

    /// <summary>
    /// How a weapon grows with kills. XP requirements and stat multipliers are curves over the
    /// weapon level so tuning is data. Implements <see cref="IWeaponLevelTable"/> so the pure
    /// <see cref="WeaponProgression"/> class can be unit tested against a fake table.
    /// </summary>
    [CreateAssetMenu(menuName = "Vent/Weapons/Weapon Level Curve", fileName = "WeaponLevels_")]
    public sealed class WeaponLevelCurve : ScriptableObject, IWeaponLevelTable
    {
        [SerializeField, Min(1)] private int maxLevel = 25;

        [SerializeField, Tooltip("X = current level, Y = experience needed to reach the next level.")]
        private AnimationCurve experienceToNextLevel;

        [Header("Multipliers (X = level)")]
        [SerializeField] private AnimationCurve damageMultiplier;
        [SerializeField] private AnimationCurve fireRateMultiplier;
        [SerializeField] private AnimationCurve magazineMultiplier;
        [SerializeField] private AnimationCurve reloadSpeedMultiplier;
        [SerializeField] private AnimationCurve spreadMultiplier;

        public int MaxLevel => maxLevel;

        public int ExperienceToNext(int level)
        {
            return Mathf.Max(1, Mathf.RoundToInt(CurveUtil.EvaluateLevel(experienceToNextLevel, level, 100f)));
        }

        public WeaponLevelModifiers Evaluate(int level)
        {
            level = Mathf.Clamp(level, 1, maxLevel);
            return new WeaponLevelModifiers(
                level,
                CurveUtil.EvaluateLevel(damageMultiplier, level),
                CurveUtil.EvaluateLevel(fireRateMultiplier, level),
                CurveUtil.EvaluateLevel(magazineMultiplier, level),
                CurveUtil.EvaluateLevel(reloadSpeedMultiplier, level),
                CurveUtil.EvaluateLevel(spreadMultiplier, level));
        }

        /// <summary>Shipped tuning. Damage is the headline reward; the rest are gentle quality-of-life gains.</summary>
        public void ApplyDefaults(int cap = 25)
        {
            maxLevel = cap;
            experienceToNextLevel = CurveUtil.FromFunction(l => 60 + 35 * (l - 1) + 4 * (l - 1) * (l - 1), 1, cap);
            damageMultiplier = CurveUtil.FromFunction(l => 1f + 0.12f * (l - 1), 1, cap);
            fireRateMultiplier = CurveUtil.FromFunction(l => Mathf.Min(1f + 0.02f * (l - 1), 1.4f), 1, cap);
            magazineMultiplier = CurveUtil.FromFunction(l => 1f + 0.05f * (l - 1), 1, cap);
            reloadSpeedMultiplier = CurveUtil.FromFunction(l => Mathf.Min(1f + 0.04f * (l - 1), 1.8f), 1, cap);
            spreadMultiplier = CurveUtil.FromFunction(l => Mathf.Max(1f - 0.025f * (l - 1), 0.5f), 1, cap);
        }

        private void Reset() => ApplyDefaults();
    }
}
