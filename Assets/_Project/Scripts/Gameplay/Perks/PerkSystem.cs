using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Vent.Core.Audio;
using Vent.Core.Damage;
using Vent.Core.Events;
using Vent.Core.Perks;
using Vent.Core.Pooling;
using Vent.Core.Services;
using Vent.Enemies.Runtime;

namespace Vent.Gameplay.Perks
{
    /// <summary>
    /// The scene-side half of perks. Listens for kills and rolls the <see cref="PerkDropTable"/>
    /// to drop a <see cref="PerkPickup"/> where the zombie fell; listens for collected perks and
    /// carries out the one that needs the whole building (Nuke). Perks that belong to the player
    /// (ammo, invulnerability, one-shot) are applied by the player itself from the same channel.
    ///
    /// Only weapon kills drop perks: a Nuke's kills never chain into more drops.
    /// </summary>
    public sealed class PerkSystem : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private PerkDropTable table;
        [SerializeField] private GameObject pickupPrefab;
        [SerializeField] private ZombieRuntimeSet zombies;

        [Header("Events")]
        [SerializeField] private KillEventChannel kills;
        [SerializeField] private PerkEventChannel perkCollected;
        [SerializeField] private LevelEventChannel levelChanged;

        [SerializeField, Tooltip("0 = random per run; anything else makes the drop sequence repeatable.")]
        private int seed;

        private readonly List<PerkPickup> live = new();
        private System.Random rng;
        private PoolRegistry pools;

        public PerkDropTable Table => table;

        /// <summary>Orbs currently on the floor.</summary>
        public int LiveCount
        {
            get
            {
                live.RemoveAll(p => p == null || !p.IsLive);
                return live.Count;
            }
        }

        public void Configure(PerkDropTable dropTable, GameObject prefab, ZombieRuntimeSet zombieSet,
            KillEventChannel killChannel, PerkEventChannel perkChannel, LevelEventChannel levelChannel)
        {
            table = dropTable;
            pickupPrefab = prefab;
            zombies = zombieSet;
            kills = killChannel;
            perkCollected = perkChannel;
            levelChanged = levelChannel;
        }

        private void Awake() => rng = seed != 0 ? new System.Random(seed) : new System.Random();

        private void OnEnable()
        {
            GameServices.Register(this);
            kills?.Subscribe(OnKill);
            perkCollected?.Subscribe(OnPerkCollected);
            levelChanged?.Subscribe(OnLevelChanged);
        }

        private void OnDisable()
        {
            levelChanged?.Unsubscribe(OnLevelChanged);
            perkCollected?.Unsubscribe(OnPerkCollected);
            kills?.Unsubscribe(OnKill);
            GameServices.Unregister(this);
        }

        // ------------------------------------------------------------------ drops

        private void OnKill(KillInfo info)
        {
            // Killer == null is a despawn or an environmental death; Killer == this is a Nuke.
            if (table == null || info.Killer == null || ReferenceEquals(info.Killer, this))
            {
                return;
            }

            if (LiveCount >= table.MaxOnFloor)
            {
                return;
            }

            if (table.TryRoll(rng.NextDouble(), rng.NextDouble(), out PerkInfo perk))
            {
                Drop(perk, info.Position);
            }
        }

        /// <summary>Put an orb on the floor near <paramref name="near"/> (snapped to the NavMesh, so it is always reachable).</summary>
        public PerkPickup Drop(PerkInfo perk, Vector3 near)
        {
            if (pickupPrefab == null || (pools == null && !GameServices.TryGet(out pools)))
            {
                return null;
            }

            Vector3 floor = NavMesh.SamplePosition(near, out NavMeshHit hit, 2f, NavMesh.AllAreas) ? hit.position : new Vector3(near.x, 0f, near.z);
            var pickup = pools.Spawn<PerkPickup>(pickupPrefab, floor + Vector3.up * 0.9f, Quaternion.identity);
            if (pickup == null)
            {
                return null;
            }

            pickup.Show(perk, table != null ? table.PickupLifetime : 25f);
            live.Add(pickup);
            SfxPlayer.TryPlayAt(SoundId.PerkDrop, floor, 0.7f);
            return pickup;
        }

        /// <summary>Remove every orb (run reset).</summary>
        public void ClearAll()
        {
            foreach (PerkPickup p in live)
            {
                if (p != null && p.IsLive)
                {
                    p.GetComponent<PooledObject>().Release();
                }
            }

            live.Clear();
        }

        // ------------------------------------------------------------------ effects

        private void OnPerkCollected(PerkInfo perk)
        {
            if (perk.Kind == PerkKind.Nuke)
            {
                Nuke();
            }
        }

        /// <summary>Kill every living zombie. Kills are credited to this system, so they count for the level but not for weapon XP.</summary>
        public int Nuke()
        {
            if (zombies == null)
            {
                return 0;
            }

            Vector3 origin = GameServices.TryGet(out IPlayerTarget player) ? player.Position : transform.position;
            var targets = new List<Zombie>(zombies.Items); // dying removes from the set; iterate a copy
            int killed = 0;
            foreach (Zombie z in targets)
            {
                if (z == null || !z.IsAlive)
                {
                    continue;
                }

                Vector3 away = z.transform.position - origin;
                away.y = 0f;
                away = away.sqrMagnitude > 0.001f ? away.normalized : Vector3.forward;
                var blast = new DamageInfo(float.MaxValue, DamageKind.Environment, this, z.transform.position + Vector3.up, -away, away);
                if (z.ApplyDamage(blast).Killed)
                {
                    killed++;
                }
            }

            SfxPlayer.TryPlay2D(SoundId.PerkNuke, 1f);
            return killed;
        }

        private void OnLevelChanged(LevelInfo info)
        {
            if (info.Level <= 1)
            {
                ClearAll(); // a new run starts with a clean floor
            }
        }
    }
}
