using UnityEngine;

namespace Vent.Enemies.Runtime
{
    /// <summary>
    /// World-space health bar over a zombie: two unlit quads (track and fill) that face the camera.
    /// The fill's width and colour follow <see cref="Zombie.HealthNormalized"/>; the whole bar
    /// fades with distance and hides while the zombie is dormant, emerging or dead. Colour goes
    /// through a <see cref="MaterialPropertyBlock"/>, so pooled bars share one material.
    /// </summary>
    public sealed class ZombieHealthBar : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Zombie zombie;
        [SerializeField] private Transform fill;
        [SerializeField] private Renderer fillRenderer;
        [SerializeField] private Renderer trackRenderer;
        [SerializeField, Min(0f)] private float fadeStart = 18f;
        [SerializeField, Min(0f)] private float fadeEnd = 30f;
        [SerializeField] private Color fullColor = new(0.35f, 0.85f, 0.35f);
        [SerializeField] private Color midColor = new(0.95f, 0.8f, 0.2f);
        [SerializeField] private Color emptyColor = new(0.9f, 0.2f, 0.15f);

        private MaterialPropertyBlock block;
        private Vector3 fillRestScale;
        private Color trackBase = new(0.05f, 0.05f, 0.05f, 0.8f);

        public void Configure(Zombie owner, Transform fillTransform, Renderer fillRend, Renderer trackRend)
        {
            zombie = owner;
            fill = fillTransform;
            fillRenderer = fillRend;
            trackRenderer = trackRend;
        }

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            if (fill != null)
            {
                fillRestScale = fill.localScale;
            }

            if (trackRenderer != null && trackRenderer.sharedMaterial != null && trackRenderer.sharedMaterial.HasProperty(BaseColorId))
            {
                trackBase = trackRenderer.sharedMaterial.GetColor(BaseColorId);
            }
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            bool visible = zombie != null && cam != null && zombie.IsAlive && zombie.State != ZombieState.Emerging;
            float alpha = 0f;
            if (visible)
            {
                float distance = Vector3.Distance(cam.transform.position, transform.position);
                alpha = 1f - Mathf.Clamp01((distance - fadeStart) / Mathf.Max(0.01f, fadeEnd - fadeStart));
                visible = alpha > 0.01f;
            }

            if (fillRenderer != null) fillRenderer.enabled = visible;
            if (trackRenderer != null) trackRenderer.enabled = visible;
            if (!visible)
            {
                return;
            }

            // Face the camera, upright, and stay at world scale regardless of the rig's height variation.
            Vector3 toCam = cam.transform.position - transform.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
            }

            Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            transform.localScale = new Vector3(1f / Mathf.Max(0.01f, parentScale.x), 1f / Mathf.Max(0.01f, parentScale.y), 1f / Mathf.Max(0.01f, parentScale.z));

            float health = zombie.HealthNormalized;
            if (fill != null)
            {
                // Anchored at the left edge: shift by half the removed width.
                fill.localScale = new Vector3(fillRestScale.x * health, fillRestScale.y, fillRestScale.z);
                fill.localPosition = new Vector3(-fillRestScale.x * (1f - health) * 0.5f, 0f, -0.001f);
            }

            Color c = health > 0.5f ? Color.Lerp(midColor, fullColor, (health - 0.5f) * 2f) : Color.Lerp(emptyColor, midColor, health * 2f);
            c.a = alpha;
            if (fillRenderer != null)
            {
                block.SetColor(BaseColorId, c);
                fillRenderer.SetPropertyBlock(block);
            }

            if (trackRenderer != null)
            {
                Color t = trackBase;
                t.a = trackBase.a * alpha;
                block.SetColor(BaseColorId, t);
                trackRenderer.SetPropertyBlock(block);
            }
        }
    }
}
