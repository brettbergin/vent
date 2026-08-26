namespace Vent.Gameplay.World
{
    /// <summary>What happened when the player pushed on the door.</summary>
    public enum DoorAction
    {
        Locked,
        Opened,
        AlreadyOpen,
    }

    /// <summary>
    /// The front door's rule, engine-free: locked until the run reaches <see cref="UnlockLevel"/>,
    /// then opened once by the player, and back to locked when a new run starts at level 1.
    /// <see cref="FrontDoor"/> owns the hinges and sounds; this owns the decision.
    /// </summary>
    public sealed class FrontDoorState
    {
        public FrontDoorState(int unlockLevel)
        {
            UnlockLevel = unlockLevel < 1 ? 1 : unlockLevel;
        }

        public int UnlockLevel { get; }
        public bool IsUnlocked { get; private set; }
        public bool IsOpen { get; private set; }

        /// <summary>
        /// Feed every level change here. Returns true the first time the level crosses the
        /// threshold (the moment to celebrate); a level of one or less means a new run and relocks
        /// and closes the door.
        /// </summary>
        public bool OnLevel(int level)
        {
            if (level <= 1)
            {
                IsUnlocked = false;
                IsOpen = false;
                return false;
            }

            if (IsUnlocked || level < UnlockLevel)
            {
                return false;
            }

            IsUnlocked = true;
            return true;
        }

        public DoorAction TryOpen()
        {
            if (!IsUnlocked)
            {
                return DoorAction.Locked;
            }

            if (IsOpen)
            {
                return DoorAction.AlreadyOpen;
            }

            IsOpen = true;
            return DoorAction.Opened;
        }
    }
}
