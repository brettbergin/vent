using NUnit.Framework;
using Vent.Weapons.Progression;

namespace Vent.Tests.EditMode
{
    public sealed class WeaponProgressionTests
    {
        private sealed class FlatTable : IWeaponLevelTable
        {
            public int MaxLevel { get; set; } = 5;
            public int PerLevel { get; set; } = 100;
            public int ExperienceToNext(int level) => PerLevel;
        }

        [Test]
        public void StartsAtLevelOneWithNoExperience()
        {
            var p = new WeaponProgression(new FlatTable());
            Assert.AreEqual(1, p.Level);
            Assert.AreEqual(0, p.Experience);
            Assert.AreEqual(0f, p.Progress01);
            Assert.IsFalse(p.IsMaxLevel);
        }

        [Test]
        public void ExperienceBelowThresholdDoesNotLevel()
        {
            var p = new WeaponProgression(new FlatTable());
            int gained = p.AddExperience(50);
            Assert.AreEqual(0, gained);
            Assert.AreEqual(1, p.Level);
            Assert.AreEqual(0.5f, p.Progress01, 1e-5f);
        }

        [Test]
        public void ReachingThresholdLevelsUpAndCarriesSurplus()
        {
            var p = new WeaponProgression(new FlatTable());
            p.AddExperience(50);
            int gained = p.AddExperience(60);
            Assert.AreEqual(1, gained);
            Assert.AreEqual(2, p.Level);
            Assert.AreEqual(10, p.Experience);
        }

        [Test]
        public void LargeGrantCanSkipSeveralLevels()
        {
            var p = new WeaponProgression(new FlatTable());
            int levelUps = 0;
            p.LevelUp += _ => levelUps++;
            int gained = p.AddExperience(250);
            Assert.AreEqual(2, gained);
            Assert.AreEqual(2, levelUps);
            Assert.AreEqual(3, p.Level);
            Assert.AreEqual(50, p.Experience);
        }

        [Test]
        public void CapsAtMaxLevelAndDiscardsExperience()
        {
            var p = new WeaponProgression(new FlatTable { MaxLevel = 3 });
            p.AddExperience(10_000);
            Assert.AreEqual(3, p.Level);
            Assert.IsTrue(p.IsMaxLevel);
            Assert.AreEqual(0, p.Experience);
            Assert.AreEqual(1f, p.Progress01);
            Assert.AreEqual(0, p.AddExperience(100));
        }

        [Test]
        public void ResetReturnsToLevelOne()
        {
            var p = new WeaponProgression(new FlatTable());
            p.AddExperience(350);
            p.Reset();
            Assert.AreEqual(1, p.Level);
            Assert.AreEqual(0, p.Experience);
            Assert.AreEqual(0, p.TotalExperience);
        }

        [Test]
        public void NegativeOrZeroExperienceIsIgnored()
        {
            var p = new WeaponProgression(new FlatTable());
            Assert.AreEqual(0, p.AddExperience(0));
            Assert.AreEqual(0, p.AddExperience(-20));
            Assert.AreEqual(0, p.Experience);
        }
    }
}
