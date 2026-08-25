using UnityEngine;
using Vent.Core.Audio;
using Vent.Core.Events;
using Vent.Core.Pooling;
using Vent.Core.Services;

namespace Vent.Core.Perks
{
    /// <summary>
    /// A perk orb on the floor. Pooled; shown with <see cref="Show"/>. It bobs and spins, blinks
    /// during its last seconds, and is collected when the player walks through it. Collection is
    /// a distance check against <see cref="IPlayerTarget"/> rather than a trigger collider, so it
    /// needs no physics layer, never blocks bullets and cannot be stood on.
    /// </summary>
    [RequireComponent(typeof(PooledObject))]
    public sealed class PerkPickup : MonoBehaviour
    {
        private const float BlinkSeconds = 4f;

        [Header("Wiring")]
        [SerializeField] private PerkEventChannel collected;
        [SerializeField] private Transform visual;
        [SerializeField] private Renderer[] tinted = System.Array.Empty<Renderer>();
        [SerializeField] private Light glow;

        [Header("Tuning")]
        [SerializeField, Min(0.1f)] private float pickupRadius = 1.1f;
        [SerializeField, Min(0f)] private float bobHeight = 0.12f;
        [SerializeField, Min(0f)] private float spinDegreesPerSecond = 90f;

        private PooledObject pooled;
        private MaterialPropertyBlock block;
        private PerkInfo perk;
        private float lifetime;
        private float shownAt;
        private Vector3 restPosition;
        private float phase;
        private bool live;

        public PerkInfo Perk => perk;
        public bool IsLive => live;

        public void Configure(PerkEventChannel channel, Transform visualRoot, Renderer[] tintedRenderers, Light light)
        {
            collected = channel;
            visual = visualRoot;
            tinted = tintedRenderers;
            glow = light;
        }

        private void Awake()
        {
            pooled = GetComponent<PooledObject>();
            block = new MaterialPropertyBlock();
        }

        private void OnDisable() => live = false;

        /// <summary>Make the orb live at its current position for <paramref name="lifetimeSeconds"/>.</summary>
        public void Show(PerkInfo info, float lifetimeSeconds)
        {
            perk = info;
            lifetime = lifetimeSeconds;
            shownAt = Time.time;
            restPosition = transform.position;
            phase = Random.value * Mathf.PI * 2f;
            live = true;

            Color color = PerkStyle.Color(info.Kind);
            block.SetColor("_BaseColor", color);
            block.SetColor("_EmissionColor", color * 2.5f);
            foreach (Renderer r in tinted)
            {
                if (r != null)
                {
                    r.SetPropertyBlock(block);
                    r.enabled = true;
                }
            }

            if (glow != null)
            {
                glow.color = color;
                glow.enabled = true;
            }
        }

        private void Update()
        {
            if (!live)
            {
                return;
            }

            float age = Time.time - shownAt;
            if (age >= lifetime)
            {
                Expire();
                return;
            }

            float t = Time.time * 2f + phase;
            transform.position = restPosition + Vector3.up * (Mathf.Sin(t) * bobHeight);
            if (visual != null)
            {
                visual.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.World);
            }

            // Blink faster as it runs out, so the player knows it is about to go.
            float remaining = lifetime - age;
            bool visible = remaining > BlinkSeconds || Mathf.Repeat(remaining * (remaining < 1.5f ? 8f : 4f), 1f) > 0.35f;
            SetVisible(visible);

            if (GameServices.TryGet(out IPlayerTarget player) && player.IsAlive && IsWithinReach(player.Position))
            {
                Collect();
            }
        }

        private bool IsWithinReach(Vector3 feet)
        {
            Vector3 d = restPosition - feet;
            float dy = d.y;
            d.y = 0f;
            return d.sqrMagnitude <= pickupRadius * pickupRadius && dy > -0.5f && dy < 2f;
        }

        private void Collect()
        {
            live = false;
            SfxPlayer.TryPlay2D(SoundId.PerkPickup, 0.8f);
            PerkInfo info = perk;
            pooled.Release();
            collected?.Raise(info);
        }

        private void Expire()
        {
            live = false;
            pooled.Release();
        }

        private void SetVisible(bool visible)
        {
            foreach (Renderer r in tinted)
            {
                if (r != null)
                {
                    r.enabled = visible;
                }
            }

            if (glow != null)
            {
                glow.enabled = visible;
            }
        }
    }
}
