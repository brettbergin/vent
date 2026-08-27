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
    /// The front door's rule, engine-free: locked until the run reaches <see cref="UnlockLevel"/>
    /// or the player finds the office key, then opened once by the player, and back to locked
    /// when a new run starts at level 1. <see cref="FrontDoor"/> owns the hinges and sounds;
    /// this owns the decision.
    ///
    /// The two routes are tracked separately so that unlocking with the key at level 2 does not
    /// make <see cref="OnLevel"/> announce a second unlock on the way past level 4.
    /// </summary>
    public sealed class FrontDoorState
    {
        public FrontDoorState(int unlockLevel)
        {
            UnlockLevel = unlockLevel < 1 ? 1 : unlockLevel;
        }

        public int UnlockLevel { get; }

        /// <summary>True once the run has reached <see cref="UnlockLevel"/>.</summary>
        public bool LevelUnlocked { get; private set; }

        /// <summary>True once the office key from a desk drawer has been turned in this lock.</summary>
        public bool KeyUsed { get; private set; }

        public bool IsUnlocked => LevelUnlocked || KeyUsed;
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
                LevelUnlocked = false;
                KeyUsed = false;
                IsOpen = false;
                return false;
            }

            if (IsUnlocked || level < UnlockLevel)
            {
                return false;
            }

            LevelUnlocked = true;
            return true;
        }

        /// <summary>
        /// The office key, found in a desk drawer once the servers are back up: it unlocks the
        /// door at any level. Returns true the first time, the moment to celebrate.
        /// </summary>
        public bool UseKey()
        {
            if (KeyUsed)
            {
                return false;
            }

            KeyUsed = true;
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
