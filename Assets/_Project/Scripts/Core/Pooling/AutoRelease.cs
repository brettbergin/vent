using UnityEngine;

namespace Vent.Core.Pooling
{
    /// <summary>
    /// Returns a pooled effect to its pool after a fixed lifetime. Used by muzzle flashes,
    /// impact particles and tracers so the systems that spawn them can fire-and-forget.
    /// </summary>
    [RequireComponent(typeof(PooledObject))]
    public sealed class AutoRelease : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float lifetime = 1f;

        private PooledObject pooled;
        private float releaseAt;

        /// <summary>Lifetime in seconds; may be overridden per spawn.</summary>
        public float Lifetime
        {
            get => lifetime;
            set => lifetime = Mathf.Max(0.01f, value);
        }

        private void Awake() => pooled = GetComponent<PooledObject>();

        private void OnEnable() => releaseAt = Time.time + lifetime;

        private void Update()
        {
            if (Time.time >= releaseAt)
            {
                pooled.Release();
            }
        }
    }
}
