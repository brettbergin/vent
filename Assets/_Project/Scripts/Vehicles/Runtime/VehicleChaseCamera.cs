using UnityEngine;
using Vent.Core.Utility;

namespace Vent.Vehicles.Runtime
{
    /// <summary>
    /// Third-person chase rig. It borrows the player's own camera rather than owning a second one:
    /// the camera (with its listener, post-processing stack and CameraMotion) is re-parented under
    /// this transform while driving, so <c>Camera.main</c>, the HUD and the roadkill shake all keep
    /// working; CameraMotion's local offsets simply compose on top of the rig pose.
    ///
    /// It follows the direction the car is travelling rather than the direction it is pointing, so
    /// a slide reads as a slide; it sits further back and lower as the car speeds up; its focus
    /// point lags the car a little so acceleration and braking are felt; the mouse orbits it, and it
    /// swings back behind the car once the driver stops looking around — quickly at speed, gently
    /// at a crawl. It pulls in rather than clipping through walls. Its forward is the crosshair:
    /// drive-by aims where it looks.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class VehicleChaseCamera : MonoBehaviour
    {
        [Header("Orbit")]
        [SerializeField, Min(1f)] private float distance = 6.2f;
        [SerializeField, Min(1f), Tooltip("Distance at top speed.")] private float distanceAtSpeed = 7.8f;
        [SerializeField] private float defaultPitch = 12f;
        [SerializeField, Tooltip("Pitch at top speed; lower flattens the view and sells the speed.")] private float pitchAtSpeed = 8f;
        [SerializeField] private float pitchMin = -8f;
        [SerializeField] private float pitchMax = 45f;
        [SerializeField, Min(0f), Tooltip("Metres above the target the camera looks at.")]
        private float lookAtLift = 0.6f;
        [SerializeField, Min(0f), Tooltip("Metres the focus leads the car along its velocity at top speed.")]
        private float lookAhead = 1.6f;

        [Header("Follow")]
        [SerializeField, Min(0f), Tooltip("Seconds without look input before the camera swings back behind the car.")]
        private float recentreDelay = 0.9f;
        [SerializeField, Min(0.1f), Tooltip("Recentre rate at a crawl, 1/s.")] private float recentreSharpnessSlow = 1.8f;
        [SerializeField, Min(0.1f), Tooltip("Recentre rate at top speed, 1/s.")] private float recentreSharpnessFast = 4.5f;
        [SerializeField, Range(0f, 1f), Tooltip("How much the camera follows the velocity direction instead of the nose at speed.")]
        private float velocityFollow = 0.65f;
        [SerializeField, Min(0.1f), Tooltip("Speed at which the velocity direction fully counts, m/s.")] private float velocityFollowSpeed = 6f;
        [SerializeField, Min(0.1f), Tooltip("How fast the focus point chases the car, 1/s; lower lags more.")] private float focusSharpness = 11f;

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
        private Vector3 velocity;
        private float speed01;
        private float currentDistance;
        private Vector3 focus;
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
            velocity = Vector3.zero;
            speed01 = 0f;
            currentDistance = distance;
            focus = target.position + Vector3.up * lookAtLift;
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

        /// <summary>The car's velocity and how close to its top speed it is; the driver reports these each frame.</summary>
        public void SetMotion(Vector3 carVelocity, float speedFraction)
        {
            velocity = carVelocity;
            speed01 = Mathf.Clamp01(speedFraction);
        }

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

            Vector3 flat = new(velocity.x, 0f, velocity.z);
            float speed = flat.magnitude;
            if (idle > recentreDelay && speed > 0.8f && heading != null)
            {
                float behind = heading.eulerAngles.y;
                float forwardSpeed = Vector3.Dot(velocity, heading.forward);
                if (forwardSpeed > 1f)
                {
                    // Travelling forward: look where the car is going, most of the way. Reversing keeps the view over the nose.
                    float travelYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                    float blend = velocityFollow * Mathf.Clamp01((speed - 1f) / velocityFollowSpeed);
                    behind += Mathf.DeltaAngle(behind, travelYaw) * blend;
                }

                float sharpness = Mathf.Lerp(recentreSharpnessSlow, recentreSharpnessFast, speed01);
                yaw = MathUtil.Damp(yaw, yaw + Mathf.DeltaAngle(yaw, behind), sharpness, dt);
                pitch = MathUtil.Damp(pitch, Mathf.Lerp(defaultPitch, pitchAtSpeed, speed01), sharpness, dt);
            }

            kickCurrent = MathUtil.Damp(kickCurrent, kickTarget, kickSharpness, dt);
            kickTarget = MathUtil.Damp(kickTarget, Vector2.zero, kickRecovery, dt);
            Solve(snap: false);
        }

        private void Solve(bool snap)
        {
            float dt = Time.deltaTime;
            Vector3 flat = new(velocity.x, 0f, velocity.z);
            Vector3 lead = flat.sqrMagnitude > 0.01f ? flat.normalized * (lookAhead * speed01) : Vector3.zero;
            Vector3 focusTarget = target.position + Vector3.up * lookAtLift + lead;
            focus = snap ? focusTarget : MathUtil.Damp(focus, focusTarget, focusSharpness, dt);

            var rot = Quaternion.Euler(pitch - kickCurrent.x, yaw + kickCurrent.y, 0f);
            Vector3 back = rot * Vector3.back;
            float wanted = Mathf.Lerp(distance, distanceAtSpeed, speed01);
            float allowed = wanted;
            if (Physics.SphereCast(focus, collisionRadius, back, out RaycastHit hit, wanted, Layers.OcclusionMask, QueryTriggerInteraction.Ignore))
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
