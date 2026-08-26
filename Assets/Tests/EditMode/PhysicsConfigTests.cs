using NUnit.Framework;
using UnityEngine;
using Vent.Core.Utility;

namespace Vent.Tests.EditMode
{
    /// <summary>
    /// Guards the collision matrix that keeps the player contained. The reported bug: a crowd of
    /// zombies squeezed the player's CharacterController through a wall because Zombie and Player
    /// layers collided. These assertions fail if that regresses.
    /// </summary>
    public sealed class PhysicsConfigTests
    {
        [Test]
        public void ZombiesDoNotCollideWithThePlayer()
        {
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(Layers.ZombieIndex, Layers.PlayerIndex),
                "Zombie and Player layers must not collide, or a crowd can push the player through walls.");
        }

        [Test]
        public void ZombiesDoNotCollideWithEachOtherOrVents()
        {
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(Layers.ZombieIndex, Layers.ZombieIndex), "Zombie-Zombie should be ignored (NavMesh avoidance handles crowding).");
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(Layers.ZombieIndex, Layers.VentIndex), "Zombie-Vent should be ignored.");
        }

        [Test]
        public void TheViewModelLayerCollidesWithNothing()
        {
            for (int layer = 0; layer < 32; layer++)
            {
                Assert.IsTrue(Physics.GetIgnoreLayerCollision(Layers.WeaponViewIndex, layer),
                    $"WeaponView must not collide with layer {layer} (it is render-only).");
            }
        }

        [Test]
        public void VehiclesIgnoreZombiesAndVentsButHitEverythingElse()
        {
            // A NavMeshAgent cannot be pushed, so a car would bounce off a zombie like a bollard;
            // roadkill is an overlap query in code instead. Manhole covers are Vent-layer bullet targets.
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(Layers.VehicleIndex, Layers.ZombieIndex), "Vehicle-Zombie must be ignored (roadkill is applied in code).");
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(Layers.VehicleIndex, Layers.VentIndex), "Vehicle-Vent must be ignored (manhole covers are not bumps).");
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(Layers.VehicleIndex, Layers.EnvironmentIndex), "cars drive on the streets and hit walls");
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(Layers.VehicleIndex, Layers.PlayerIndex), "parked cars are solid to the player on foot");
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(Layers.VehicleIndex, Layers.VehicleIndex), "cars collide with each other");
        }

        [Test]
        public void WorldBoundsCoverTheDistrict()
        {
            Object dynamics = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0];
            var so = new UnityEditor.SerializedObject(dynamics);
            UnityEditor.SerializedProperty bounds = so.FindProperty("m_WorldBounds");
            Assert.IsNotNull(bounds, "DynamicsManager exposes m_WorldBounds");
            Vector3 extent = bounds.FindPropertyRelative("m_Extent").vector3Value;
            Assert.GreaterOrEqual(extent.x, 380f, "the district reaches ±193 m; PhysX culls bodies outside the world bounds");
            Assert.GreaterOrEqual(extent.z, 380f);
        }

        [Test]
        public void ZombiesStillCollideWithTheEnvironment()
        {
            // NavMesh geometry is baked from Environment colliders; the matrix should leave them intact.
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(Layers.ZombieIndex, Layers.EnvironmentIndex),
                "Zombies vs Environment must remain enabled.");
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(Layers.PlayerIndex, Layers.EnvironmentIndex),
                "Player vs Environment must remain enabled, or the player would fall through the floor.");
        }
    }
}
