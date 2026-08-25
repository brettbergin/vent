using UnityEngine;
using Vent.Core.Pooling;

namespace Vent.Weapons.VFX
{
    /// <summary>
    /// A pooled brass casing. Not a rigidbody: a hand-rolled arc with spin and a single bounce off
    /// the floor plane it was ejected above, which is all the eye needs at this size and lifetime.
    /// </summary>
    [RequireComponent(typeof(PooledObject))]
    public sealed class ShellCasing : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float lifetime = 1.4f;
        [SerializeField, Min(0f)] private float ejectSpeed = 2.6f;
        [SerializeField, Min(0f)] private float upwardSpeed = 1.6f;
        [SerializeField, Min(0f)] private float gravity = 9.81f;
        [SerializeField, Range(0f, 1f)] private float bounce = 0.35f;

        private PooledObject pooled;
        private Vector3 velocity;
        private Vector3 spin;
        private float floorY;
        private float spawnedAt;
        private Vector3 baseScale;

        private void Awake()
        {
            pooled = GetComponent<PooledObject>();
            baseScale = transform.localScale;
        }

        /// <summary>Fling the casing out of the ejection port, to its right and slightly up.</summary>
        public void Eject(Vector3 portRight, Vector3 portUp)
        {
            spawnedAt = Time.time;
            velocity = portRight * ejectSpeed * Random.Range(0.8f, 1.2f) + portUp * upwardSpeed * Random.Range(0.7f, 1.3f)
                       + Random.insideUnitSphere * 0.4f;
            spin = Random.insideUnitSphere * 720f;
            floorY = transform.position.y - 1.5f; // gun is held ~1.5 m above the floor
            transform.localScale = baseScale;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            float age = Time.time - spawnedAt;
            if (age >= lifetime)
            {
                pooled.Release();
                return;
            }

            velocity.y -= gravity * dt;
            Vector3 next = transform.position + velocity * dt;
            if (next.y <= floorY && velocity.y < 0f)
            {
                next.y = floorY;
                velocity = new Vector3(velocity.x * 0.6f, -velocity.y * bounce, velocity.z * 0.6f);
                spin *= 0.4f;
            }

            transform.position = next;
            transform.Rotate(spin * dt, Space.Self);

            // Shrink away over the final quarter instead of popping.
            float fade = Mathf.Clamp01((lifetime - age) / (lifetime * 0.25f));
            transform.localScale = baseScale * fade;
        }
    }
}
