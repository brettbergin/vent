using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vent.Core.Events;
using Vent.Core.Pooling;
using Vent.Core.Services;
using Vent.Core.Utility;
using Vent.Enemies.Data;
using Vent.Enemies.Runtime;
using Vent.Enemies.Spawning;
using Vent.Gameplay.Flow;
using Vent.Gameplay.World;
using Vent.Player;
using Vent.Player.Interaction;

namespace Vent.Tests.PlayMode
{
    /// <summary>
    /// The level-4 front door and the street beyond it, end to end in the generated scene: locked
    /// until the level says otherwise, opened by the Interact key, walkable and contained outside,
    /// zombies pathing through only once it is open, outdoor spawns waking with it, and the fog
    /// lifting as the player steps out.
    /// </summary>
    public sealed class FrontDoorTests
    {
        private BuildingSceneController building;
        private PlayerCharacter player;
        private FrontDoor door;
        private LevelEventChannel level;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            GameServices.Clear();
            yield return SceneManager.LoadSceneAsync(SceneNames.Building, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(GameServices.TryGet(out building));
            Assert.IsTrue(GameServices.TryGet(out player));
            door = Object.FindFirstObjectByType<FrontDoor>();
            Assert.IsNotNull(door, "the building has a front door");
            level = Resources.FindObjectsOfTypeAll<LevelEventChannel>()[0];
            Cursor.lockState = CursorLockMode.None;
            // Let the door's NavMesh obstacle carve before anything asks about paths.
            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }
        }

        private IEnumerator UnlockAndOpen()
        {
            level.Raise(new LevelInfo(door.UnlockLevel, 15));
            yield return null;
            door.Interact();
            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }

            Assert.IsTrue(door.IsOpen, "sanity: the door opened");
        }

        private void PutPlayerAt(Vector3 position, float yaw)
        {
            player.Controller.Teleport(position, Quaternion.Euler(0f, yaw, 0f));
            player.Look.SetRotation(yaw, 0f);
        }

        [UnityTest]
        public IEnumerator DoorIsLockedUntilLevelFourThenOpensOnInteract()
        {
            Assert.IsFalse(door.IsUnlocked);
            door.Interact();
            yield return null;
            Assert.IsFalse(door.IsOpen, "a locked door only rattles");

            level.Raise(new LevelInfo(door.UnlockLevel - 1, 12));
            yield return null;
            Assert.IsFalse(door.IsUnlocked, "one level short");

            level.Raise(new LevelInfo(door.UnlockLevel, 15));
            yield return null;
            Assert.IsTrue(door.IsUnlocked);
            Assert.IsFalse(door.IsOpen, "unlocked is not open");

            PutPlayerAt(new Vector3(28f, 0f, 0f), 90f);
            yield return null;
            yield return null;
            var interactor = player.GetComponent<PlayerInteractor>();
            Assert.IsNotNull(interactor);
            Assert.IsTrue(interactor.TryInteract(), "the door is within reach and in view");
            Assert.IsTrue(door.IsOpen);

            float deadline = Time.time + 2f;
            while (door.OpenAmount < 0.6f && Time.time < deadline)
            {
                yield return null;
            }

            Assert.Greater(door.OpenAmount, 0.6f, "the leaves swing open");
        }

        [UnityTest]
        public IEnumerator InteractPromptShowsLockedTextThenTheOpenHint()
        {
            StringEventChannel prompt = null;
            foreach (StringEventChannel channel in Resources.FindObjectsOfTypeAll<StringEventChannel>())
            {
                if (channel.name == "Evt_Prompt")
                {
                    prompt = channel;
                }
            }

            Assert.IsNotNull(prompt, "Evt_Prompt exists");
            string last = null;
            prompt.Subscribe(s => last = s);

            PutPlayerAt(new Vector3(28f, 0f, 0f), 90f);
            yield return null;
            yield return null;
            Assert.IsNotNull(last);
            StringAssert.Contains("LOCKED", last);

            level.Raise(new LevelInfo(door.UnlockLevel, 15));
            yield return null;
            yield return null;
            StringAssert.Contains("OPEN", last);
            StringAssert.DoesNotContain("LOCKED", last);
        }

        [UnityTest]
        public IEnumerator ZombiesPathThroughTheOpenDoorOnly()
        {
            var inside = new Vector3(22.5f, 0f, 0f);
            var street = new Vector3(40f, 0f, 0f);
            var path = new NavMeshPath();
            Assert.IsTrue(NavMesh.SamplePosition(street, out NavMeshHit hit, 1f, NavMesh.AllAreas), "the front lot is on the NavMesh");
            NavMesh.CalculatePath(inside, hit.position, NavMesh.AllAreas, path);
            Assert.AreNotEqual(NavMeshPathStatus.PathComplete, path.status, "a shut door carves the doorway");

            yield return UnlockAndOpen();
            NavMesh.CalculatePath(inside, hit.position, NavMesh.AllAreas, path);
            Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, "an open door lets zombies through");
        }

        [UnityTest]
        public IEnumerator PlayerCanWalkOutToTheStreetAndContainmentKeepsThemThere()
        {
            yield return UnlockAndOpen();
            building.BeginRun();
            var street = new Vector3(40f, 0f, 0f);
            PutPlayerAt(street, 90f);
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.Less(Vector3.Distance(new Vector3(player.Position.x, 0f, player.Position.z), street), 3f, "standing on the street is allowed");
            Assert.IsTrue(NavMesh.SamplePosition(player.Position, out _, 1f, NavMesh.AllAreas));

            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = new Vector3(500f, 0f, 500f);
            controller.enabled = true;
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.Less(Vector3.Distance(new Vector3(player.Position.x, 0f, player.Position.z), street), 3f, "containment returns the player to where they last stood");
        }

        [UnityTest]
        public IEnumerator ExteriorVentsActivateOnOpenAndSitOnTheNavMesh()
        {
            int before = Object.FindObjectsByType<AirVent>(FindObjectsSortMode.None).Length;
            yield return UnlockAndOpen();
            var vents = Object.FindObjectsByType<AirVent>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(vents.Length, before + 28, "the manholes wake up with the door");
            foreach (AirVent vent in vents)
            {
                Assert.IsTrue(NavMesh.SamplePosition(vent.FloorPosition, out NavMeshHit hit, 0.75f, NavMesh.AllAreas), $"{vent.name} floor point must be on the NavMesh");
                Assert.Less(Mathf.Abs(hit.position.y), 0.5f);
            }
        }

        [UnityTest]
        public IEnumerator ZombieChasesThePlayerOutdoors()
        {
            yield return UnlockAndOpen();
            building.BeginRun();
            Object.FindFirstObjectByType<ZombieSpawner>().StopSpawning();
            PutPlayerAt(new Vector3(44f, 0f, 0f), 90f);
            yield return null;

            AirVent nearest = null;
            float best = float.MaxValue;
            foreach (AirVent vent in Object.FindObjectsByType<AirVent>(FindObjectsSortMode.None))
            {
                float d = Vector3.Distance(vent.FloorPosition, player.Position);
                if (vent.FloorPosition.x > 31f && d < best)
                {
                    best = d;
                    nearest = vent;
                }
            }

            Assert.IsNotNull(nearest, "an outdoor vent near the front lot");
            var registry = GameServices.Get<PoolRegistry>();
            var zombieDef = Resources.FindObjectsOfTypeAll<ZombieDefinition>()[0];
            var zombie = registry.Spawn<Zombie>(zombieDef.Prefab, nearest.GratePosition, Quaternion.identity);
            zombie.Spawn(new ZombieStats(50f, 5f, 3.4f, 25), nearest);

            float deadline = Time.time + 12f;
            while (Time.time < deadline && Vector3.Distance(zombie.transform.position, player.Position) > 6f)
            {
                yield return null;
            }

            Assert.Less(Vector3.Distance(zombie.transform.position, player.Position), 6f, "the zombie climbs out and closes in on the street");
        }

        [UnityTest]
        public IEnumerator AtmosphereBlendsWhenSteppingOutside()
        {
            yield return null;
            Assert.AreEqual(0.018f, RenderSettings.fogDensity, 0.002f, "indoor haze at spawn");

            yield return UnlockAndOpen();
            PutPlayerAt(new Vector3(44f, 0f, 0f), 90f);
            yield return new WaitForSeconds(2.5f);
            Assert.Less(RenderSettings.fogDensity, 0.006f, "the haze lifts outside");

            PutPlayerAt(new Vector3(22.5f, 0f, 0f), 90f);
            yield return new WaitForSeconds(2.5f);
            Assert.Greater(RenderSettings.fogDensity, 0.015f, "and settles back indoors");
        }
    }
}
