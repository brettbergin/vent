using UnityEngine;
using Vent.Core.Utility;

namespace Vent.Vehicles.Runtime
{
    /// <summary>
    /// Third-person chase rig. It borrows the player's own camera rather than owning a second one:
    /// the camera (with its listener, post-processing stack and CameraMotion) is re-parented under
    /// this transform while driving, so <c>Camera.main</c>, the HUD and the roadkill shake all keep
    /// working; CameraMotion's local offsets simply compose on top of the rig pose. Orbits the car
    /// on mouse input, drifts back behind it when the driver stops looking around, and pulls in
    /// rather than clipping through walls. Its forward is the crosshair: drive-by aims where it looks.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class VehicleChaseCamera : MonoBehaviour
    {
        [Header("Orbit")]
        [SerializeField, Min(1f)] private float distance = 6f;
        [SerializeField] private float defaultPitch = 10f;
        [SerializeField] private float pitchMin = -10f;
        [SerializeField] private float pitchMax = 45f;
        [SerializeField, Min(0f), Tooltip("Metres above the target the camera looks at.")]
        private float lookAtLift = 0.5f;

        [Header("Recentre")]
        [SerializeField, Min(0f), Tooltip("Seconds without look input before the camera swings back behind the car.")]
        private float recentreDelay = 1.5f;
        [SerializeField, Min(0.1f)] private float recentreSharpness = 2.5f;

        [Header("Smoothing")]
        [SerializeField, Min(0.1f)] private float positionSharpness = 14f;
        [SerializeField, Min(0.05f)] private float collisionRadius = 0.3f;
        [SerializeField, Min(0.5f)] private float minDistance = 1.2f;

        [Header("Kick")]
        [SerializeField, Min(0.1f)] private float kickSharpness = 40f;
        [SerializeField, Min(0.1f)] private float kickRecovery = 12f;

        private Transform target;
        private Transform heading;
        private Transform cam;
        private Transform camHome;
        private Vector3 camHomePosition;
        private Quaternion camHomeRotation;
        private float yaw;
        private float pitch;
        private float idle;
        private float headingSpeed;
        private float currentDistance;
        private Vector2 kickTarget;
        private Vector2 kickCurrent;
        private Vector2 orbitDelta;

        public bool IsAttached => cam != null;
        public float Yaw => yaw;
        public float Pitch => pitch;

        /// <summary>Take the camera: orbit <paramref name="followTarget"/>, recentre behind <paramref name="carHeading"/>.</summary>
        public void Attach(Transform followTarget, Transform carHeading, Transform camera)
        {
            target = followTarget;
            heading = carHeading;
            cam = camera;
            camHome = camera.parent;
            camHomePosition = camera.localPosition;
            camHomeRotation = camera.localRotation;
            camera.SetParent(transform, worldPositionStays: false);
            camera.localPosition = Vector3.zero;
            camera.localRotation = Quaternion.identity;
            yaw = heading.eulerAngles.y;
            pitch = defaultPitch;
            idle = 0f;
            currentDistance = distance;
            kickTarget = kickCurrent = Vector2.zero;
            Solve(snap: true);
        }

        /// <summary>Give the camera back to wherever it came from, at its rest pose.</summary>
        public void Detach()
        {
            if (cam == null)
            {
                return;
            }

            cam.SetParent(camHome, worldPositionStays: false);
            cam.localPosition = camHomePosition;
            cam.localRotation = camHomeRotation;
            cam = null;
            target = null;
            heading = null;
        }

        /// <summary>Look input in degrees: x = yaw, y = pitch (positive looks down, as in PlayerLook).</summary>
        public void AddOrbit(Vector2 degrees) => orbitDelta += degrees;

        /// <summary>Recoil kick in degrees: x = pitch up, y = yaw.</summary>
        public void AddKick(Vector2 pitchYawDegrees) => kickTarget += pitchYawDegrees;

        public void SetHeadingSpeed(float metresPerSecond) => headingSpeed = metresPerSecond;

        private void LateUpdate()
        {
            if (cam == null || target == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            if (orbitDelta.sqrMagnitude > 0.0001f)
            {
                yaw += orbitDelta.x;
                pitch = Mathf.Clamp(pitch + orbitDelta.y, pitchMin, pitchMax);
                idle = 0f;
            }
            else
            {
                idle += dt;
            }

            orbitDelta = Vector2.zero;

            if (idle > recentreDelay && headingSpeed > 1f && heading != null)
            {
                float behind = heading.eulerAngles.y;
                yaw = MathUtil.Damp(yaw, yaw + Mathf.DeltaAngle(yaw, behind), recentreSharpness, dt);
                pitch = MathUtil.Damp(pitch, defaultPitch, recentreSharpness, dt);
            }

            kickCurrent = MathUtil.Damp(kickCurrent, kickTarget, kickSharpness, dt);
            kickTarget = MathUtil.Damp(kickTarget, Vector2.zero, kickRecovery, dt);
            Solve(snap: false);
        }

        private void Solve(bool snap)
        {
            float dt = Time.deltaTime;
            var rot = Quaternion.Euler(pitch - kickCurrent.x, yaw + kickCurrent.y, 0f);
            Vector3 focus = target.position + Vector3.up * lookAtLift;
            Vector3 back = rot * Vector3.back;
            float allowed = distance;
            if (Physics.SphereCast(focus, collisionRadius, back, out RaycastHit hit, distance, Layers.OcclusionMask, QueryTriggerInteraction.Ignore))
            {
                allowed = Mathf.Max(minDistance, hit.distance - 0.05f);
            }

            // Pull in instantly (never clip), ease back out (never pop).
            currentDistance = snap || allowed < currentDistance ? allowed : MathUtil.Damp(currentDistance, allowed, positionSharpness, dt);
            Vector3 position = focus + back * currentDistance;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position, Vector3.up));
        }
    }
}
