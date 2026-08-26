using UnityEngine;
using Vent.Core.Pooling;
using Vent.Core.Services;
using Vent.Core.Utility;

namespace Vent.Weapons.VFX
{
    /// <summary>
    /// A pooled muzzle flash: additive sprite planes that pop for a couple of frames, a point light
    /// that spikes and dies, and a world-space smoke/spark burst spawned alongside so it lingers after
    /// the flash is gone. Parented to the muzzle while alive so it follows the gun during recoil.
    /// Every shot gets a random roll and size so automatic fire never strobes the same shape.
    /// </summary>
    [RequireComponent(typeof(PooledObject))]
    public sealed class MuzzleFlash : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField, Min(0.01f)] private float lifetime = 0.07f;
        [SerializeField] private Light flashLight;
        [SerializeField] private Transform visual;
        [SerializeField] private Renderer[] planes = System.Array.Empty<Renderer>();
        [SerializeField] private GameObject smokePrefab;
        [SerializeField, Min(0f)] private float lightIntensity = 9f;

        private PooledObject pooled;
        private MaterialPropertyBlock block;
        private float spawnedAt;
        private float scale = 1f;

        public void Configure(Light light, Transform visualRoot, Renderer[] flashPlanes, GameObject smoke)
        {
            flashLight = light;
            visual = visualRoot;
            planes = flashPlanes;
            smokePrefab = smoke;
        }

        private void Awake()
        {
            pooled = GetComponent<PooledObject>();
            block = new MaterialPropertyBlock();
        }

        /// <param name="firstPerson">
        /// True for the view-model (the flash lives on the overlay camera's layer); false for a muzzle
        /// out in the world, where the main camera must see it and the overlay camera cannot.
        /// </param>
        public void Play(Transform muzzle, float weaponScale = 1f, bool firstPerson = true)
        {
            spawnedAt = Time.time;
            transform.SetParent(muzzle, worldPositionStays: false);
            int layer = firstPerson ? Layers.WeaponViewIndex : 0;
            if (gameObject.layer != layer)
            {
                Layers.SetRecursively(gameObject, layer);
                if (flashLight != null)
                {
                    flashLight.gameObject.layer = Layers.PlayerIndex; // lights live where both cameras look
                }
            }

            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            scale = weaponScale * Random.Range(0.75f, 1.25f);
            if (visual != null)
            {
                // Longer than it is wide on a hot barrel: stretch along the bore a little more than sideways.
                visual.localScale = new Vector3(scale, scale, scale * Random.Range(1f, 1.5f));
            }

            SetBrightness(1f);
            if (flashLight != null)
            {
                flashLight.intensity = lightIntensity * Mathf.Sqrt(weaponScale);
            }

            if (smokePrefab != null && GameServices.TryGet(out PoolRegistry pools))
            {
                var smoke = pools.Spawn(smokePrefab, muzzle.position + muzzle.forward * 0.05f, muzzle.rotation);
                smoke.transform.localScale = Vector3.one * Mathf.Sqrt(weaponScale);
            }
        }

        private void Update()
        {
            float t = (Time.time - spawnedAt) / lifetime;
            if (t >= 1f)
            {
                pooled.Release();
                return;
            }

            // Bright for the first third, then a fast falloff; the light follows the same shape.
            float brightness = t < 0.35f ? 1f : 1f - (t - 0.35f) / 0.65f;
            SetBrightness(brightness);
            if (flashLight != null)
            {
                flashLight.intensity = lightIntensity * Mathf.Sqrt(scale) * brightness;
            }
        }

        private void SetBrightness(float brightness)
        {
            block.SetColor(BaseColor, new Color(1f, 1f, 1f, brightness));
            foreach (Renderer r in planes)
            {
                if (r != null)
                {
                    r.SetPropertyBlock(block);
                }
            }
        }
    }
}
