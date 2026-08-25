using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vent.Core.Events;
using Vent.Core.Perks;
using Vent.Core.Pooling;
using Vent.Core.Services;
using Vent.Core.Utility;
using Vent.Enemies.Data;
using Vent.Enemies.Runtime;
using Vent.Enemies.Spawning;
using Vent.Gameplay.Flow;
using Vent.Gameplay.Perks;
using Vent.Player;

namespace Vent.Tests.PlayMode
{
    /// <summary>
    /// Runs the building at full tilt for a while: spawns pouring out, the gun firing, perks dropping and
    /// being collected, nukes going off, levels advancing. Exists to shake out crashes and leaks that a
    /// single-scenario test never reaches; a failure here is a stability bug, not a rules bug.
    /// </summary>
    public sealed class SoakTests
    {
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator BuildingSurvivesAFranticMinute()
        {
            GameServices.Clear();
            yield return SceneManager.LoadSceneAsync(SceneNames.Building, LoadSceneMode.Single);
            yield return null;
            var building = GameServices.Get<BuildingSceneController>();
            var player = GameServices.Get<PlayerCharacter>();
            var perks = GameServices.Get<PerkSystem>();
            var pools = GameServices.Get<PoolRegistry>();
            var spawner = Object.FindFirstObjectByType<ZombieSpawner>();
            var vents = Object.FindObjectsByType<AirVent>(FindObjectsSortMode.None);
            var zombieDef = Resources.FindObjectsOfTypeAll<ZombieDefinition>()[0];
            var perkChannel = Resources.FindObjectsOfTypeAll<PerkEventChannel>()[0];
            var zombies = Resources.FindObjectsOfTypeAll<ZombieRuntimeSet>()[0];
            var rng = new System.Random(7);

            building.BeginRun();
            player.Health.Invulnerable = true; // we are here to crash the engine, not the player
            spawner.StopSpawning();

            float end = Time.time + 30f;
            float nextSpawn = 0f, nextPerk = 0f, nextNuke = 0f, nextSwitch = 0f;
            int drops = 0, nukes = 0, spawned = 0;
            var kinds = (PerkKind[])System.Enum.GetValues(typeof(PerkKind));

            while (Time.time < end)
            {
                if (Time.time >= nextSpawn && zombies.Count < 24)
                {
                    nextSpawn = Time.time + 0.15f;
                    AirVent vent = vents[rng.Next(vents.Length)];
                    var z = pools.Spawn<Zombie>(zombieDef.Prefab, vent.GratePosition, Quaternion.identity);
                    z.Spawn(new ZombieStats(60f, 5f, 4f, 25), vent);
                    spawned++;
                }

                if (Time.time >= nextPerk)
                {
                    nextPerk = Time.time + 0.7f;
                    PerkKind kind = kinds[rng.Next(kinds.Length)];
                    if (kind == PerkKind.Nuke)
                    {
                        kind = PerkKind.OneShot; // nukes are raised on their own cadence below
                    }

                    // One under the player (collected next frame) and one out in the building (expires or gets cleared).
                    perks.Drop(new PerkInfo(kind, 3f), player.Position);
                    perks.Drop(new PerkInfo(kind, 3f), vents[rng.Next(vents.Length)].FloorPosition);
                    drops += 2;
                }

                if (Time.time >= nextNuke)
                {
                    nextNuke = Time.time + 4f;
                    perkChannel.Raise(new PerkInfo(PerkKind.Nuke, 0f));
                    nukes++;
                }

                if (Time.time >= nextSwitch)
                {
                    nextSwitch = Time.time + 1.5f;
                    player.Inventory.Cycle(1);
                }

                // Spray in a random direction; sometimes at a zombie.
                Vector3 aim = zombies.Count > 0 && rng.NextDouble() < 0.6
                    ? zombies.Items[rng.Next(zombies.Count)].transform.position + Vector3.up - player.AimPoint
                    : Random.onUnitSphere;
                player.Look.SetRotation(Quaternion.LookRotation(aim.sqrMagnitude > 0.001f ? aim : Vector3.forward).eulerAngles.y, 0f);
                player.Inventory.PullTrigger();
                if (player.Inventory.Current != null && player.Inventory.Current.Magazine == 0)
                {
                    player.Inventory.Reload();
                }

                yield return null;
                player.Inventory.ReleaseTrigger();
            }

            Debug.Log($"[Soak] spawned={spawned} drops={drops} nukes={nukes} level={building.Director.Level} kills={building.Director.TotalKills} alive={zombies.Count} orbs={perks.LiveCount}");
            Assert.Greater(building.Director.TotalKills, 20, "the soak should have killed plenty");
            Assert.Greater(building.Director.Level, 1, "and advanced the level");
            building.EndRun();
            building.Director.ClearBuilding();
            perks.ClearAll();
            yield return null;
            Assert.AreEqual(0, zombies.Count);
            Assert.AreEqual(0, perks.LiveCount);
        }
    }
}
