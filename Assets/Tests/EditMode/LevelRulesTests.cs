using NUnit.Framework;
using Vent.Gameplay.Levels;

namespace Vent.Tests.EditMode
{
    public sealed class LevelRulesTests
    {
        private static int N(int level) => 8 + 3 * (level - 1);

        [Test]
        public void StartsAtLevelOne()
        {
            var rules = new LevelRules(N);
            Assert.AreEqual(1, rules.Level);
            Assert.AreEqual(8, rules.KillsRequired);
            Assert.AreEqual(8, rules.KillsRemaining);
        }

        [Test]
        public void KillsBelowRequirementDoNotAdvance()
        {
            var rules = new LevelRules(N);
            for (int i = 0; i < 7; i++)
            {
                Assert.IsFalse(rules.RegisterKill());
            }

            Assert.AreEqual(1, rules.Level);
            Assert.AreEqual(7, rules.KillsThisLevel);
            Assert.AreEqual(1, rules.KillsRemaining);
        }

        [Test]
        public void NthKillAdvancesAndResetsCounter()
        {
            var rules = new LevelRules(N);
            for (int i = 0; i < 7; i++)
            {
                rules.RegisterKill();
            }

            Assert.IsTrue(rules.RegisterKill());
            Assert.AreEqual(2, rules.Level);
            Assert.AreEqual(0, rules.KillsThisLevel);
            Assert.AreEqual(11, rules.KillsRequired);
            Assert.AreEqual(8, rules.TotalKills);
        }

        [Test]
        public void LevelsAreUnbounded()
        {
            var rules = new LevelRules(N);
            for (int i = 0; i < 10_000; i++)
            {
                rules.RegisterKill();
            }

            Assert.Greater(rules.Level, 50);
            Assert.AreEqual(10_000, rules.TotalKills);
        }

        [Test]
        public void ResetClearsEverything()
        {
            var rules = new LevelRules(N);
            for (int i = 0; i < 20; i++)
            {
                rules.RegisterKill();
            }

            rules.Reset();
            Assert.AreEqual(1, rules.Level);
            Assert.AreEqual(0, rules.KillsThisLevel);
            Assert.AreEqual(0, rules.TotalKills);
        }
    }
}
