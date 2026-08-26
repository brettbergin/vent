using UnityEngine;
using Vent.Core.Utility;

namespace Vent.Vehicles.Runtime
{
    /// <summary>
    /// The driver's arm out of the window with the pistol in it. Third-person drive-by needs
    /// something to shoot from; this pivots toward the aim point within what a shoulder allows and
    /// kicks back on each shot. Purely visual: the hitscan comes from the camera, the flash from
    /// the muzzle transform at the end of this arm.
    /// </summary>
    public sealed class VehicleDriveByArm : MonoBehaviour
    {
        [Header("Reach (degrees, local)")]
        [SerializeField, Tooltip("Yaw range around the window; the driver sits on the left, so most of the arc is to the left and rear.")]
        private Vector2 yawRange = new(-170f, 15f);
        [SerializeField] private Vector2 pitchRange = new(-25f, 25f);
        [SerializeField, Min(0.5f)] private float aimSharpness = 14f;

        [Header("Kick")]
        [SerializeField, Min(0f)] private float kickDegrees = 12f;
        [SerializeField, Min(0.5f)] private float kickRecovery = 18f;

        private Quaternion target = Quaternion.identity;
        private float kick;

        /// <summary>Point the pistol at a world position (clamped to the window's arc).</summary>
        public void SetAimPoint(Vector3 world)
        {
            Transform parent = transform.parent != null ? transform.parent : transform;
            Vector3 local = parent.InverseTransformDirection(world - transform.position);
            if (local.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Asin(Mathf.Clamp(local.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            yaw = Mathf.Clamp(yaw, yawRange.x, yawRange.y);
            pitch = Mathf.Clamp(pitch, pitchRange.x, pitchRange.y);
            target = Quaternion.Euler(pitch, yaw, 0f);
        }

        /// <summary>Recoil: the muzzle flips up and settles back.</summary>
        public void Kick(float strength) => kick = Mathf.Max(kick, kickDegrees * Mathf.Clamp(strength, 0.5f, 2f));

        private void Update()
        {
            float dt = Time.deltaTime;
            kick = MathUtil.Damp(kick, 0f, kickRecovery, dt);
            Quaternion desired = target * Quaternion.Euler(-kick, 0f, 0f);
            transform.localRotation = MathUtil.Damp(transform.localRotation, desired, aimSharpness, dt);
        }
    }
}
