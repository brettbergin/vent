using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Vent.Core.Damage;
using Vent.Enemies.Runtime;

namespace Vent.Tests.EditMode
{
    /// <summary>The generated zombie prefab is the canonical enemy; pin the parts the game relies on.</summary>
    public sealed class ZombiePrefabTests
    {
        private GameObject prefab;

        [SetUp]
        public void Load()
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Vent.Editor.Paths.Prefabs}/Zombie.prefab");
            Assert.IsNotNull(prefab, "Zombie prefab missing; run Vent/Rebuild Everything.");
        }

        [Test]
        public void HasHeadTorsoAndLimbHitboxesWithDistinctMultipliers()
        {
            Hitbox[] boxes = prefab.GetComponentsInChildren<Hitbox>(true);
            Assert.AreEqual(1, boxes.Count(b => b.IsHead), "exactly one head hitbox");
            Assert.AreEqual(2.5f, boxes.First(b => b.IsHead).DamageMultiplier, 1e-4f);
            Assert.AreEqual(1, boxes.Count(b => !b.IsHead && Mathf.Approximately(b.DamageMultiplier, 1f)), "one torso hitbox at 1x");
            int limbs = boxes.Count(b => !b.IsHead && b.DamageMultiplier < 1f);
            Assert.AreEqual(8, limbs, "upper/lower segments of two arms and two legs");
            Assert.IsTrue(boxes.All(b => b.GetComponent<Collider>() != null));
        }

        [Test]
        public void HasAHealthBarAndAJointedRig()
        {
            Assert.IsNotNull(prefab.GetComponentInChildren<ZombieHealthBar>(true));
            foreach (string joint in new[] { "Hips", "Spine", "Head", "Jaw", "LeftShoulder", "LeftElbow", "RightShoulder", "RightElbow", "LeftHip", "LeftKnee", "RightHip", "RightKnee" })
            {
                Assert.IsNotNull(prefab.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == joint), $"missing joint {joint}");
            }
        }
    }
}
