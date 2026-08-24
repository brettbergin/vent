using NUnit.Framework;
using UnityEngine;
using Vent.Core.Data;

namespace Vent.Tests.EditMode
{
    public sealed class DifficultyProfileTests
    {
        private DifficultyProfile profile;

        [SetUp]
        public void SetUp()
        {
            profile = ScriptableObject.CreateInstance<DifficultyProfile>();
            profile.ApplyDefaults();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(profile);

        [Test]
        public void LevelOneIsBaseline()
        {
            DifficultySnapshot s = profile.Evaluate(1);
            Assert.AreEqual(1f, s.HealthMultiplier, 1e-4f);
            Assert.AreEqual(1f, s.DamageMultiplier, 1e-4f);
            Assert.AreEqual(1f, s.SpeedMultiplier, 1e-4f);
            Assert.AreEqual(8, s.KillsRequired);
        }

        [Test]
        public void ZombiesOnlyGetHarderWithLevel()
        {
            DifficultySnapshot previous = profile.Evaluate(1);
            for (int level = 2; level <= 80; level++)
            {
                DifficultySnapshot s = profile.Evaluate(level);
                Assert.GreaterOrEqual(s.HealthMultiplier, previous.HealthMultiplier, $"health at {level}");
                Assert.GreaterOrEqual(s.DamageMultiplier, previous.DamageMultiplier, $"damage at {level}");
                Assert.GreaterOrEqual(s.SpeedMultiplier, previous.SpeedMultiplier, $"speed at {level}");
                Assert.LessOrEqual(s.SpawnInterval, previous.SpawnInterval, $"interval at {level}");
                Assert.GreaterOrEqual(s.MaxConcurrent, previous.MaxConcurrent, $"concurrent at {level}");
                Assert.GreaterOrEqual(s.KillsRequired, previous.KillsRequired, $"kills at {level}");
                previous = s;
            }
        }

        [Test]
        public void ValuesStayWithinSaneBounds()
        {
            for (int level = 1; level <= 500; level++)
            {
                DifficultySnapshot s = profile.Evaluate(level);
                Assert.GreaterOrEqual(s.SpawnInterval, 0.8f);
                Assert.LessOrEqual(s.MaxConcurrent, 24);
                Assert.LessOrEqual(s.SpeedMultiplier, 1.6f + 1e-4f);
                Assert.LessOrEqual(s.KillsRequired, 40);
            }
        }

        [Test]
        public void LevelsBelowOneClampToOne()
        {
            Assert.AreEqual(profile.Evaluate(1).HealthMultiplier, profile.Evaluate(0).HealthMultiplier);
            Assert.AreEqual(profile.Evaluate(1).KillsRequired, profile.Evaluate(-5).KillsRequired);
        }

        [Test]
        public void CurvesBeyondTheirLastKeyClamp()
        {
            DifficultySnapshot last = profile.Evaluate(DifficultyProfile.CurveMaxLevel);
            DifficultySnapshot beyond = profile.Evaluate(DifficultyProfile.CurveMaxLevel + 100);
            Assert.AreEqual(last.HealthMultiplier, beyond.HealthMultiplier, 1e-4f);
            Assert.AreEqual(last.DamageMultiplier, beyond.DamageMultiplier, 1e-4f);
        }
    }
}
