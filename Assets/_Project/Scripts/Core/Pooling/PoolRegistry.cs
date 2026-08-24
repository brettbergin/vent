using System.Collections.Generic;
using UnityEngine;
using Vent.Core.Services;

namespace Vent.Core.Pooling
{
    /// <summary>
    /// Scene-level owner of all <see cref="PrefabPool"/>s, keyed by prefab. Systems ask the
    /// registry for "the pool for this prefab" rather than each maintaining their own, so a
    /// muzzle-flash prefab shared by two weapons shares one pool.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoolRegistry : MonoBehaviour
    {
        [System.Serializable]
        private struct PrewarmEntry
        {
            public GameObject Prefab;
            [Min(0)] public int Count;
        }

        [SerializeField]
        [Tooltip("Pools created (and filled) on Awake to avoid first-use hitches.")]
        private PrewarmEntry[] prewarm = System.Array.Empty<PrewarmEntry>();

        private readonly Dictionary<GameObject, PrefabPool> pools = new();

        private void Awake()
        {
            foreach (PrewarmEntry entry in prewarm)
            {
                if (entry.Prefab != null)
                {
                    GetPool(entry.Prefab, entry.Count);
                }
            }
        }

        private void OnEnable() => GameServices.Register(this);
        private void OnDisable() => GameServices.Unregister(this);

        private void OnDestroy()
        {
            foreach (PrefabPool pool in pools.Values)
            {
                pool.Dispose();
            }

            pools.Clear();
        }

        /// <summary>Get (or lazily create) the pool for a prefab.</summary>
        public PrefabPool GetPool(GameObject prefab, int prewarmCount = 0)
        {
            if (pools.TryGetValue(prefab, out PrefabPool pool))
            {
                return pool;
            }

            var container = new GameObject($"Pool_{prefab.name}").transform;
            container.SetParent(transform, worldPositionStays: false);
            pool = new PrefabPool(prefab, container, prewarmCount);
            pools.Add(prefab, pool);
            return pool;
        }

        /// <summary>Convenience: spawn from the pool for <paramref name="prefab"/>.</summary>
        public PooledObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            return GetPool(prefab).Get(position, rotation);
        }

        /// <summary>Convenience: spawn and fetch a component.</summary>
        public T Spawn<T>(GameObject prefab, Vector3 position, Quaternion rotation) where T : Component
        {
            return GetPool(prefab).Get<T>(position, rotation);
        }
    }
}
