using UnityEngine;

namespace Vent.Vehicles.Runtime
{
    /// <summary>
    /// Headlamps that come on with the engine and tail lamps that brighten under braking or go white
    /// in reverse. The lamps are the prefab's emissive panels; while the car is live their emission
    /// is overridden through a property block, and cleared again when it parks, so the twenty
    /// parked cars keep sharing one material.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public sealed class VehicleLights : MonoBehaviour
    {
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private Light[] headlights;
        [SerializeField] private Renderer[] headLamps;
        [SerializeField] private Renderer[] tailLamps;
        [SerializeField, ColorUsage(false, true)] private Color headOn = new Color(1f, 0.95f, 0.8f) * 6f;
        [SerializeField, ColorUsage(false, true)] private Color tailOn = new Color(1f, 0.1f, 0.05f) * 2.5f;
        [SerializeField, ColorUsage(false, true)] private Color tailBrake = new Color(1f, 0.06f, 0.03f) * 6f; // bright, but under the bloom threshold that turns red white
        [SerializeField, ColorUsage(false, true)] private Color tailReverse = new Color(1f, 0.95f, 0.85f) * 4f;

        private VehicleController controller;
        private MaterialPropertyBlock block;
        private int tailState = -1;

        public Light[] Headlights => headlights;

        public void Configure(Light[] lamps, Renderer[] frontRenderers, Renderer[] rearRenderers)
        {
            headlights = lamps;
            headLamps = frontRenderers;
            tailLamps = rearRenderers;
        }

        private void Awake()
        {
            controller = GetComponent<VehicleController>();
            block = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            controller.OccupiedChanged += OnOccupied;
            OnOccupied(controller.IsOccupied);
        }

        private void OnDisable() => controller.OccupiedChanged -= OnOccupied;

        private void OnOccupied(bool on)
        {
            foreach (Light light in headlights)
            {
                if (light != null)
                {
                    light.enabled = on;
                }
            }

            if (on)
            {
                Apply(headLamps, headOn);
                tailState = -1;
            }
            else
            {
                Clear(headLamps);
                Clear(tailLamps);
                tailState = -1;
            }
        }

        private void Update()
        {
            if (!controller.IsOccupied)
            {
                return;
            }

            int state = controller.IsReversing ? 2 : controller.IsBraking ? 1 : 0;
            if (state == tailState)
            {
                return;
            }

            tailState = state;
            Apply(tailLamps, state == 2 ? tailReverse : state == 1 ? tailBrake : tailOn);
        }

        private void Apply(Renderer[] renderers, Color emission)
        {
            if (renderers == null)
            {
                return;
            }

            block.Clear();
            block.SetColor(EmissionColor, emission);
            foreach (Renderer r in renderers)
            {
                if (r != null)
                {
                    r.SetPropertyBlock(block);
                }
            }
        }

        private static void Clear(Renderer[] renderers)
        {
            if (renderers == null)
            {
                return;
            }

            foreach (Renderer r in renderers)
            {
                if (r != null)
                {
                    r.SetPropertyBlock(null);
                }
            }
        }
    }
}
