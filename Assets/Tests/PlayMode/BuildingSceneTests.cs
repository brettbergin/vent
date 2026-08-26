using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vent.Core.Data;
using Vent.Core.Damage;
using Vent.Core.Events;
using Vent.Core.Perks;
using Vent.Gameplay.Perks;
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
        public IEnumerator PlayerStartsInASealedLobbyBehindTheFrontDoor()
        {
            // From the player's eye, a ray in any horizontal direction must hit Environment (a wall, or
            // the closed front door) within the building's extents: the run starts sealed in.
            Vector3 eye = player.AimPoint;
            int mask = LayerMask.GetMask(Layers.Environment);
            bool sawDoor = false;
            for (int angle = 0; angle < 360; angle += 15)
            {
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Assert.IsTrue(Physics.Raycast(eye, dir, out RaycastHit hit, 80f, mask, QueryTriggerInteraction.Ignore), $"open horizon at {angle}°");
                sawDoor |= hit.collider.GetComponentInParent<Vent.Gameplay.World.FrontDoor>() != null;
            }

            Assert.IsTrue(sawDoor, "the lobby's outer wall carries the closed front door");
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
            Assert.AreEqual(weapon.Capacity, weapon.Magazine, "level-up refills the magazine, plus one chambered");
        }

        [UnityTest]
        public IEnumerator RunStartsWithAGracePeriodBeforeTheFirstZombie()
        {
            var spawner = Object.FindFirstObjectByType<ZombieSpawner>();
            building.BeginRun();
            yield return null;

            Assert.Greater(spawner.SecondsUntilNextSpawn, 1f, "the spawner should be holding at run start");
            float until = Time.time + 1.5f;
            while (Time.time < until)
            {
                Assert.AreEqual(0, spawner.AliveCount, "no zombie may appear during the run-start grace");
                yield return null;
            }

            Assert.Greater(spawner.SecondsUntilNextSpawn, 0f, "still inside the grace period after 1.5 s");
        }

        [UnityTest]
        public IEnumerator AnnoyedZombieWandersUntilItHearsAGunshot()
        {
            building.BeginRun();
            var spawner = Object.FindFirstObjectByType<ZombieSpawner>();
            spawner.StopSpawning();

            // The vent farthest from the player: well outside a level-1 zombie's notice radius.
            AirVent vent = null;
            float farthest = 0f;
            foreach (AirVent v in Object.FindObjectsByType<AirVent>(FindObjectsSortMode.None))
            {
                float d = Vector3.Distance(v.FloorPosition, player.Position);
                if (d > farthest) { farthest = d; vent = v; }
            }

            var zombieDef = Resources.FindObjectsOfTypeAll<ZombieDefinition>()[0];
            var difficulty = Resources.FindObjectsOfTypeAll<DifficultyProfile>()[0];
            ZombieStats annoyed = ZombieStats.From(zombieDef, difficulty.Evaluate(1));
            Assert.Greater(farthest, annoyed.NoticeRadius, "sanity: the chosen vent is beyond notice range");

            var zombie = GameServices.Get<PoolRegistry>().Spawn<Zombie>(zombieDef.Prefab, vent.GratePosition, Quaternion.identity);
            zombie.Spawn(annoyed, vent);

            float deadline = Time.time + 5f;
            while (zombie.State == ZombieState.Emerging && Time.time < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(ZombieState.Wandering, zombie.State, "a level-1 zombie that cannot see the player wanders");
            Assert.IsFalse(zombie.IsAlerted);

            // A shot right next to it is within any hearing radius.
            Resources.FindObjectsOfTypeAll<NoiseEventChannel>()[0].Raise(new NoiseInfo(zombie.transform.position));
            yield return null;

            Assert.IsTrue(zombie.IsAlerted);
            Assert.AreEqual(ZombieState.Chasing, zombie.State, "hearing gunfire turns a wanderer into a chaser");
        }

        [UnityTest]
        public IEnumerator TacticalReloadKeepsOneInTheChamberAndEmptyReloadDoesNot()
        {
            building.BeginRun();
            Object.FindFirstObjectByType<ZombieSpawner>().StopSpawning();
            Weapon weapon = player.Inventory.Current;
            float deadline = Time.time + 2f;
            while (weapon.State != WeaponState.Ready && Time.time < deadline)
            {
                yield return null;
            }

            int magazine = weapon.Stats.MagazineSize;
            Assert.AreEqual(magazine + 1, weapon.Magazine, "a fresh gun carries a full magazine plus one chambered");

            // Fire one round, then a tactical reload: back to magazine + 1.
            weapon.PullTrigger();
            yield return null;
            weapon.ReleaseTrigger();
            Assert.Less(weapon.Magazine, magazine + 1);
            Assert.IsTrue(weapon.TryReload());
            deadline = Time.time + weapon.Stats.ReloadSeconds + 1f;
            while (weapon.State == WeaponState.Reloading && Time.time < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(magazine + 1, weapon.Magazine, "tactical reload keeps the chambered round");

            // Empty reload: only a full magazine, and it takes longer.
            Assert.Greater(weapon.Stats.EmptyReloadSeconds, weapon.Stats.ReloadSeconds);
            weapon.RefillAmmo();
            weapon.PullTrigger();
            deadline = Time.time + 6f;
            while (weapon.Magazine > 0 && Time.time < deadline)
            {
                yield return null;
            }

            weapon.ReleaseTrigger();
            Assert.AreEqual(0, weapon.Magazine, "held trigger runs the SMG dry");
            Assert.IsTrue(weapon.TryReload(), "R on an empty gun starts the (slower) empty reload");
            deadline = Time.time + weapon.Stats.EmptyReloadSeconds + 1f;
            while (weapon.State == WeaponState.Reloading && Time.time < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(magazine, weapon.Magazine, "empty reload has nothing to chamber beyond the magazine");
        }

        [UnityTest]
        public IEnumerator HeavyHitStaggersAndHealthBarTracksDamage()
        {
            building.BeginRun();
            Object.FindFirstObjectByType<ZombieSpawner>().StopSpawning();
            // The nearest vent: the bar only updates while it is within the camera's fade distance.
            AirVent vent = null;
            float best = float.MaxValue;
            foreach (AirVent candidate in Object.FindObjectsByType<AirVent>(FindObjectsSortMode.None))
            {
                float d = Vector3.Distance(candidate.FloorPosition, player.Position);
                if (d < best)
                {
                    best = d;
                    vent = candidate;
                }
            }

            var zombieDef = Resources.FindObjectsOfTypeAll<ZombieDefinition>()[0];
            var zombie = GameServices.Get<PoolRegistry>().Spawn<Zombie>(zombieDef.Prefab, vent.GratePosition, Quaternion.identity);
            zombie.Spawn(new ZombieStats(100f, 5f, 3f, 25), vent);

            float deadline = Time.time + 5f;
            while (zombie.State == ZombieState.Emerging && Time.time < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(ZombieState.Chasing, zombie.State);

            // A light body hit only flinches; a heavy one staggers.
            var weapon = new GameObject("FakeGun");
            zombie.ApplyDamage(new DamageInfo(10f, DamageKind.Bullet, weapon, zombie.transform.position, Vector3.up, Vector3.forward));
            Assert.AreEqual(ZombieState.Chasing, zombie.State, "10% is a flinch, not a stagger");
            zombie.ApplyDamage(new DamageInfo(40f, DamageKind.Bullet, weapon, zombie.transform.position, Vector3.up, Vector3.forward));
            Assert.AreEqual(ZombieState.Staggered, zombie.State, "40% in one hit staggers");
            Assert.AreEqual(0.5f, zombie.HealthNormalized, 1e-4f);

            yield return null;
            yield return null;
            Transform fill = zombie.GetComponentInChildren<ZombieHealthBar>().transform.Find("Fill");
            Assert.IsNotNull(fill);
            Assert.AreEqual(0.5f, fill.localScale.x / 0.58f, 0.02f, "fill width follows health");

            deadline = Time.time + zombieDef.StaggerSeconds + 1f;
            while (zombie.State == ZombieState.Staggered && Time.time < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(ZombieState.Chasing, zombie.State, "stagger ends and the chase resumes");
            Object.Destroy(weapon);
        }

        [UnityTest]
        public IEnumerator NukePerkKillsEveryZombieAndCountsTheKills()
        {
            building.BeginRun();
            Object.FindFirstObjectByType<ZombieSpawner>().StopSpawning();
            var vents = Object.FindObjectsByType<AirVent>(FindObjectsSortMode.None);
            var zombieDef = Resources.FindObjectsOfTypeAll<ZombieDefinition>()[0];
            var pools = GameServices.Get<PoolRegistry>();
            var zombies = new Zombie[2];
            for (int i = 0; i < zombies.Length; i++)
            {
                zombies[i] = pools.Spawn<Zombie>(zombieDef.Prefab, vents[i].GratePosition, Quaternion.identity);
                zombies[i].Spawn(new ZombieStats(100f, 5f, 3f, 25), vents[i]);
            }

            float deadline = Time.time + 5f;
            while ((zombies[0].State == ZombieState.Emerging || zombies[1].State == ZombieState.Emerging) && Time.time < deadline)
            {
                yield return null;
            }

            int killsBefore = building.Director.KillsThisLevel;
            var perks = GameServices.Get<PerkSystem>();
            int onFloorBefore = perks.LiveCount;

            Resources.FindObjectsOfTypeAll<PerkEventChannel>()[0].Raise(new PerkInfo(PerkKind.Nuke, 0f));
            yield return null;

            foreach (Zombie z in zombies)
            {
                Assert.IsFalse(z.IsAlive, "the nuke kills every zombie");
            }

            Assert.AreEqual(killsBefore + 2, building.Director.KillsThisLevel, "nuke kills count toward the level");
            Assert.AreEqual(onFloorBefore, perks.LiveCount, "nuke kills never drop perks");
        }

        [UnityTest]
        public IEnumerator PerkOrbIsCollectedByWalkingIntoItAndAppliesItsEffect()
        {
            building.BeginRun();
            Object.FindFirstObjectByType<ZombieSpawner>().StopSpawning();
            var perks = GameServices.Get<PerkSystem>();
            PerkEventChannel channel = Resources.FindObjectsOfTypeAll<PerkEventChannel>()[0];

            PerkInfo? collected = null;
            void OnPerk(PerkInfo p) => collected = p;
            channel.Subscribe(OnPerk);
            try
            {
                Vector3 spot = player.Position + player.transform.forward * 3f;
                PerkPickup orb = perks.Drop(new PerkInfo(PerkKind.Invulnerable, 8f), spot);
                Assert.IsNotNull(orb);
                Assert.IsTrue(orb.IsLive);
                Assert.AreEqual(1, perks.LiveCount);
                yield return null;
                Assert.IsNull(collected, "an orb three metres away is not collected");

                player.Controller.Teleport(new Vector3(orb.transform.position.x, player.Position.y, orb.transform.position.z), player.transform.rotation);
                float deadline = Time.time + 2f;
                while (collected == null && Time.time < deadline)
                {
                    yield return null;
                }

                Assert.IsTrue(collected.HasValue, "walking into the orb collects it");
                Assert.AreEqual(PerkKind.Invulnerable, collected.Value.Kind);
                Assert.IsFalse(orb.IsLive);
                Assert.AreEqual(0, perks.LiveCount);

                Assert.IsTrue(player.Health.IsInvulnerable, "the Invulnerable perk applies to the player");
                DamageResult hit = player.Health.ApplyDamage(new DamageInfo(30f, DamageKind.Melee, null, player.AimPoint, Vector3.back, Vector3.forward));
                Assert.IsTrue(hit.Ignored);
                Assert.AreEqual(player.Health.Max, player.Health.Current);
            }
            finally
            {
                channel.Unsubscribe(OnPerk);
            }
        }

        [UnityTest]
        public IEnumerator OneShotAndInstantReloadPerksApplyToTheWeapons()
        {
            building.BeginRun();
            Object.FindFirstObjectByType<ZombieSpawner>().StopSpawning();
            PerkEventChannel channel = Resources.FindObjectsOfTypeAll<PerkEventChannel>()[0];
            Weapon gun = player.Inventory.Current;
            yield return null;

            channel.Raise(new PerkInfo(PerkKind.OneShot, 5f));
            foreach (Weapon w in player.Inventory.Weapons)
            {
                Assert.IsTrue(w.OneShotActive, $"{w.Definition.DisplayName} gets One Shot");
            }

            // Fire until the magazine is short, then the perk refills it at once.
            player.SetControlsEnabled(true);
            int full = gun.Capacity;
            float deadline = Time.time + 3f;
            while (gun.Magazine == full && Time.time < deadline)
            {
                gun.PullTrigger();
                yield return null;
            }

            gun.ReleaseTrigger();
            Assert.Less(gun.Magazine, full, "a shot left the magazine short");
            channel.Raise(new PerkInfo(PerkKind.InstantReload, 0f));
            Assert.AreEqual(full, gun.Magazine, "Instant Reload fills the magazine immediately");
        }
    }
}
