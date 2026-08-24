using UnityEngine;
using Vent.Core.Audio;
using Vent.Core.Services;

namespace Vent.Enemies.Spawning
{
    /// <summary>
    /// An AC vent: a grate high on a wall plus a floor point directly below it on the NavMesh.
    /// Zombies emerge at the grate and drop to the floor point. The grate rattles briefly
    /// before a spawn so an attentive player gets a tell.
    /// </summary>
    public sealed class AirVent : MonoBehaviour
    {
        [SerializeField, Tooltip("Where the zombie first appears (inside the duct opening).")]
        private Transform grate;
        [SerializeField, Tooltip("Where the zombie lands; must be on the NavMesh.")]
        private Transform floorPoint;
        [SerializeField] private VentRuntimeSet registry;
        [SerializeField, Tooltip("Optional: the grate mesh, shaken during the rattle tell.")]
        private Transform grateVisual;

        private float rattleUntil;
        private Vector3 grateVisualRest;

        public Vector3 GratePosition => grate != null ? grate.position : transform.position;
        public Vector3 FloorPosition => floorPoint != null ? floorPoint.position : transform.position;
        /// <summary>Direction the vent faces into the room.</summary>
        public Vector3 Facing => transform.forward;

        /// <summary>Time of the last spawn; the spawner avoids reusing a vent immediately.</summary>
        public float LastSpawnTime { get; private set; } = float.NegativeInfinity;

        public void Configure(Transform grateTransform, Transform floorTransform, VentRuntimeSet set, Transform visual)
        {
            grate = grateTransform;
            floorPoint = floorTransform;
            registry = set;
            grateVisual = visual;
        }

        private void Awake()
        {
            if (grateVisual != null)
            {
                grateVisualRest = grateVisual.localPosition;
            }
        }

        private void OnEnable() => registry?.Add(this);
        private void OnDisable() => registry?.Remove(this);

        /// <summary>Play the pre-spawn tell.</summary>
        public void Rattle(float seconds)
        {
            rattleUntil = Time.time + seconds;
            if (GameServices.TryGet(out SfxPlayer sfx))
            {
                sfx.PlayAt(SoundId.VentRattle, GratePosition, 0.9f);
            }
        }

        public void MarkSpawned() => LastSpawnTime = Time.time;

        private void Update()
        {
            if (grateVisual == null)
            {
                return;
            }

            if (Time.time < rattleUntil)
            {
                float t = Time.time * 40f;
                grateVisual.localPosition = grateVisualRest + new Vector3(Mathf.Sin(t) * 0.01f, Mathf.Cos(t * 1.3f) * 0.01f, 0f);
            }
            else if (grateVisual.localPosition != grateVisualRest)
            {
                grateVisual.localPosition = grateVisualRest;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(GratePosition, 0.3f);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(FloorPosition, 0.3f);
            Gizmos.DrawLine(GratePosition, FloorPosition);
        }
    }
}
