using System;
using UnityEngine;
using UnityEngine.Pool;

namespace Vent.Core.Pooling
{
    /// <summary>
    /// A pool of instances of a single prefab, built on <see cref="UnityEngine.Pool.ObjectPool{T}"/>.
    ///
    /// Instantiate/Destroy are among the most expensive things a Unity game does per-frame;
    /// every zombie, muzzle flash, tracer and impact effect in this project is pooled.
    /// Instances are parented under a container object so the hierarchy stays readable.
    /// </summary>
    public sealed class PrefabPool : IDisposable
    {
        private readonly GameObject prefab;
        private readonly Transform container;
        private readonly ObjectPool<PooledObject> pool;

        /// <summary>Instances currently checked out.</summary>
        public int CountActive => pool.CountActive;

        /// <summary>Instances sitting idle in the pool.</summary>
        public int CountInactive => pool.CountInactive;

        /// <param name="prefab">Prefab to instantiate. A <see cref="PooledObject"/> is added if missing.</param>
        /// <param name="container">Parent for pooled instances (keeps the hierarchy tidy). May be null.</param>
        /// <param name="prewarm">Instances to create immediately, to avoid hitches mid-game.</param>
        /// <param name="maxSize">Instances beyond this count are destroyed on release instead of kept.</param>
        public PrefabPool(GameObject prefab, Transform container = null, int prewarm = 0, int maxSize = 256)
        {
            this.prefab = prefab != null ? prefab : throw new ArgumentNullException(nameof(prefab));
            this.container = container;

            pool = new ObjectPool<PooledObject>(
                createFunc: Create,
                actionOnGet: OnGet,
                actionOnRelease: OnRelease,
                actionOnDestroy: OnDestroy,
                collectionCheck: true,
                defaultCapacity: Mathf.Max(prewarm, 8),
                maxSize: maxSize);

            if (prewarm <= 0)
            {
                return;
            }

            // Get/Release in a loop is the documented way to prewarm ObjectPool<T>.
            var buffer = new PooledObject[prewarm];
            for (int i = 0; i < prewarm; i++)
            {
                buffer[i] = pool.Get();
            }

            for (int i = 0; i < prewarm; i++)
            {
                pool.Release(buffer[i]);
            }
        }

        /// <summary>Check out an instance and place it at the given pose.</summary>
        public PooledObject Get(Vector3 position, Quaternion rotation)
        {
            PooledObject instance = pool.Get();
            instance.transform.SetPositionAndRotation(position, rotation);
            return instance;
        }

        /// <summary>Check out an instance, returning the requested component (must exist on the prefab).</summary>
        public T Get<T>(Vector3 position, Quaternion rotation) where T : Component
        {
            return Get(position, rotation).GetComponent<T>();
        }

        internal void Release(PooledObject instance) => pool.Release(instance);

        /// <summary>Destroy all pooled instances. Active instances are left alone (they release themselves).</summary>
        public void Dispose() => pool.Clear();

        private PooledObject Create()
        {
            GameObject go = UnityEngine.Object.Instantiate(prefab, container);
            go.name = prefab.name; // strip "(Clone)" so the hierarchy and profiler are readable
            if (!go.TryGetComponent(out PooledObject pooled))
            {
                pooled = go.AddComponent<PooledObject>();
            }

            pooled.Owner = this;
            go.SetActive(false);
            return pooled;
        }

        private static void OnGet(PooledObject instance)
        {
            instance.IsActiveInPool = true;
            instance.gameObject.SetActive(true);
        }

        private void OnRelease(PooledObject instance)
        {
            instance.IsActiveInPool = false;
            instance.gameObject.SetActive(false);
            if (container != null)
            {
                instance.transform.SetParent(container, worldPositionStays: false);
            }
        }

        private static void OnDestroy(PooledObject instance)
        {
            if (instance == null)
            {
                return;
            }

            // Destroy is play-mode only; edit-mode tests exercise the pool too.
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(instance.gameObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(instance.gameObject);
            }
        }
    }
}
