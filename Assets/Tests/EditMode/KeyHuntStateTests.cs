using NUnit.Framework;
using Vent.Gameplay.World;

namespace Vent.Tests.EditMode
{
    /// <summary>
    /// The key hunt's rule in isolation: read the board, gather the cables, patch the rack, and
    /// find the one drawer that now has a key in it. The interesting cases are the refusals —
    /// patching early, opening every drawer early, opening the wrong drawer at all.
    /// </summary>
    public sealed class KeyHuntStateTests
    {
        private static KeyHuntState Gathered(int required = 3)
        {
            var hunt = new KeyHuntState(required);
            for (int i = 0; i < required; i++)
            {
                hunt.TakeCable();
            }

            return hunt;
        }

        private static KeyHuntState Powered(int required = 3)
        {
            KeyHuntState hunt = Gathered(required);
            hunt.TryRestorePower();
            return hunt;
        }

        [Test]
        public void ACablesRequirementBelowOneIsClamped()
        {
            Assert.AreEqual(1, new KeyHuntState(0).CablesRequired);
            Assert.AreEqual(1, new KeyHuntState(-5).CablesRequired);
        }

        [Test]
        public void TheHintIsReportedOnceAndStartsTheObjective()
        {
            var hunt = new KeyHuntState(3);
            Assert.AreEqual(KeyHuntStep.Unaware, hunt.Step);
            Assert.AreEqual(string.Empty, hunt.Objective, "nothing on the HUD until the player reads the board");

            Assert.IsTrue(hunt.ReadHint());
            Assert.IsFalse(hunt.ReadHint(), "re-reading the board is not a second discovery");
            Assert.AreEqual(KeyHuntStep.FindCables, hunt.Step);
            Assert.AreEqual("FIND PATCH CABLES   0 / 3", hunt.Objective);
        }

        [Test]
        public void CablesCountUpAndStopAtTheRequirement()
        {
            var hunt = new KeyHuntState(3);
            Assert.IsTrue(hunt.TakeCable());
            Assert.IsTrue(hunt.TakeCable());
            Assert.AreEqual("FIND PATCH CABLES   2 / 3", hunt.Objective);
            Assert.IsTrue(hunt.TakeCable());
            Assert.IsFalse(hunt.TakeCable(), "a fourth cable is not collectable");
            Assert.AreEqual(3, hunt.CablesHeld);
        }

        [Test]
        public void TakingACableWithoutReadingTheBoardStillCounts()
        {
            var hunt = new KeyHuntState(3);
            Assert.IsTrue(hunt.TakeCable(), "the whiteboard is a hint, never a gate");
            Assert.IsTrue(hunt.HintRead, "and the objective catches up on its own");
            Assert.AreEqual(KeyHuntStep.FindCables, hunt.Step);
        }

        [Test]
        public void ThePanelRefusesUntilEveryCableIsHeld()
        {
            var hunt = new KeyHuntState(3);
            hunt.TakeCable();
            Assert.IsFalse(hunt.CanPatch);
            Assert.AreEqual(PanelAction.NotEnoughCables, hunt.TryRestorePower());
            Assert.IsFalse(hunt.PowerRestored);

            hunt.TakeCable();
            hunt.TakeCable();
            Assert.IsTrue(hunt.CanPatch);
            Assert.AreEqual(KeyHuntStep.PatchPanel, hunt.Step);
            Assert.AreEqual("PATCH THE SERVER RACK", hunt.Objective);
        }

        [Test]
        public void PowerIsRestoredOnceAndThenTheTerminalIsTheObjective()
        {
            KeyHuntState hunt = Gathered();
            Assert.AreEqual(PanelAction.Restored, hunt.TryRestorePower());
            Assert.AreEqual(PanelAction.AlreadyRestored, hunt.TryRestorePower());
            Assert.IsTrue(hunt.PowerRestored);
            Assert.IsFalse(hunt.CanPatch);
            Assert.AreEqual(KeyHuntStep.FindTerminal, hunt.Step);
            Assert.AreEqual("FIND THE TERMINAL THAT CAME BACK UP", hunt.Objective);
        }

        [Test]
        public void EveryDrawerIsEmptyBeforeThePowerIsBack()
        {
            KeyHuntState hunt = Gathered();
            Assert.AreEqual(DrawerAction.Empty, hunt.TryOpenDrawer(isKeyDesk: true),
                "brute-forcing the drawers before patching finds nothing");
            Assert.IsFalse(hunt.HasKey);
        }

        [Test]
        public void TheWrongDeskIsAlwaysEmpty()
        {
            KeyHuntState hunt = Powered();
            Assert.AreEqual(DrawerAction.Empty, hunt.TryOpenDrawer(isKeyDesk: false));
            Assert.IsFalse(hunt.HasKey);
        }

        [Test]
        public void TheKeyDeskYieldsTheKeyExactlyOnce()
        {
            KeyHuntState hunt = Powered();
            Assert.AreEqual(DrawerAction.KeyTaken, hunt.TryOpenDrawer(isKeyDesk: true));
            Assert.AreEqual(DrawerAction.Empty, hunt.TryOpenDrawer(isKeyDesk: true), "the drawer is empty now");
            Assert.IsTrue(hunt.HasKey);
            Assert.AreEqual(KeyHuntStep.UnlockDoor, hunt.Step);
            Assert.AreEqual("UNLOCK THE FRONT DOOR", hunt.Objective);
        }

        [Test]
        public void TheKeyIsSpentOnceAndTheObjectiveClears()
        {
            KeyHuntState hunt = Powered();
            Assert.IsFalse(hunt.SpendKey(), "there is no key to spend yet");

            hunt.TryOpenDrawer(isKeyDesk: true);
            Assert.IsTrue(hunt.SpendKey());
            Assert.IsFalse(hunt.SpendKey(), "the door cannot be unlocked twice by one key");
            Assert.AreEqual(KeyHuntStep.Done, hunt.Step);
            Assert.AreEqual(string.Empty, hunt.Objective);
        }

        [Test]
        public void ANewRunStartsTheHuntOver()
        {
            KeyHuntState hunt = Powered();
            hunt.TryOpenDrawer(isKeyDesk: true);
            hunt.SpendKey();

            hunt.Reset();
            Assert.AreEqual(0, hunt.CablesHeld);
            Assert.IsFalse(hunt.HintRead);
            Assert.IsFalse(hunt.PowerRestored);
            Assert.IsFalse(hunt.HasKey);
            Assert.IsFalse(hunt.KeySpent);
            Assert.AreEqual(KeyHuntStep.Unaware, hunt.Step);

            // ...and the whole chain walks again exactly as it did the first time.
            Assert.IsTrue(hunt.ReadHint());
            hunt.TakeCable();
            hunt.TakeCable();
            hunt.TakeCable();
            Assert.AreEqual(PanelAction.Restored, hunt.TryRestorePower());
            Assert.AreEqual(DrawerAction.KeyTaken, hunt.TryOpenDrawer(isKeyDesk: true));
            Assert.IsTrue(hunt.SpendKey());
        }
    }
}
