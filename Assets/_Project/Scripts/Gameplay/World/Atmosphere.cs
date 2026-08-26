using UnityEngine;
using UnityEngine.Rendering;
using Vent.Core.Services;
using Vent.Core.Utility;
using Vent.Weapons.Runtime;

namespace Vent.Gameplay.World
{
    /// <summary>
    /// Blends the scene's fog and ambient light between an indoor preset (the dim, close haze the
    /// building was graded for) and an outdoor one (dusk that still reads at three hundred metres)
    /// depending on whether the player is inside the building's footprint. RenderSettings are
    /// global, so one component owns them; the blend takes about a second, which reads as eyes
    /// adjusting rather than a cut. Also lets the sun reach the weapon view-model only outdoors,
    /// where there are no walls for its shadow to acne against.
    /// </summary>
    public sealed class Atmosphere : MonoBehaviour
    {
        [Header("Where inside is")]
        [SerializeField, Tooltip("World bounds of the building; the player inside them gets the indoor look.")]
        private Bounds interior = new(new Vector3(0f, 1.6f, 0f), new Vector3(62f, 6f, 47f));

        [Header("Indoor")]
        [SerializeField] private Color indoorSky = new(0.56f, 0.57f, 0.64f);
        [SerializeField] private Color indoorEquator = new(0.44f, 0.44f, 0.48f);
        [SerializeField] private Color indoorGround = new(0.24f, 0.23f, 0.25f);
        [SerializeField] private Color indoorFog = new(0.10f, 0.08f, 0.09f);
        [SerializeField, Min(0f)] private float indoorFogDensity = 0.018f;

        [Header("Outdoor")]
        [SerializeField] private Color outdoorSky = new(0.50f, 0.40f, 0.46f);
        [SerializeField] private Color outdoorEquator = new(0.34f, 0.24f, 0.26f);
        [SerializeField] private Color outdoorGround = new(0.10f, 0.08f, 0.09f);
        [SerializeField] private Color outdoorFog = new(0.26f, 0.16f, 0.18f);
        [SerializeField, Min(0f), Tooltip("Exponential-squared density: 0.0032 is 86% haze at 120 m and 40% at 300 m.")]
        private float outdoorFogDensity = 0.0032f;

        [Header("Blend")]
        [SerializeField, Min(0.1f), Tooltip("Per-second sharpness of the crossfade; 3 is about a second.")]
        private float sharpness = 3f;
        [SerializeField, Tooltip("Optional post-processing volume faded in outdoors (weight = outdoor blend).")]
        private Volume outdoorVolume;
        [SerializeField, Tooltip("The player's guns: their renderers get the exterior rendering layer while outdoors.")]
        private WeaponInventory inventory;

        private const uint ExteriorRenderingLayer = 1u << 1;

        private float indoor = 1f; // 1 = fully indoor, 0 = fully outdoor
        private bool viewModelOutdoors;

        /// <summary>0 indoors .. 1 outdoors, as currently blended.</summary>
        public float Outdoor => 1f - indoor;

        /// <summary>True when the player's feet are inside the building bounds.</summary>
        public bool PlayerInside => !GameServices.TryGet(out IPlayerTarget player) || interior.Contains(player.Position);

        public void Configure(Bounds building, WeaponInventory weapons, Volume outdoors)
        {
            interior = building;
            inventory = weapons;
            outdoorVolume = outdoors;
        }

        private void Start()
        {
            indoor = PlayerInside ? 1f : 0f;
            Apply();
        }

        private void Update()
        {
            float target = PlayerInside ? 1f : 0f;
            indoor = MathUtil.Damp(indoor, target, sharpness, Time.deltaTime);
            Apply();
        }

        private void Apply()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(outdoorSky, indoorSky, indoor);
            RenderSettings.ambientEquatorColor = Color.Lerp(outdoorEquator, indoorEquator, indoor);
            RenderSettings.ambientGroundColor = Color.Lerp(outdoorGround, indoorGround, indoor);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.Lerp(outdoorFog, indoorFog, indoor);
            RenderSettings.fogDensity = Mathf.Lerp(outdoorFogDensity, indoorFogDensity, indoor);
            if (outdoorVolume != null)
            {
                outdoorVolume.weight = 1f - indoor;
            }

            bool outdoors = indoor < 0.5f;
            if (outdoors != viewModelOutdoors)
            {
                viewModelOutdoors = outdoors;
                SetViewModelSunlit(outdoors);
            }
        }

        private void SetViewModelSunlit(bool sunlit)
        {
            if (inventory == null || inventory.ViewModelSocket == null)
            {
                return;
            }

            foreach (Renderer r in inventory.ViewModelSocket.GetComponentsInChildren<Renderer>(true))
            {
                r.renderingLayerMask = sunlit ? (r.renderingLayerMask | ExteriorRenderingLayer) : (r.renderingLayerMask & ~ExteriorRenderingLayer);
            }
        }
    }
}
