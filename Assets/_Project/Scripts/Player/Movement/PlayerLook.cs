using System;
using UnityEngine;
using Vent.Core.Settings;
using Vent.Core.Utility;
using Vent.Player.Input;
using Vent.Weapons;

namespace Vent.Player.Movement
{
    /// <summary>
    /// Mouse/stick look: yaw rotates the body (so movement follows the view), pitch rotates the
    /// camera pivot only. Weapons push recoil into this component; it is applied as a decaying
    /// offset on top of the player's own aim so the player never "loses" their input.
    /// </summary>
    public sealed class PlayerLook : MonoBehaviour, IRecoilReceiver
    {
        [Header("References")]
        [SerializeField] private InputReader input;
        [SerializeField, Tooltip("Rotated on X for pitch. Usually the camera's parent.")]
        private Transform pitchPivot;

        [Header("Sensitivity")]
        [SerializeField, Min(0.01f), Tooltip("Degrees per mouse pixel at sensitivity 1.")]
        private float mouseDegreesPerPixel = 0.08f;
        [SerializeField, Min(1f), Tooltip("Degrees per second at full stick deflection.")]
        private float stickDegreesPerSecond = 160f;
        [SerializeField, Min(0.05f)] private float sensitivity = 1f;
        [SerializeField] private bool invertY;
        [SerializeField, Range(0.1f, 1f), Tooltip("Sensitivity multiplier while aiming down sights.")]
        private float aimSensitivityScale = 0.6f;

        [Header("Limits")]
        [SerializeField, Range(0f, 89.9f)] private float maxPitch = 89f;

        [Header("Recoil")]
        [SerializeField, Min(0f), Tooltip("How quickly recoil offset decays back to zero (per second).")]
        private float recoilRecoverySharpness = 12f;
        [SerializeField, Min(0f), Tooltip("How quickly a new kick blends in (per second). Higher = snappier.")]
        private float recoilKickSharpness = 40f;

        private float yaw;
        private float pitch;
        private Vector2 recoilTarget;   // accumulated kick (pitch, yaw) in degrees
        private Vector2 recoilCurrent;  // smoothed offset applied this frame
        private bool lookEnabled = true;
        private bool aiming;
        private bool seated;

        /// <summary>
        /// While seated the look input is not applied here: it is handed to whoever drives the camera
        /// (the chase rig), as degrees of yaw and pitch with the player's sensitivity and invert applied.
        /// </summary>
        public event Action<Vector2> SeatedLook;

        /// <summary>Where recoil goes while seated (the chase rig); null discards it.</summary>
        public IRecoilReceiver SeatedRecoil { get; set; }

        public bool IsSeated => seated;

        public void SetSeated(bool value) => seated = value;

        public float Sensitivity
        {
            get => sensitivity;
            set => sensitivity = Mathf.Max(0.05f, value);
        }

        public bool InvertY
        {
            get => invertY;
            set => invertY = value;
        }

        public InputReader Input
        {
            get => input;
            set => input = value;
        }

        public Transform PitchPivot
        {
            get => pitchPivot;
            set => pitchPivot = value;
        }

        /// <summary>Current camera pitch in degrees (negative = looking up).</summary>
        public float Pitch => pitch - recoilCurrent.x;

        public void SetLookEnabled(bool enabled) => lookEnabled = enabled;
        public void SetAiming(bool isAiming) => aiming = isAiming;

        /// <inheritdoc/>
        public void AddRecoil(Vector2 pitchYawDegrees)
        {
            if (seated)
            {
                SeatedRecoil?.AddRecoil(pitchYawDegrees);
                return;
            }

            recoilTarget += pitchYawDegrees;
        }

        /// <summary>Snap to an orientation (used on spawn).</summary>
        public void SetRotation(float yawDegrees, float pitchDegrees)
        {
            yaw = yawDegrees;
            pitch = Mathf.Clamp(pitchDegrees, -maxPitch, maxPitch);
            recoilTarget = Vector2.zero;
            recoilCurrent = Vector2.zero;
            Apply();
        }

        private void OnEnable()
        {
            ApplySettings();
            SettingsStore.Changed += ApplySettings;
        }

        private void OnDisable() => SettingsStore.Changed -= ApplySettings;

        private void ApplySettings()
        {
            sensitivity = SettingsStore.Sensitivity;
            invertY = SettingsStore.InvertY;
        }

        private void Start()
        {
            yaw = transform.eulerAngles.y;
            pitch = pitchPivot != null ? NormalizeAngle(pitchPivot.localEulerAngles.x) : 0f;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;

            if (lookEnabled && input != null)
            {
                float scale = sensitivity * (aiming ? aimSensitivityScale : 1f);
                Vector2 mouse = input.LookDelta * (mouseDegreesPerPixel * scale);
                Vector2 stick = input.LookAnalog * (stickDegreesPerSecond * scale * dt);
                Vector2 delta = mouse + stick;
                input.ConsumeLookDelta();

                if (seated)
                {
                    // Same sign convention as below: positive y looks down.
                    SeatedLook?.Invoke(new Vector2(delta.x, invertY ? delta.y : -delta.y));
                    return;
                }

                yaw += delta.x;
                pitch += invertY ? delta.y : -delta.y;
                pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);
            }
            else if (seated)
            {
                return;
            }

            // Recoil: kick blends toward the accumulated target, and the target itself decays.
            recoilCurrent = MathUtil.Damp(recoilCurrent, recoilTarget, recoilKickSharpness, dt);
            recoilTarget = MathUtil.Damp(recoilTarget, Vector2.zero, recoilRecoverySharpness, dt);

            Apply();
        }

        private void Apply()
        {
            transform.rotation = Quaternion.Euler(0f, yaw + recoilCurrent.y, 0f);
            if (pitchPivot != null)
            {
                float finalPitch = Mathf.Clamp(pitch - recoilCurrent.x, -maxPitch, maxPitch);
                pitchPivot.localRotation = Quaternion.Euler(finalPitch, 0f, 0f);
            }
        }

        private static float NormalizeAngle(float degrees)
        {
            degrees %= 360f;
            return degrees > 180f ? degrees - 360f : degrees;
        }
    }
}
