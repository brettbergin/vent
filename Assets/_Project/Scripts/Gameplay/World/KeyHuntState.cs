namespace Vent.Gameplay.World
{
    /// <summary>Where the player has got to in the key hunt. Derived from the state, never stored.</summary>
    public enum KeyHuntStep
    {
        /// <summary>The whiteboard has not been read; the objective line stays hidden.</summary>
        Unaware,
        FindCables,
        PatchPanel,
        FindTerminal,
        UnlockDoor,
        Done,
    }

    /// <summary>What happened when the player used the patch panel.</summary>
    public enum PanelAction
    {
        NotEnoughCables,
        Restored,
        AlreadyRestored,
    }

    /// <summary>What was in the drawer.</summary>
    public enum DrawerAction
    {
        Empty,
        KeyTaken,
    }

    /// <summary>
    /// The alternate way out of the building, engine-free: read the whiteboard, gather patch
    /// cables, plug them into a server rack, and the terminal that comes back up is sitting on
    /// the desk whose drawer holds the front door key.
    ///
    /// The whiteboard is the gate: the coils do not exist until the hint has been read, so the
    /// hunt always starts with the player knowing what they are looking for. Drawers open freely
    /// at any time — that is a satisfying affordance and there is no reason to deny it — but the
    /// key only exists once the power is back, so opening all eighteen drawers before patching
    /// finds nothing. <see cref="KeyHuntDirector"/> owns which desk it is and re-rolls that every
    /// run; this owns the decisions.
    /// </summary>
    public sealed class KeyHuntState
    {
        public KeyHuntState(int cablesRequired)
        {
            CablesRequired = cablesRequired < 1 ? 1 : cablesRequired;
        }

        public int CablesRequired { get; }
        public int CablesHeld { get; private set; }
        public bool HintRead { get; private set; }
        public bool PowerRestored { get; private set; }
        public bool HasKey { get; private set; }

        /// <summary>Set when the door consumes the key, so it is never spent twice.</summary>
        public bool KeySpent { get; private set; }

        public KeyHuntStep Step
        {
            get
            {
                if (KeySpent) return KeyHuntStep.Done;
                if (HasKey) return KeyHuntStep.UnlockDoor;
                if (PowerRestored) return KeyHuntStep.FindTerminal;
                if (CablesHeld >= CablesRequired) return KeyHuntStep.PatchPanel;
                return HintRead ? KeyHuntStep.FindCables : KeyHuntStep.Unaware;
            }
        }

        /// <summary>The HUD objective line for the current step; empty before the hint and after the door.</summary>
        public string Objective => Step switch
        {
            KeyHuntStep.FindCables => $"FIND PATCH CABLES   {CablesHeld} / {CablesRequired}",
            KeyHuntStep.PatchPanel => "PATCH THE SERVER RACK",
            KeyHuntStep.FindTerminal => "FIND THE TERMINAL THAT CAME BACK UP",
            KeyHuntStep.UnlockDoor => "UNLOCK THE FRONT DOOR",
            _ => string.Empty,
        };

        /// <summary>The whiteboard. Returns true the first time, when the objective appears.</summary>
        public bool ReadHint()
        {
            if (HintRead)
            {
                return false;
            }

            HintRead = true;
            return true;
        }

        /// <summary>True once the cables are out to be found: the board has been read.</summary>
        public bool CablesShown => HintRead;

        /// <summary>Pick up one cable. Refused before the hint has been read — the coils are not there yet — and once the set is complete.</summary>
        public bool TakeCable()
        {
            if (!HintRead || CablesHeld >= CablesRequired)
            {
                return false;
            }

            CablesHeld++;
            return true;
        }

        /// <summary>True when the panel would accept the cables right now.</summary>
        public bool CanPatch => !PowerRestored && CablesHeld >= CablesRequired;

        public PanelAction TryRestorePower()
        {
            if (PowerRestored)
            {
                return PanelAction.AlreadyRestored;
            }

            if (CablesHeld < CablesRequired)
            {
                return PanelAction.NotEnoughCables;
            }

            PowerRestored = true;
            return PanelAction.Restored;
        }

        /// <summary>
        /// Open a drawer. The key is only ever in the one desk the director chose, and only once
        /// the power is back.
        /// </summary>
        public DrawerAction TryOpenDrawer(bool isKeyDesk)
        {
            if (!isKeyDesk || !PowerRestored || HasKey)
            {
                return DrawerAction.Empty;
            }

            HasKey = true;
            return DrawerAction.KeyTaken;
        }

        /// <summary>The front door turning the key. True the first time only.</summary>
        public bool SpendKey()
        {
            if (!HasKey || KeySpent)
            {
                return false;
            }

            KeySpent = true;
            return true;
        }

        /// <summary>Back to the start of a run.</summary>
        public void Reset()
        {
            CablesHeld = 0;
            HintRead = false;
            PowerRestored = false;
            HasKey = false;
            KeySpent = false;
        }
    }
}
