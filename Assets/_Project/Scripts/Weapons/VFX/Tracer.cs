using UnityEngine;
using Vent.Core.Pooling;

namespace Vent.Weapons.VFX
{
    /// <summary>
    /// A pooled bullet trail: a <see cref="LineRenderer"/> that fades out over a short lifetime.
    /// Hitscan is instantaneous; the tracer is purely a readability cue for where the shot went.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    [RequireComponent(typeof(PooledObject))]
    public sealed class Tracer : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float lifetime = 0.08f;
        [SerializeField, Min(0f)] private float startWidth = 0.02f;

        private LineRenderer line;
        private PooledObject pooled;
        private float spawnedAt;

        private void Awake()
        {
            line = GetComponent<LineRenderer>();
            pooled = GetComponent<PooledObject>();
            line.positionCount = 2;
            line.useWorldSpace = true;
        }

        public void Show(Vector3 from, Vector3 to)
        {
            spawnedAt = Time.time;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.widthMultiplier = startWidth;
        }

        private void Update()
        {
            float t = (Time.time - spawnedAt) / lifetime;
            if (t >= 1f)
            {
                pooled.Release();
                return;
            }

            line.widthMultiplier = startWidth * (1f - t);
        }
    }
}
