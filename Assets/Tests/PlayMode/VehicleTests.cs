using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vent.Core.Damage;
using Vent.Core.Events;
using Vent.Core.Pooling;
using Vent.Core.Services;
using Vent.Core.Utility;
using Vent.Enemies.Data;
using Vent.Enemies.Runtime;
using Vent.Enemies.Spawning;
using Vent.Gameplay.Flow;
using Vent.Gameplay.Vehicles;
using Vent.Player;
using Vent.Vehicles.Runtime;
using Vent.Weapons.Data;
using Vent.Weapons.View;

namespace Vent.Tests.PlayMode
{
    /// <summary>
    /// The cars, end to end in the generated scene: they drive, the player gets in and out cleanly,
    /// the pistol is the only gun in the car, running a zombie over kills it with vehicle credit, a
    /// flipped car rights itself, dying at the wheel exits, and pausing freezes the car.
    /// Input callbacks do not fire in -batchmode, so the tests drive <see cref="VehicleInput"/> directly.
    /// </summary>
    public sealed class VehicleTests
    {
        private BuildingSceneController building;
        private PlayerCharacter player;
        private VehicleDriver driver;
        private VehicleSeat hero;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            GameServices.Clear();
            yield return SceneManager.LoadSceneAsync(SceneNames.Building, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(GameServices.TryGet(out building));
            Assert.IsTrue(GameServices.TryGet(out player));
            Assert.IsTrue(GameServices.TryGet(out IVehicleOccupant occupant), "a VehicleDriver registers itself");
            driver = (VehicleDriver)occupant;
            float best = float.MaxValue;
            foreach (VehicleSeat seat in Object.FindObjectsByType<VehicleSeat>(FindObjectsSortMode.None))
            {
                float d = Vector3.Distance(seat.transform.position, player.Position);
                if (d < best)
                {
                    best = d;
                    hero = seat;
                }
            }

            Assert.IsNotNull(hero, "cars are parked in the district");
            Cursor.lockState = CursorLockMode.None;
        }

        [TearDown]
        public void TearDown() => Time.timeScale = 1f;

        /// <summary>Drop a parked (kinematic) car onto the avenue outside the office, nose north.</summary>
        private static void PlaceOnAvenue(VehicleController car, float z)
        {
            car.transform.SetPositionAndRotation(new Vector3(62f, -0.13f, z), Quaternion.identity);
            Physics.SyncTransforms();
        }

        private static IEnumerator Drive(VehicleController car, float throttle, float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                car.SetInput(new VehicleInput(throttle, 0f, false));
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator CarAcceleratesUnderThrottle()
        {
            VehicleController car = hero.Controller;
            PlaceOnAvenue(car, -60f);
            car.SetOccupied(true);
            Vector3 start = car.transform.position;
            yield return Drive(car, 1f, 2.5f);
            Assert.Greater(car.ForwardSpeed, 3f, "the car moves under its own power");
            Assert.Greater(Vector3.Distance(car.transform.position, start), 4f);
            car.SetOccupied(false);
        }

        [UnityTest]
        public IEnumerator PlayerEntersAndExitsTheCar()
        {
            building.BeginRun();
            yield return null;
            Camera main = Camera.main;
            Transform pivot = player.CameraPivot;
            Assert.IsTrue(driver.TryEnter(hero), "the seat is free");
            yield return null;

            Assert.IsTrue(driver.IsDriving);
            Assert.IsTrue(player.IsSeated);
            Assert.AreSame(hero.Anchor, player.transform.parent, "the player rides the seat");
            Assert.IsFalse(player.GetComponent<CharacterController>().enabled, "the controller is off in the seat");
            Assert.AreNotSame(pivot, main.transform.parent, "the camera is on the chase rig");
            Assert.IsTrue(hero.Controller.IsOccupied);
            Assert.IsTrue(hero.Arm.gameObject.activeSelf, "the arm is out of the window");

            driver.Exit();
            yield return null;
            Assert.IsFalse(driver.IsDriving);
            Assert.IsFalse(player.IsSeated);
            Assert.IsNull(player.transform.parent);
            Assert.IsTrue(player.GetComponent<CharacterController>().enabled);
            Assert.IsTrue(NavMesh.SamplePosition(player.Position, out _, 1.5f, NavMesh.AllAreas), "the player steps out onto the NavMesh");
            Assert.AreSame(pivot, main.transform.parent, "the camera is back on the player");
            Assert.Less(main.transform.localPosition.magnitude, 0.01f, "the camera sits at its rest pose again");
            Assert.IsFalse(hero.Controller.IsOccupied);
            Assert.IsFalse(hero.Arm.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator DriveByUsesThePistolOnly()
        {
            building.BeginRun();
            yield return null;
            Assert.IsTrue(driver.TryEnter(hero));
            yield return new WaitForSeconds(0.6f); // draw time

            var pistol = player.Inventory.Current;
            Assert.AreEqual(WeaponSlot.Secondary, pistol.Definition.Slot, "the pistol is the drive-by gun");
            var viewModel = pistol.GetComponentInChildren<WeaponViewModel>(true);
            Assert.IsNotNull(viewModel);
            Assert.IsFalse(viewModel.gameObject.activeSelf, "the first-person model is hidden; the arm out of the window stands in");

            player.Inventory.SelectSlot(0);
            Assert.AreEqual(WeaponSlot.Secondary, player.Inventory.Current.Definition.Slot, "switching is locked while seated");

            int before = pistol.Magazine;
            player.Inventory.PullTrigger();
            yield return null;
            player.Inventory.ReleaseTrigger();
            yield return new WaitForSeconds(0.2f);
            Assert.Less(pistol.Magazine, before, "the pistol fires from the car");

            driver.Exit();
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(WeaponSlot.Primary, player.Inventory.Current.Definition.Slot, "the SMG comes back out on foot");
            Assert.IsTrue(player.Inventory.Current.GetComponentInChildren<WeaponViewModel>(true).gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator DrivingThroughAZombieKillsItWithVehicleCredit()
        {
            building.BeginRun();
            Object.FindFirstObjectByType<ZombieSpawner>().StopSpawning();
            yield return null;

            VehicleController car = hero.Controller;
            PlaceOnAvenue(car, -70f);

            // A zombie standing on the avenue 25 m ahead, spawned through a throwaway vent so it is
            // on the NavMesh and awake like any other.
            var ventGo = new GameObject("TestVent");
            var grate = new GameObject("Grate").transform;
            grate.SetParent(ventGo.transform, false);
            grate.position = new Vector3(62f, -0.15f, -45f);
            var floor = new GameObject("Floor").transform;
            floor.SetParent(ventGo.transform, false);
            floor.position = new Vector3(62f, -0.15f, -45f);
            var vent = ventGo.AddComponent<AirVent>();
            vent.Configure(grate, floor, null, null);
            var registry = GameServices.Get<PoolRegistry>();
            var zombieDef = Resources.FindObjectsOfTypeAll<ZombieDefinition>()[0];
            var zombie = registry.Spawn<Zombie>(zombieDef.Prefab, vent.GratePosition, Quaternion.identity);
            zombie.Spawn(new ZombieStats(100f, 5f, 0.1f, 25), vent);
            float deadline = Time.time + 4f;
            while (zombie.State == ZombieState.Emerging && Time.time < deadline)
            {
                yield return null;
            }

            Assert.IsTrue(zombie.IsAlive);
            var killChannel = Resources.FindObjectsOfTypeAll<KillEventChannel>()[0];
            KillInfo? received = null;
            killChannel.Subscribe(k => received = k);
            int killsBefore = building.Director.KillsThisLevel;
            int smgLevel = player.Inventory.Weapons[0].Progression.Level;
            int pistolLevel = player.Inventory.Weapons[1].Progression.Level;

            car.SetOccupied(true);
            deadline = Time.time + 8f;
            while (Time.time < deadline && zombie.IsAlive)
            {
                car.SetInput(new VehicleInput(1f, 0f, false));
                yield return null;
            }

            car.SetInput(default);
            Assert.IsFalse(zombie.IsAlive, "the zombie was run over");
            Assert.IsTrue(received.HasValue, "a kill was reported");
            Assert.AreSame(car, received.Value.Killer, "credited to the car");
            Assert.AreEqual(killsBefore + 1, building.Director.KillsThisLevel, "counts toward the level");
            Assert.AreEqual(smgLevel, player.Inventory.Weapons[0].Progression.Level, "no weapon XP for roadkill");
            Assert.AreEqual(pistolLevel, player.Inventory.Weapons[1].Progression.Level);
            car.SetOccupied(false);
            Object.Destroy(ventGo);
        }

        [UnityTest]
        public IEnumerator UpsideDownCarSelfRights()
        {
            VehicleController car = hero.Controller;
            car.transform.SetPositionAndRotation(new Vector3(62f, 1.4f, -60f), Quaternion.Euler(0f, 0f, 180f));
            Physics.SyncTransforms();
            car.SetOccupied(true);
            yield return new WaitForSeconds(4.5f);
            Assert.Greater(Vector3.Dot(car.transform.up, Vector3.up), 0.9f, "back on its wheels");
            car.SetOccupied(false);
        }

        [UnityTest]
        public IEnumerator PlayerDeathWhileDrivingExitsCleanly()
        {
            building.BeginRun();
            yield return null;
            Assert.IsTrue(driver.TryEnter(hero));
            yield return null;
            player.Health.ApplyDamage(new DamageInfo(1_000_000f, DamageKind.Melee, null, player.Position, Vector3.up, Vector3.forward));
            yield return null;
            Assert.IsFalse(player.IsAlive);
            Assert.IsFalse(driver.IsDriving, "death gets you out of the car");
            Assert.IsNull(player.transform.parent);
            Assert.IsTrue(player.GetComponent<CharacterController>().enabled);

            building.EndRun();
            building.BeginRun();
            yield return null;
            Assert.IsTrue(player.IsAlive, "a new run starts on foot at the spawn");
            Assert.IsFalse(player.IsSeated);
        }

        [UnityTest]
        public IEnumerator PauseWhileDrivingFreezesTheCar()
        {
            VehicleController car = hero.Controller;
            PlaceOnAvenue(car, -60f);
            car.SetOccupied(true);
            yield return Drive(car, 1f, 1.5f);
            Assert.Greater(car.ForwardSpeed, 1f);

            Time.timeScale = 0f;
            Vector3 frozen = car.transform.position;
            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }

            Assert.AreEqual(frozen, car.transform.position, "physics stops with the game");
            Time.timeScale = 1f;
            car.SetOccupied(false);
        }
    }
}
