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
using Vent.Player;
using Vent.Weapons.Runtime;

namespace Vent.Tests.PlayMode
{
    /// <summary>
    /// End-to-end checks against the generated Building scene. These exercise the real prefabs,
    /// NavMesh and event wiring, so they double as a regression suite for the generators.
    /// </summary>
    public sealed class BuildingSceneTests
    {
        private BuildingSceneController building;
        private PlayerCharacter player;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            GameServices.Clear();
            yield return SceneManager.LoadSceneAsync(SceneNames.Building, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(GameServices.TryGet(out building), "BuildingSceneController must register itself");
            Assert.IsTrue(GameServices.TryGet(out player), "PlayerCharacter must register itself");
            Cursor.lockState = CursorLockMode.None;
        }

        [UnityTest]
        public IEnumerator SceneHasVentsPlayerAndNavMesh()
        {
            Assert.IsTrue(GameServices.TryGet(out PoolRegistry _));
            var vents = Object.FindObjectsByType<AirVent>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(vents.Length, 12, "every room has at least one vent");

            foreach (AirVent vent in vents)
            {
                Assert.IsTrue(NavMesh.SamplePosition(vent.FloorPosition, out NavMeshHit hit, 0.75f, NavMesh.AllAreas), $"{vent.name} floor point must be on the NavMesh");
                Assert.Less(Mathf.Abs(hit.position.y), 0.5f);
            }

            Assert.IsTrue(NavMesh.SamplePosition(player.Position, out _, 1f, NavMesh.AllAreas), "player spawn must be on the NavMesh");
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerShovedOutsideIsReturnedToTheBuilding()
        {
            building.BeginRun();
            // Let containment cache a known-good position at the spawn point.
            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }

            // Simulate being pushed clean out of the building WITHOUT going through Teleport
            // (which would record the outside position as safe). Move the controller directly.
            var controller = player.GetComponent<CharacterController>();
            var outside = new Vector3(500f, 0f, 500f);
            controller.enabled = false;
            player.transform.position = outside;
            controller.enabled = true;

            Assert.IsFalse(NavMesh.SamplePosition(outside, out _, 3f, NavMesh.AllAreas), "sanity: the chosen point is outside the NavMesh");

            // Containment runs each frame after movement; a few frames must bring the player home.
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.IsTrue(NavMesh.SamplePosition(player.Position, out _, 2f, NavMesh.AllAreas),
                "player must be returned onto the NavMesh after being pushed outside");
            // The building is a ~40x30 m footprint centred on the origin; being back within it
            // (and far from the 500,500 breach point) proves containment pulled the player home.
            Vector3 planar = new Vector3(player.Position.x, 0f, player.Position.z);
            Assert.Less(planar.magnitude, 40f, "player must be returned inside the building footprint");
            Assert.Greater(Vector3.Distance(player.Position, outside), 400f, "player must no longer be at the outside position");
            Assert.IsTrue(player.Health.IsAlive, "player should survive being briefly outside");
        }

        [UnityTest]
        public IEnumerator PlayerCannotLeaveTheBuilding()
        {
            // From the player's eye, a ray in any horizontal direction must hit Environment (a wall) within the building's extents.
            Vector3 eye = player.AimPoint;
            int mask = LayerMask.GetMask(Layers.Environment);
            for (int angle = 0; angle < 360; angle += 15)
            {
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Assert.IsTrue(Physics.Raycast(eye, dir, 80f, mask), $"open horizon at {angle}°");
            }

            Assert.IsTrue(Physics.Raycast(eye, Vector3.up, 10f, mask), "no ceiling above the player");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ZombieEmergesChasesAndDiesWithKillCredit()
        {
            building.BeginRun();
            var spawner = Object.FindFirstObjectByType<ZombieSpawner>();
            spawner.StopSpawning(); // we drive the spawn manually
            var vents = Object.FindObjectsByType<AirVent>(FindObjectsSortMode.None);
            AirVent vent = vents[0];

            var registry = GameServices.Get<PoolRegistry>();
            var zombieDef = Resources.FindObjectsOfTypeAll<ZombieDefinition>()[0];
            var zombie = registry.Spawn<Zombie>(zombieDef.Prefab, vent.GratePosition, Quaternion.identity);
            zombie.Spawn(new ZombieStats(50f, 5f, 3f, 25), vent);
            Assert.AreEqual(ZombieState.Emerging, zombie.State);

            float deadline = Time.time + 5f;
            while (zombie.State == ZombieState.Emerging && Time.time < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(ZombieState.Chasing, zombie.State, "zombie should start chasing after emerging");
            Assert.IsTrue(zombie.IsAlive);

            var killChannel = Resources.FindObjectsOfTypeAll<KillEventChannel>()[0];
            KillInfo? received = null;
            killChannel.Subscribe(k => received = k);

            var fakeWeapon = new GameObject("FakeKiller");
            DamageResult result = zombie.ApplyDamage(new DamageInfo(60f, DamageKind.Bullet, fakeWeapon, zombie.transform.position, Vector3.up, Vector3.forward, headshot: true));
            Assert.IsTrue(result.Killed);
            Assert.AreEqual(ZombieState.Dead, zombie.State);
            Assert.IsTrue(received.HasValue, "kill event raised");
            Assert.AreSame(fakeWeapon, received.Value.Killer);
            Assert.IsTrue(received.Value.Headshot);
            Assert.AreEqual(25, received.Value.Experience);
            Object.Destroy(fakeWeapon);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FiringConsumesAmmoAndKillsGrantWeaponExperience()
        {
            building.BeginRun();
            Object.FindFirstObjectByType<ZombieSpawner>().StopSpawning();
            yield return null;

            Weapon weapon = player.Inventory.Current;
            Assert.IsNotNull(weapon);
            float deadline = Time.time + 2f;
            while (weapon.State != WeaponState.Ready && Time.time < deadline)
            {
                yield return null;
            }

            int before = weapon.Magazine;
            weapon.PullTrigger();
            yield return null;
            yield return null;
            weapon.ReleaseTrigger();
            Assert.Less(weapon.Magazine, before, "trigger pull fires at least one round");

            var killChannel = Resources.FindObjectsOfTypeAll<KillEventChannel>()[0];
            killChannel.Raise(new KillInfo(Vector3.zero, weapon, false, 1_000));
            Assert.Greater(weapon.Progression.Level, 1, "experience credited to the killing weapon levels it up");
        }

        [UnityTest]
        public IEnumerator KillsAdvanceTheLevelAndRefillAmmo()
        {
            building.BeginRun();
            Object.FindFirstObjectByType<ZombieSpawner>().StopSpawning();
            yield return null;

            var levelChannel = Resources.FindObjectsOfTypeAll<LevelEventChannel>()[0];
            var killChannel = Resources.FindObjectsOfTypeAll<KillEventChannel>()[0];
            int lastLevel = 0;
            levelChannel.Subscribe(l => lastLevel = l.Level);

            int required = building.Director.KillsRequired;
            Assert.Greater(required, 0);

            Weapon weapon = player.Inventory.Current;
            float ready = Time.time + 2f;
            while (weapon.State != WeaponState.Ready && Time.time < ready)
            {
                yield return null; // the draw animation must finish before the trigger does anything
            }

            weapon.PullTrigger(); // fire a few rounds so a refill is observable
            yield return new WaitForSeconds(0.3f);
            weapon.ReleaseTrigger();
            Assert.Less(weapon.Magazine, weapon.Stats.MagazineSize);

            for (int i = 0; i < required; i++)
            {
                killChannel.Raise(new KillInfo(Vector3.zero, null, false, 1));
            }

            Assert.AreEqual(2, lastLevel);
            Assert.AreEqual(2, building.Director.Level);
            Assert.AreEqual(weapon.Stats.MagazineSize, weapon.Magazine, "level-up refills the magazine");
        }
    }
}
