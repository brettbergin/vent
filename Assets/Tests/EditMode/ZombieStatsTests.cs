using NUnit.Framework;
using UnityEngine;
using Vent.Core.Data;
using Vent.Enemies.Data;

namespace Vent.Tests.EditMode
{
    /// <summary>Aggression is a number in the profile; these pin what it does to a zombie's numbers.</summary>
    public sealed class ZombieStatsTests
    {
        private ZombieDefinition def;

        [SetUp]
        public void SetUp() => def = ScriptableObject.CreateInstance<ZombieDefinition>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(def);

        private static DifficultySnapshot Snapshot(float aggression) => new(1, 8, 1f, 1f, 1f, 3f, 5, 1f, 0f, aggression);

        [Test]
        public void AnnoyedZombiesNoticeLessStrikeSlowerAndTrackLooser()
        {
            ZombieStats annoyed = ZombieStats.From(def, Snapshot(0f));
            ZombieStats enraged = ZombieStats.From(def, Snapshot(1f));

            Assert.Less(annoyed.NoticeRadius, enraged.NoticeRadius);
            Assert.AreEqual(0f, annoyed.SenseRadius, "annoyed zombies cannot feel you through walls");
            Assert.Greater(enraged.SenseRadius, 50f, "enraged zombies know where you are anywhere in the building");
            Assert.Less(annoyed.HearingRadius, enraged.HearingRadius);
            Assert.Greater(annoyed.AttackWindup, enraged.AttackWindup);
            Assert.Greater(annoyed.AttackCooldown, enraged.AttackCooldown);
            Assert.Greater(annoyed.RepathInterval, enraged.RepathInterval);
            Assert.Less(annoyed.WanderSpeed, annoyed.Speed);
        }

        [Test]
        public void HalfAggressionSitsBetweenTheEnds()
        {
            ZombieStats a0 = ZombieStats.From(def, Snapshot(0f));
            ZombieStats a1 = ZombieStats.From(def, Snapshot(1f));
            ZombieStats mid = ZombieStats.From(def, Snapshot(0.5f));

            Assert.AreEqual((a0.AttackWindup + a1.AttackWindup) / 2f, mid.AttackWindup, 1e-4f);
            Assert.AreEqual((a0.NoticeRadius + a1.NoticeRadius) / 2f, mid.NoticeRadius, 1e-4f);
            Assert.AreEqual(a1.SenseRadius / 2f, mid.SenseRadius, 1e-4f);
        }

        [Test]
        public void ShortConstructorIsFullyAware()
        {
            var stats = new ZombieStats(50f, 5f, 3f, 25);
            Assert.IsTrue(float.IsPositiveInfinity(stats.SenseRadius));
            Assert.AreEqual(3f, stats.WanderSpeed);
        }
    }
}
