using UnityEngine;
using Vent.Core.Pooling;

namespace Vent.Weapons.VFX
{
    /// <summary>
    /// A pooled muzzle flash: a point light plus an emissive quad that lives for a few frames.
    /// Parented to the muzzle while alive so it follows the gun during recoil.
    /// </summary>
    [RequireComponent(typeof(PooledObject))]
    public sealed class MuzzleFlash : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float lifetime = 0.05f;
        [SerializeField] private Light flashLight;
        [SerializeField] private Transform visual;
        [SerializeField, Min(0f)] private float lightIntensity = 6f;

        private PooledObject pooled;
        private float spawnedAt;

        private void Awake() => pooled = GetComponent<PooledObject>();

        public void Play(Transform muzzle)
        {
            spawnedAt = Time.time;
            transform.SetParent(muzzle, worldPositionStays: false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float scale = Random.Range(0.8f, 1.2f);
            if (visual != null)
            {
                visual.localScale = Vector3.one * scale;
            }

            if (flashLight != null)
            {
                flashLight.intensity = lightIntensity;
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

            if (flashLight != null)
            {
                flashLight.intensity = lightIntensity * (1f - t);
            }
        }
    }
}
