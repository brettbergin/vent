using UnityEngine;
using Vent.Core.Utility;

namespace Vent.Weapons.View
{
    /// <summary>
    /// Procedural first-person weapon animation. There are no animation clips in this project;
    /// draw, sway, bob, recoil kick and reload are all computed here from a few numbers so the
    /// behaviour is readable and tweakable in the inspector.
    ///
    /// Everything is expressed as an offset from the "rest" pose (hip or aim), applied to this
    /// transform, which is a child of the weapon socket under the camera.
    /// </summary>
    public sealed class WeaponViewModel : MonoBehaviour
    {
        [Header("Anchors")]
        [SerializeField, Tooltip("Where shots originate and the muzzle flash attaches.")]
        private Transform muzzle;
        [SerializeField] private Vector3 hipPosition = new(0.22f, -0.2f, 0.45f);
        [SerializeField] private Vector3 aimPosition = new(0f, -0.13f, 0.3f);
        [SerializeField, Min(0f)] private float aimSharpness = 14f;

        [Header("Sway (look input)")]
        [SerializeField, Min(0f)] private float swayAmount = 0.0025f;
        [SerializeField, Min(0f)] private float swayMax = 0.04f;
        [SerializeField, Min(0f)] private float swayRotation = 0.35f;
        [SerializeField, Min(0f)] private float swaySharpness = 10f;

        [Header("Bob (movement)")]
        [SerializeField, Min(0f)] private float bobFrequency = 1.9f;
        [SerializeField, Min(0f)] private float bobAmount = 0.012f;

        [Header("Kick (per shot)")]
        [SerializeField, Min(0f)] private float kickBack = 0.05f;
        [SerializeField, Min(0f)] private float kickUpDegrees = 4f;
        [SerializeField, Min(0f)] private float kickRecovery = 18f;

        [Header("Reload / Draw")]
        [SerializeField] private Vector3 reloadOffset = new(0f, -0.15f, 0f);
        [SerializeField] private Vector3 reloadRotation = new(-20f, 0f, 25f);
        [SerializeField] private Vector3 drawOffset = new(0f, -0.35f, 0f);
        [SerializeField] private Vector3 drawRotation = new(35f, 0f, 0f);

        private bool aiming;
        private float speed01;
        private bool grounded = true;
        private Vector2 lookDelta;
        private float bobPhase;
        private Vector3 swayPos;
        private Vector3 swayRot;
        private float kick;
        private float reloadT = 1f;
        private float reloadDuration = 1f;
        private float drawT = 1f;
        private float drawDuration = 0.3f;
        private Vector3 restPosition;

        /// <summary>Shot origin. Falls back to this transform if none is assigned.</summary>
        public Transform Muzzle => muzzle != null ? muzzle : transform;

        public void SetMuzzle(Transform t) => muzzle = t;

        public void SetAiming(bool value) => aiming = value;

        public void SetMotion(float movementSpeed01, bool isGrounded, Vector2 look)
        {
            speed01 = movementSpeed01;
            grounded = isGrounded;
            lookDelta = look;
        }

        public void Kick(float strength = 1f) => kick = Mathf.Min(kick + strength, 2.5f);

        public void PlayReload(float seconds)
        {
            reloadDuration = Mathf.Max(0.05f, seconds);
            reloadT = 0f;
        }

        public void PlayDraw(float seconds)
        {
            drawDuration = Mathf.Max(0.05f, seconds);
            drawT = 0f;
        }

        private void OnEnable()
        {
            restPosition = aiming ? aimPosition : hipPosition;
            swayPos = Vector3.zero;
            swayRot = Vector3.zero;
            kick = 0f;
            reloadT = 1f;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;

            // Rest pose (hip vs aim). Damped separately from the composed local position so the
            // per-frame offsets below never feed back into next frame's base.
            Vector3 targetRest = aiming ? aimPosition : hipPosition;
            restPosition = MathUtil.Damp(restPosition, targetRest, aimSharpness, dt);
            Vector3 basePos = restPosition;

            // Sway: the gun lags behind look input.
            float swayScale = aiming ? 0.3f : 1f;
            Vector3 swayTargetPos = new(
                Mathf.Clamp(-lookDelta.x * swayAmount, -swayMax, swayMax) * swayScale,
                Mathf.Clamp(-lookDelta.y * swayAmount, -swayMax, swayMax) * swayScale,
                0f);
            Vector3 swayTargetRot = new(
                Mathf.Clamp(lookDelta.y * swayRotation, -8f, 8f) * swayScale,
                Mathf.Clamp(-lookDelta.x * swayRotation, -8f, 8f) * swayScale,
                Mathf.Clamp(-lookDelta.x * swayRotation * 0.5f, -5f, 5f) * swayScale);
            swayPos = MathUtil.Damp(swayPos, swayTargetPos, swaySharpness, dt);
            swayRot = MathUtil.Damp(swayRot, swayTargetRot, swaySharpness, dt);

            // Bob
            Vector3 bob = Vector3.zero;
            if (grounded && speed01 > 0.05f)
            {
                bobPhase += dt * bobFrequency * Mathf.PI * 2f;
                float amt = bobAmount * speed01 * (aiming ? 0.3f : 1f);
                bob = new Vector3(Mathf.Cos(bobPhase) * amt, Mathf.Sin(bobPhase * 2f) * amt * 0.6f, 0f);
            }

            // Kick
            kick = MathUtil.Damp(kick, 0f, kickRecovery, dt);
            Vector3 kickPos = new(0f, 0f, -kickBack * kick);
            Vector3 kickRot = new(-kickUpDegrees * kick, 0f, 0f);

            // Reload: a dip-and-twist that eases out over the reload duration.
            float reloadWeight = 0f;
            if (reloadT < 1f)
            {
                reloadT = Mathf.Min(1f, reloadT + dt / reloadDuration);
                reloadWeight = Mathf.Sin(reloadT * Mathf.PI); // 0 → 1 → 0
            }

            // Draw: rises from below over drawDuration.
            float drawWeight = 0f;
            if (drawT < 1f)
            {
                drawT = Mathf.Min(1f, drawT + dt / drawDuration);
                drawWeight = 1f - Mathf.SmoothStep(0f, 1f, drawT);
            }

            transform.localPosition = basePos + swayPos + bob + kickPos + reloadOffset * reloadWeight + drawOffset * drawWeight;
            transform.localRotation = Quaternion.Euler(swayRot + kickRot + reloadRotation * reloadWeight + drawRotation * drawWeight);
        }
    }
}
