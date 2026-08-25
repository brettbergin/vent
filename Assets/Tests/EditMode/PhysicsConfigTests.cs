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
