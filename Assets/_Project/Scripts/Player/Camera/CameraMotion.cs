using UnityEngine;
using Vent.Core.Utility;
using Vent.Player.Movement;

namespace Vent.Player.Camera
{
    /// <summary>
    /// Procedural camera motion layered on top of look rotation: head-bob while moving, a dip on
    /// landing, and a brief shake on damage. All motion is expressed as a local offset on the
    /// camera transform, so it never fights <see cref="PlayerLook"/>, which owns rotation of the parent.
    /// </summary>
    public sealed class CameraMotion : MonoBehaviour
    {
        [SerializeField] private FirstPersonController controller;

        [Header("Head Bob")]
        [SerializeField, Min(0f)] private float bobFrequency = 1.9f;
        [SerializeField, Min(0f)] private float bobVerticalAmplitude = 0.035f;
        [SerializeField, Min(0f)] private float bobHorizontalAmplitude = 0.02f;
        [SerializeField, Min(0f)] private float bobSprintMultiplier = 1.4f;

        [Header("Landing")]
        [SerializeField, Min(0f)] private float landDipAmount = 0.08f;
        [SerializeField, Min(0f)] private float landRecoverySharpness = 10f;

        [Header("Shake")]
        [SerializeField, Min(0f)] private float shakeDecaySharpness = 8f;
        [SerializeField, Min(0f)] private float shakeFrequency = 28f;

        private Vector3 restLocalPosition;
        private float bobPhase;
        private float landOffset;
        private float shakeAmplitude;
        private bool wasGrounded = true;
        private float lastVerticalVelocity;

        public FirstPersonController Controller
        {
            get => controller;
            set => controller = value;
        }

        private void Awake() => restLocalPosition = transform.localPosition;

        /// <summary>Kick off a shake (damage, nearby explosion). Amplitude in metres.</summary>
        public void Shake(float amplitude) => shakeAmplitude = Mathf.Max(shakeAmplitude, amplitude);

        private void LateUpdate()
        {
            if (controller == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            float speed01 = controller.Speed01;
            bool grounded = controller.IsGrounded;

            // --- Bob ---
            Vector3 bob = Vector3.zero;
            if (grounded && speed01 > 0.05f)
            {
                float mult = controller.IsSprinting ? bobSprintMultiplier : 1f;
                bobPhase += dt * bobFrequency * Mathf.PI * 2f * mult;
                bob.y = Mathf.Sin(bobPhase * 2f) * bobVerticalAmplitude * speed01;
                bob.x = Mathf.Cos(bobPhase) * bobHorizontalAmplitude * speed01;
            }
            else
            {
                bobPhase = MathUtil.Damp(bobPhase, Mathf.Round(bobPhase / Mathf.PI) * Mathf.PI, 6f, dt);
            }

            // --- Landing dip ---
            if (grounded && !wasGrounded)
            {
                float impact = Mathf.Clamp01(-lastVerticalVelocity / 12f);
                landOffset = -landDipAmount * impact;
            }

            wasGrounded = grounded;
            lastVerticalVelocity = controller.Velocity.y;
            landOffset = MathUtil.Damp(landOffset, 0f, landRecoverySharpness, dt);

            // --- Shake ---
            Vector3 shake = Vector3.zero;
            if (shakeAmplitude > 0.0005f)
            {
                float t = Time.time * shakeFrequency;
                shake = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * (2f * shakeAmplitude);
                shakeAmplitude = MathUtil.Damp(shakeAmplitude, 0f, shakeDecaySharpness, dt);
            }

            transform.localPosition = restLocalPosition + bob + Vector3.up * landOffset + shake;
        }
    }
}
