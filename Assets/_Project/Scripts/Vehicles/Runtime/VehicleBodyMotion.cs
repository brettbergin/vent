using UnityEngine;
using Vent.Core.Utility;

namespace Vent.Vehicles.Runtime
{
    /// <summary>
    /// The body's lean. The physics keeps the chassis flat on purpose (cornering forces act at the
    /// centre of mass so the car cannot roll), so the roll into a corner, the squat under power, the
    /// dive under braking and the drop over a kerb are painted on here: the visual body tilts and
    /// sinks with the filtered acceleration and the mean suspension compression. The colliders live
    /// outside this transform, so nothing physical moves.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public sealed class VehicleBodyMotion : MonoBehaviour
    {
        [SerializeField, Tooltip("The visual body; colliders must not be under it.")]
        private Transform body;
        [SerializeField, Min(0.5f)] private float sharpness = 9f;
        [SerializeField, Tooltip("Mean compression with the car parked; the body drops below its rest pose when the springs go past it.")]
        private float restCompression = 0.08f;

        private VehicleController controller;
        private Vector3 restPosition;
        private float roll;
        private float pitch;
        private float lift;

        public void Configure(Transform visualBody, float parkedCompression)
        {
            body = visualBody;
            restCompression = parkedCompression;
        }

        private void Awake()
        {
            controller = GetComponent<VehicleController>();
            if (body != null)
            {
                restPosition = body.localPosition;
            }
        }

        private void Update()
        {
            if (body == null || controller.Definition == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            bool live = controller.Body != null && !controller.Body.isKinematic;
            Vector3 g = live ? controller.LocalAcceleration / 9.81f : Vector3.zero;
            float targetRoll = g.x * controller.Definition.BodyRollPerG;
            float targetPitch = -g.z * controller.Definition.BodyPitchPerG;
            float targetLift = live && controller.IsGrounded ? -Mathf.Clamp(controller.AverageCompression - restCompression, -0.08f, 0.12f) * 0.6f : 0f;

            roll = MathUtil.Damp(roll, targetRoll, sharpness, dt);
            pitch = MathUtil.Damp(pitch, targetPitch, sharpness, dt);
            lift = MathUtil.Damp(lift, targetLift, sharpness, dt);
            body.localRotation = Quaternion.Euler(pitch, 0f, roll);
            body.localPosition = restPosition + Vector3.up * lift;
        }
    }
}
