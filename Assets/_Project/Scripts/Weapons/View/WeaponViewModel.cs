using UnityEngine;
using Vent.Core.Utility;

namespace Vent.Weapons.View
{
    /// <summary>
    /// Procedural first-person weapon animation. There are no animation clips in this project;
    /// draw, sway, bob, sprint carry, landing dip, recoil kick, slide cycle and the phased reload
    /// are all computed here from a few numbers so the behaviour is readable and tweakable.
    ///
    /// Everything is expressed as an offset from the "rest" pose (hip or aim), applied to this
    /// transform, which is a child of the weapon socket under the camera. Optional part transforms
    /// (magazine, slide) are animated in place so the gun visibly does what the sounds say.
    /// </summary>
    public sealed class WeaponViewModel : MonoBehaviour
    {
        /// <summary>Reload progress at which the fresh magazine seats (mag-in sound, magazine snaps back).</summary>
        public const float ReloadMagInAt = 0.55f;
        /// <summary>Reload progress at which the action is racked on an empty reload (rack sound, slide/bolt snaps).</summary>
        public const float ReloadRackAt = 0.82f;

        [Header("Anchors")]
        [SerializeField, Tooltip("Where shots originate and the muzzle flash attaches.")]
        private Transform muzzle;
        [SerializeField, Tooltip("Where casings fly out; +X is the ejection direction.")]
        private Transform ejectionPort;
        [SerializeField, Tooltip("Optional: dropped and reseated during a reload.")]
        private Transform magazine;
        [SerializeField, Tooltip("Optional: cycles on each shot and locks back on empty (pistol slide / SMG bolt).")]
        private Transform slide;
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
        [SerializeField, Tooltip("Carry pose while sprinting: gun drops and cants away from the eye.")]
        private Vector3 sprintOffset = new(0.04f, -0.06f, -0.04f);
        [SerializeField] private Vector3 sprintRotation = new(18f, -25f, 12f);
        [SerializeField, Min(0f)] private float sprintSharpness = 8f;
        [SerializeField, Min(0f), Tooltip("Dip when landing from a jump.")]
        private float landDip = 0.05f;

        [Header("Kick (per shot)")]
        [SerializeField, Min(0f)] private float kickBack = 0.05f;
        [SerializeField, Min(0f)] private float kickUpDegrees = 4f;
        [SerializeField, Min(0f)] private float kickRecovery = 18f;
        [SerializeField, Min(0f), Tooltip("How far the slide/bolt travels rearward per shot.")]
        private float slideTravel = 0.03f;
        [SerializeField, Min(0f)] private float slideRecovery = 30f;

        [Header("Reload / Draw")]
        [SerializeField] private Vector3 reloadOffset = new(0f, -0.12f, 0f);
        [SerializeField] private Vector3 reloadRotation = new(-18f, 0f, 30f);
        [SerializeField, Tooltip("How far the magazine drops out of the well.")]
        private float magazineDrop = 0.12f;
        [SerializeField] private Vector3 drawOffset = new(0f, -0.35f, 0f);
        [SerializeField] private Vector3 drawRotation = new(35f, 0f, 0f);

        private bool aiming;
        private float speed01;
        private bool grounded = true;
        private bool wasGrounded = true;
        private Vector2 lookDelta;
        private float bobPhase;
        private Vector3 swayPos;
        private Vector3 swayRot;
        private float sprintWeight;
        private float landing;
        private float kick;
        private float slideBack;
        private bool slideLocked;
        private float reloadT = 1f;
        private float reloadDuration = 1f;
        private bool reloadEmpty;
        private float drawT = 1f;
        private float drawDuration = 0.3f;
        private Vector3 restPosition;
        private Vector3 magazineRest;
        private Vector3 slideRest;

        /// <summary>Shot origin. Falls back to this transform if none is assigned.</summary>
        public Transform Muzzle => muzzle != null ? muzzle : transform;

        /// <summary>Casing origin. Falls back to the muzzle.</summary>
        public Transform EjectionPort => ejectionPort != null ? ejectionPort : Muzzle;

        public void SetMuzzle(Transform t) => muzzle = t;

        /// <summary>Editor factory: wire the animated parts.</summary>
        public void SetParts(Transform ejection, Transform mag, Transform slideOrBolt)
        {
            ejectionPort = ejection;
            magazine = mag;
            slide = slideOrBolt;
        }

        public void SetAiming(bool value) => aiming = value;

        public void SetMotion(float movementSpeed01, bool isGrounded, Vector2 look)
        {
            speed01 = movementSpeed01;
            grounded = isGrounded;
            lookDelta = look;
        }

        /// <summary>A shot went off: kick (scaled by the recoil ramp) and cycle the slide; lock it back if the gun ran dry.</summary>
        public void OnShot(float strength, bool magazineNowEmpty)
        {
            kick = Mathf.Min(kick + strength, 2.5f);
            slideBack = 1f;
            slideLocked = magazineNowEmpty;
        }

        /// <summary>Slide/bolt goes forward when there is a round to chamber, stays back otherwise.</summary>
        public void SetChambered(bool chambered) => slideLocked = !chambered;

        public void PlayReload(float seconds, bool fromEmpty)
        {
            reloadDuration = Mathf.Max(0.05f, seconds);
            reloadT = 0f;
            reloadEmpty = fromEmpty;
        }

        public void PlayDraw(float seconds)
        {
            drawDuration = Mathf.Max(0.05f, seconds);
            drawT = 0f;
        }

        private void Awake()
        {
            if (magazine != null) magazineRest = magazine.localPosition;
            if (slide != null) slideRest = slide.localPosition;
        }

        private void OnEnable()
        {
            restPosition = aiming ? aimPosition : hipPosition;
            swayPos = Vector3.zero;
            swayRot = Vector3.zero;
            kick = 0f;
            slideBack = 0f;
            sprintWeight = 0f;
            landing = 0f;
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
                bobPhase += dt * bobFrequency * Mathf.PI * 2f * (speed01 > 0.8f ? 1.35f : 1f);
                float amt = bobAmount * speed01 * (aiming ? 0.3f : 1f);
                bob = new Vector3(Mathf.Cos(bobPhase) * amt, Mathf.Sin(bobPhase * 2f) * amt * 0.6f, 0f);
            }

            // Sprint carry: only when moving flat-out, on the ground, not aiming, not reloading.
            bool sprinting = grounded && speed01 > 0.8f && !aiming && reloadT >= 1f;
            sprintWeight = MathUtil.Damp(sprintWeight, sprinting ? 1f : 0f, sprintSharpness, dt);

            // Landing dip
            if (grounded && !wasGrounded)
            {
                landing = 1f;
            }

            wasGrounded = grounded;
            landing = MathUtil.Damp(landing, 0f, 9f, dt);

            // Kick
            kick = MathUtil.Damp(kick, 0f, kickRecovery, dt);
            Vector3 kickPos = new(0f, 0f, -kickBack * kick);
            Vector3 kickRot = new(-kickUpDegrees * kick, 0f, 0f);

            // Reload: dip-and-cant that eases out; the magazine drops out and snaps back in at ReloadMagInAt,
            // and on an empty reload the slide/bolt is racked at ReloadRackAt.
            float reloadWeight = 0f;
            float magOut = 0f;
            if (reloadT < 1f)
            {
                reloadT = Mathf.Min(1f, reloadT + dt / reloadDuration);
                reloadWeight = Mathf.Sin(reloadT * Mathf.PI);
                magOut = reloadT < ReloadMagInAt
                    ? Mathf.SmoothStep(0f, 1f, reloadT / (ReloadMagInAt * 0.6f))          // out fast, then hang
                    : 1f - Mathf.SmoothStep(0f, 1f, (reloadT - ReloadMagInAt) / 0.1f);     // snap back in
                if (reloadEmpty && reloadT >= ReloadRackAt && reloadT < ReloadRackAt + 0.02f)
                {
                    slideBack = 1f; // rack
                    slideLocked = false;
                }
            }

            // Draw: rises from below over drawDuration.
            float drawWeight = 0f;
            if (drawT < 1f)
            {
                drawT = Mathf.Min(1f, drawT + dt / drawDuration);
                drawWeight = 1f - Mathf.SmoothStep(0f, 1f, drawT);
            }

            transform.localPosition = basePos + swayPos + bob + kickPos
                                      + reloadOffset * reloadWeight + drawOffset * drawWeight
                                      + sprintOffset * sprintWeight + Vector3.down * (landDip * landing);
            transform.localRotation = Quaternion.Euler(swayRot + kickRot + reloadRotation * reloadWeight + drawRotation * drawWeight
                                                       + sprintRotation * sprintWeight + new Vector3(6f * landing, 0f, 0f));

            // Parts
            if (magazine != null)
            {
                magazine.localPosition = magazineRest + Vector3.down * (magazineDrop * magOut);
            }

            if (slide != null)
            {
                slideBack = MathUtil.Damp(slideBack, slideLocked ? 0.85f : 0f, slideRecovery, dt);
                slide.localPosition = slideRest + Vector3.back * (slideTravel * slideBack);
            }
        }
    }
}
