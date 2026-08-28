using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vent.Core.Services;
using Vent.Core.Utility;
using Vent.Gameplay.Flow;
using Vent.Gameplay.World;
using Vent.Player;
using Vent.Player.Interaction;

namespace Vent.Tests.PlayMode
{
    /// <summary>
    /// The alternate way out, end to end in the generated scene: read the board, gather the
    /// cables, patch the rack, find the monitor that came back up, take the key out of its drawer
    /// and unlock the front door — at level one, without killing anything.
    ///
    /// The load-bearing test here is <see cref="DifferentRunsMoveTheKey"/>. The building is baked
    /// from a fixed seed, so if the key ever stopped moving between runs the whole feature would
    /// quietly become "walk to the desk you memorised".
    /// </summary>
    public sealed class KeyHuntTests
    {
        private BuildingSceneController building;
        private PlayerCharacter player;
        private KeyHuntDirector hunt;
        private FrontDoor door;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            GameServices.Clear();
            yield return SceneManager.LoadSceneAsync(SceneNames.Building, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(GameServices.TryGet(out building));
            Assert.IsTrue(GameServices.TryGet(out player));
            Assert.IsTrue(GameServices.TryGet(out hunt), "the building scene has a key hunt director");
            door = Object.FindFirstObjectByType<FrontDoor>();
            Assert.IsNotNull(door, "the building has a front door");
            Cursor.lockState = CursorLockMode.None;
            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }
        }

        /// <summary>Start a run with the roll pinned, so a failure is reproducible.</summary>
        private IEnumerator Run(int seed)
        {
            hunt.SeedOverride = seed;
            building.BeginRun();
            yield return null;
        }

        /// <summary>Read the board, then take the three coils it put out. ToArray: CableTaken removes from the live list as it goes.</summary>
        private void TakeEveryCable()
        {
            hunt.Note.Interact();
            foreach (PatchCablePickup cable in new List<PatchCablePickup>(hunt.ActiveCables))
            {
                cable.Interact();
            }
        }

        private static int LitScreens(IReadOnlyList<DeskDrawer> drawers)
        {
            int lit = 0;
            foreach (DeskDrawer drawer in drawers)
            {
                if (drawer.IsScreenLit)
                {
                    lit++;
                }
            }

            return lit;
        }

        /// <summary>Stand in front of something and look straight at it, so the interactor's ray finds it.</summary>
        private IEnumerator Aim(Transform target, Vector3 lookAt, float distance)
        {
            Vector3 stand = target.position + target.forward * distance;
            player.Controller.Teleport(new Vector3(stand.x, 0f, stand.z), Quaternion.identity);
            yield return null;

            UnityEngine.Camera cam = player.GetComponentInChildren<UnityEngine.Camera>();
            Assert.IsNotNull(cam, "the player rig has a camera");
            Vector3 dir = (lookAt - cam.transform.position).normalized;
            // Pitch matters: a drawer front sits at knee height and a whiteboard at head height,
            // so aiming with a flat pitch would miss both.
            player.Look.SetRotation(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg,
                -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg);
            yield return null;
            yield return null;
        }

        // ---------------------------------------------------------------- the roll

        [UnityTest]
        public IEnumerator DifferentRunsMoveTheKey()
        {
            hunt.SeedOverride = 0; // what ships: a fresh roll every run
            var seen = new HashSet<DeskDrawer>();
            for (int i = 0; i < 12; i++)
            {
                building.BeginRun();
                yield return null;
                seen.Add(hunt.KeyDrawer);
            }

            Assert.GreaterOrEqual(seen.Count, 3,
                "the key must move between runs, or the player just walks to the desk they memorised");
        }

        [UnityTest]
        public IEnumerator TheSameSeedReproducesTheSameRoll()
        {
            yield return Run(999);
            DeskDrawer drawer = hunt.KeyDrawer;
            PatchPanel panel = hunt.ActivePanel;
            var cables = new List<PatchCablePickup>(hunt.ActiveCables);

            yield return Run(999);
            Assert.AreSame(drawer, hunt.KeyDrawer);
            Assert.AreSame(panel, hunt.ActivePanel);
            CollectionAssert.AreEqual(cables, hunt.ActiveCables);
        }

        [UnityTest]
        public IEnumerator ARunShowsExactlyTheCablesAndRackItRolled()
        {
            yield return Run(31337);
            Assert.AreEqual(hunt.State.CablesRequired, hunt.ActiveCables.Count, "the full set is always reachable");

            // Issue #3: the coils are rolled at the start but not out until the board has been read.
            foreach (PatchCablePickup cable in hunt.ActiveCables)
            {
                Assert.IsFalse(cable.gameObject.activeInHierarchy, "a rolled coil stays hidden until the whiteboard is read");
                Assert.IsFalse(cable.IsAvailable, "and cannot be taken");
            }

            hunt.Note.Interact();
            var rooms = new HashSet<int>();
            foreach (PatchCablePickup cable in hunt.ActiveCables)
            {
                Assert.IsTrue(cable.gameObject.activeInHierarchy, "reading the board puts the coils out");
                Assert.IsTrue(cable.IsAvailable);
                rooms.Add(cable.Room);
            }

            foreach (PatchCablePickup cable in Object.FindObjectsByType<PatchCablePickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!hunt.ActiveCables.Contains(cable))
                {
                    Assert.IsFalse(cable.gameObject.activeInHierarchy, "only the rolled coils come out");
                }
            }

            Assert.AreEqual(hunt.ActiveCables.Count, rooms.Count, "one coil per room, so three cables are never one shelf");

            int shown = 0;
            foreach (PatchPanel panel in hunt.Panels)
            {
                if (panel.gameObject.activeInHierarchy)
                {
                    shown++;
                }
            }

            Assert.AreEqual(1, shown, "exactly one rack is patchable, so the player looks for a specific one");
            Assert.IsTrue(hunt.ActivePanel.gameObject.activeInHierarchy);
        }

        // ---------------------------------------------------------------- the chain

        [UnityTest]
        public IEnumerator WalkingTheWholeChainUnlocksTheDoorAtLevelOne()
        {
            yield return Run(4242);
            Assert.AreEqual(1, building.Director.Level, "the whole point is that this works before level 4");
            Assert.IsFalse(door.IsUnlocked);

            hunt.Note.Interact();
            Assert.IsTrue(hunt.State.HintRead);
            Assert.AreEqual(KeyHuntStep.FindCables, hunt.State.Step);

            TakeEveryCable();
            Assert.AreEqual(hunt.State.CablesRequired, hunt.State.CablesHeld);
            Assert.AreEqual(KeyHuntStep.PatchPanel, hunt.State.Step);

            hunt.ActivePanel.Interact();
            Assert.IsTrue(hunt.State.PowerRestored);
            Assert.IsTrue(hunt.ActivePanel.IsRestored);
            Assert.AreEqual(1, LitScreens(hunt.Drawers), "exactly one monitor comes back up");
            Assert.IsTrue(hunt.KeyDrawer.IsScreenLit, "and it is the one on the desk holding the key");

            hunt.KeyDrawer.Interact();
            Assert.IsTrue(hunt.State.HasKey);
            Assert.IsTrue(door.HasKey, "the door hears about the key over the channel");

            float deadline = Time.time + 2f;
            while (hunt.KeyDrawer.OpenAmount < 0.6f && Time.time < deadline)
            {
                yield return null;
            }

            Assert.Greater(hunt.KeyDrawer.OpenAmount, 0.6f, "the drawer actually slides — it must not be batching-static");

            door.Interact();
            Assert.IsTrue(door.IsUnlocked, "the key turns the lock at level 1");
            Assert.IsTrue(door.IsOpen, "and one press both unlocks and pushes it open");
            Assert.AreEqual(1, building.Director.Level, "without a single kill");
            Assert.AreEqual(KeyHuntStep.Done, hunt.State.Step);
        }

        [UnityTest]
        public IEnumerator EveryDrawerIsEmptyBeforeThePowerIsBack()
        {
            yield return Run(77);
            hunt.KeyDrawer.Interact();
            Assert.IsFalse(hunt.State.HasKey, "opening all eighteen drawers early must not shortcut the chain");
            Assert.IsFalse(door.HasKey);
        }

        [UnityTest]
        public IEnumerator TheWrongDeskIsEmptyEvenWithThePowerBack()
        {
            yield return Run(88);
            TakeEveryCable();
            hunt.ActivePanel.Interact();
            Assert.IsTrue(hunt.State.PowerRestored);

            DeskDrawer other = null;
            foreach (DeskDrawer drawer in hunt.Drawers)
            {
                if (drawer != hunt.KeyDrawer)
                {
                    other = drawer;
                    break;
                }
            }

            Assert.IsNotNull(other, "the building has more than one desk");
            Assert.IsFalse(other.IsScreenLit, "a desk that is not the one stays dark");
            other.Interact();
            Assert.IsFalse(hunt.State.HasKey);
        }

        [UnityTest]
        public IEnumerator ThePanelRefusesWithoutEnoughCables()
        {
            yield return Run(123);
            hunt.ActiveCables[0].Interact();
            Assert.AreEqual(0, hunt.State.CablesHeld, "a coil that is not out yet cannot be taken, even by name");
            hunt.Note.Interact();
            hunt.ActiveCables[0].Interact();
            Assert.AreEqual(1, hunt.State.CablesHeld);

            // Interact() arrives even when IsAvailable is false; the refusal must not throw.
            Assert.IsFalse(hunt.ActivePanel.IsAvailable);
            hunt.ActivePanel.Interact();
            Assert.IsFalse(hunt.State.PowerRestored);
            Assert.AreEqual(0, LitScreens(hunt.Drawers));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ANewRunResetsTheHunt()
        {
            yield return Run(555);
            hunt.Note.Interact();
            TakeEveryCable();
            hunt.ActivePanel.Interact();
            hunt.KeyDrawer.Interact();
            Assert.IsTrue(hunt.State.HasKey);

            yield return Run(556);
            Assert.IsFalse(hunt.State.HintRead);
            Assert.IsFalse(hunt.State.PowerRestored);
            Assert.IsFalse(hunt.State.HasKey);
            Assert.IsFalse(door.HasKey, "a new run takes the key back");
            Assert.IsFalse(door.IsUnlocked);
            Assert.AreEqual(0, LitScreens(hunt.Drawers), "every monitor is dark again");
            Assert.AreEqual(hunt.State.CablesRequired, hunt.ActiveCables.Count);

            foreach (DeskDrawer drawer in hunt.Drawers)
            {
                Assert.Less(drawer.OpenAmount, 0.05f, "every drawer is shut again");
            }
        }

        // ---------------------------------------------------------------- looking at things

        [UnityTest]
        public IEnumerator LookingAtTheHintBoardOffersTheReadPrompt()
        {
            yield return Run(2024);
            var interactor = player.GetComponent<PlayerInteractor>();
            Assert.IsNotNull(interactor);

            Transform note = hunt.Note.transform;
            yield return Aim(note, note.position + Vector3.up * 1.5f, 1.1f);

            Assert.IsInstanceOf<QuestNote>(interactor.Current, "the board is what the ray finds");
            Assert.IsTrue(interactor.TryInteract());
            Assert.IsTrue(hunt.State.HintRead);
        }

        [UnityTest]
        public IEnumerator LookingAtTheDeskItselfOffersNothing()
        {
            yield return Run(606);
            var interactor = player.GetComponent<PlayerInteractor>();

            // Every desk part is on the Environment layer and PlayerInteractor resolves an
            // interactable by walking *up* from whatever it hit. The drawer component therefore
            // lives on the drawer, not the desk root — otherwise looking anywhere at a 1.6 m desk
            // would offer to open it.
            Transform desk = hunt.KeyDrawer.transform.parent.parent;
            Transform top = desk.Find("Top");
            Assert.IsNotNull(top, "sanity: the desk has a top");

            yield return Aim(desk, top.position, 1.3f);
            Assert.IsNotInstanceOf<DeskDrawer>(interactor.Current, "the desktop is not a drawer");
        }
    }
}
