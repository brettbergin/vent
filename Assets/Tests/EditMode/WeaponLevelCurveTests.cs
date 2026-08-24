using NUnit.Framework;
using UnityEngine;
using Vent.Weapons.Data;

namespace Vent.Tests.EditMode
{
    public sealed class WeaponLevelCurveTests
    {
        private WeaponLevelCurve curve;

        [SetUp]
        public void SetUp()
        {
            curve = ScriptableObject.CreateInstance<WeaponLevelCurve>();
            curve.ApplyDefaults(20);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(curve);

        [Test]
        public void ExperienceRequirementsGrow()
        {
            int previous = 0;
            for (int level = 1; level < curve.MaxLevel; level++)
            {
                int xp = curve.ExperienceToNext(level);
                Assert.Greater(xp, previous);
                previous = xp;
            }
        }

        [Test]
        public void DamageGrowsAndSpreadShrinks()
        {
            WeaponLevelModifiers l1 = curve.Evaluate(1);
            WeaponLevelModifiers l10 = curve.Evaluate(10);
            Assert.AreEqual(1f, l1.DamageMultiplier, 1e-4f);
            Assert.Greater(l10.DamageMultiplier, l1.DamageMultiplier);
            Assert.Less(l10.SpreadMultiplier, l1.SpreadMultiplier);
            Assert.GreaterOrEqual(l10.MagazineMultiplier, l1.MagazineMultiplier);
        }

        [Test]
        public void EvaluateClampsToMaxLevel()
        {
            Assert.AreEqual(curve.Evaluate(curve.MaxLevel).DamageMultiplier, curve.Evaluate(999).DamageMultiplier, 1e-4f);
        }

        [Test]
        public void StatsCombineDefinitionAndModifiers()
        {
            var def = ScriptableObject.CreateInstance<WeaponDefinition>();
            def.Configure("Test", WeaponSlot.Primary, FireMode.Automatic, 10f, 600f, 30, 90, 120, 2f, 0.3f,
                0.5f, 1f, 0.2f, 3f, 8f, 0.4f, Vector2.one, Vector2.zero, Core.Audio.SoundId.SmgShot, curve);
            var stats = new WeaponStats(def, curve.Evaluate(1));
            Assert.AreEqual(10f, stats.Damage, 1e-4f);
            Assert.AreEqual(0.1f, stats.SecondsBetweenShots, 1e-4f);
            Assert.AreEqual(30, stats.MagazineSize);
            Assert.AreEqual(2f, stats.ReloadSeconds, 1e-4f);
            Object.DestroyImmediate(def);
        }
    }
}
