using System.Collections.Generic;
using UnityEngine;
using Vent.Core.Data;
using Vent.Core.Events;
using Vent.Core.Pooling;
using Vent.Core.Services;
using Vent.Core.Utility;
using Vent.Enemies.Data;
using Vent.Enemies.Runtime;

namespace Vent.Enemies.Spawning
{
    /// <summary>
    /// Keeps the building populated. Every <c>spawnInterval</c> seconds (from the difficulty
    /// profile) it picks a vent and spawns a zombie, as long as fewer than <c>maxConcurrent</c>
    /// are alive. Vents are scored so spawns happen out of the player's sight but not too far
    /// away: the player should feel surrounded, not hunted from across the map.
    /// </summary>
    public sealed class ZombieSpawner : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private DifficultyProfile difficulty;
        [SerializeField] private ZombieDefinition zombie;
        [SerializeField] private ZombieRuntimeSet zombies;
        [SerializeField] private VentRuntimeSet vents;

        [Header("Events")]
        [SerializeField] private LevelEventChannel levelChanged;

        [Header("Vent selection")]
        [SerializeField, Min(0f)] private float minDistanceFromPlayer = 5f;
        [SerializeField, Min(0f)] private float idealDistance = 14f;
        [SerializeField, Min(0f), Tooltip("A vent used within this many seconds is deprioritised.")]
        private float ventReuseCooldown = 4f;
        [SerializeField, Min(0f), Tooltip("Grate rattle lead time before the zombie appears.")]
        private float tellSeconds = 0.6f;
        [SerializeField, Min(1)] private int prewarmCount = 12;

        private readonly List<PendingSpawn> pending = new();
        private readonly List<(AirVent vent, float weight)> candidates = new();
        private DifficultySnapshot snapshot;
        private Cooldown spawnCooldown;
        private PrefabPool pool;
        private bool running;

        private struct PendingSpawn
        {
            public AirVent Vent;
            public float At;
        }

        public bool IsRunning => running;
        public DifficultySnapshot Snapshot => snapshot;
        public int AliveCount => zombies != null ? zombies.Count : 0;

        public void Configure(DifficultyProfile profile, ZombieDefinition def, ZombieRuntimeSet zombieSet, VentRuntimeSet ventSet, LevelEventChannel levelEvent)
        {
            difficulty = profile;
            zombie = def;
            zombies = zombieSet;
            vents = ventSet;
            levelChanged = levelEvent;
        }

        private void Awake() => snapshot = difficulty != null ? difficulty.Evaluate(1) : default;

        private void OnEnable() => levelChanged?.Subscribe(OnLevelChanged);
        private void OnDisable() => levelChanged?.Unsubscribe(OnLevelChanged);

        private void Start()
        {
            if (zombie != null && zombie.Prefab != null && GameServices.TryGet(out PoolRegistry pools))
            {
                pool = pools.GetPool(zombie.Prefab, prewarmCount);
            }
        }

        /// <summary>Begin spawning (run start). Existing zombies are cleared first.</summary>
        public void StartSpawning()
        {
            DespawnAll();
            running = true;
            spawnCooldown.Start(Time.time, 1.5f); // brief grace period after spawn
        }

        public void StopSpawning()
        {
            running = false;
            pending.Clear();
        }

        /// <summary>Remove every zombie immediately without kill credit.</summary>
        public void DespawnAll()
        {
            pending.Clear();
            if (zombies == null)
            {
                return;
            }

            // Despawn releases to the pool, which removes from the set; iterate a copy.
            var copy = new List<Zombie>(zombies.Items);
            foreach (Zombie z in copy)
            {
                z.Despawn();
            }
        }

        private void OnLevelChanged(LevelInfo info)
        {
            if (difficulty == null)
            {
                return;
            }

            snapshot = difficulty.Evaluate(info.Level);

            // The brief says zombie damage is relative to the current level, so living zombies
            // adopt the new numbers immediately (health fraction preserved).
            if (zombies != null)
            {
                ZombieStats stats = ZombieStats.From(zombie, snapshot);
                foreach (Zombie z in zombies.Items)
                {
                    z.Rescale(stats);
                }
            }
        }

        private void Update()
        {
            if (!running || pool == null || vents == null || vents.Count == 0)
            {
                return;
            }

            float now = Time.time;

            // Resolve tells whose lead time has elapsed.
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (now >= pending[i].At)
                {
                    SpawnAt(pending[i].Vent);
                    pending.RemoveAt(i);
                }
            }

            int alive = AliveCount + pending.Count;
            if (alive >= snapshot.MaxConcurrent)
            {
                return;
            }

            if (spawnCooldown.TryConsume(now, snapshot.SpawnInterval))
            {
                AirVent vent = PickVent();
                if (vent != null)
                {
                    vent.Rattle(tellSeconds);
                    pending.Add(new PendingSpawn { Vent = vent, At = now + tellSeconds });
                }
            }
        }

        private void SpawnAt(AirVent vent)
        {
            if (vent == null || !vent.isActiveAndEnabled)
            {
                return;
            }

            var z = pool.Get<Zombie>(vent.GratePosition, Quaternion.identity);
            if (z == null)
            {
                return;
            }

            vent.MarkSpawned();
            z.Spawn(ZombieStats.From(zombie, snapshot), vent);
        }

        /// <summary>
        /// Weighted random choice. Weight favours vents near the ideal distance, out of view,
        /// and not recently used; vents closer than the minimum are excluded outright.
        /// </summary>
        private AirVent PickVent()
        {
            if (!GameServices.TryGet(out IPlayerTarget player))
            {
                return vents.Items[Random.Range(0, vents.Count)];
            }

            candidates.Clear();
            float total = 0f;
            Vector3 eye = player.AimPoint;
            Vector3 forward = player.Transform.forward;

            foreach (AirVent vent in vents.Items)
            {
                float distance = Vector3.Distance(vent.GratePosition, player.Position);
                if (distance < minDistanceFromPlayer)
                {
                    continue;
                }

                float distanceScore = Mathf.Exp(-Mathf.Abs(distance - idealDistance) / idealDistance);

                // Test a point just in front of the grate: the grate itself sits inside the wall.
                Vector3 toVent = vent.transform.position + vent.Facing * 0.3f - eye;
                bool inFront = Vector3.Dot(forward, toVent.normalized) > 0.3f;
                bool occluded = Physics.Raycast(eye, toVent.normalized, toVent.magnitude, Layers.OcclusionMask, QueryTriggerInteraction.Ignore);
                float visibilityScore = (!inFront || occluded) ? 1f : 0.25f;

                float reuseScore = Time.time - vent.LastSpawnTime < ventReuseCooldown ? 0.2f : 1f;

                float weight = distanceScore * visibilityScore * reuseScore + 0.01f;
                candidates.Add((vent, weight));
                total += weight;
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            float roll = Random.value * total;
            foreach ((AirVent vent, float weight) in candidates)
            {
                roll -= weight;
                if (roll <= 0f)
                {
                    return vent;
                }
            }

            return candidates[candidates.Count - 1].vent;
        }
    }
}
