using NUnit.Framework;
using Vent.Gameplay.World;

namespace Vent.Tests.EditMode
{
    /// <summary>
    /// The front door's rule in isolation: locked through the warm-up levels, unlocked once at the
    /// threshold, opened once by the player, and back to square one when a new run starts.
    /// </summary>
    public sealed class FrontDoorStateTests
    {
        [Test]
        public void LockedUntilTheUnlockLevel()
        {
            var door = new FrontDoorState(4);
            Assert.IsFalse(door.OnLevel(1));
            Assert.IsFalse(door.OnLevel(2));
            Assert.IsFalse(door.OnLevel(3));
            Assert.IsFalse(door.IsUnlocked);
            Assert.AreEqual(DoorAction.Locked, door.TryOpen());
            Assert.IsFalse(door.IsOpen);
        }

        [Test]
        public void UnlocksOnceAtTheThresholdAndOpensOnce()
        {
            var door = new FrontDoorState(4);
            Assert.IsTrue(door.OnLevel(4), "crossing the threshold is reported exactly once");
            Assert.IsFalse(door.OnLevel(5), "later levels are not a second unlock");
            Assert.IsTrue(door.IsUnlocked);
            Assert.AreEqual(DoorAction.Opened, door.TryOpen());
            Assert.AreEqual(DoorAction.AlreadyOpen, door.TryOpen());
            Assert.IsTrue(door.IsOpen);
        }

        [Test]
        public void SkippingPastTheThresholdStillUnlocks()
        {
            var door = new FrontDoorState(4);
            Assert.IsTrue(door.OnLevel(7));
        }

        [Test]
        public void ANewRunRelocksAndCloses()
        {
            var door = new FrontDoorState(4);
            door.OnLevel(4);
            door.TryOpen();
            Assert.IsFalse(door.OnLevel(1));
            Assert.IsFalse(door.IsUnlocked);
            Assert.IsFalse(door.IsOpen);
            Assert.AreEqual(DoorAction.Locked, door.TryOpen());
            Assert.IsTrue(door.OnLevel(4), "and it can unlock again next run");
        }
    }
}
