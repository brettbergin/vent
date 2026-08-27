using UnityEngine;

namespace Vent.Vehicles.Runtime
{
    /// <summary>
    /// One corner of the car: the suspension hardpoint this transform sits at, the wheel that hangs
    /// below it, and what the last ground probe found. There is no collider here — the controller
    /// sweeps a sphere the size of the tyre down the suspension each step and applies the spring and
    /// tyre forces itself — so the wheel is a probe plus a visual, and the numbers it reports are
    /// plain fields the controller writes and the audio, lights and body motion read.
    /// </summary>
    public sealed class VehicleWheel : MonoBehaviour
    {
        [SerializeField] private bool steered;
        [SerializeField] private bool driven = true;
        [SerializeField, Tooltip("0 front, 1 rear.")] private int axle;
        [SerializeField, Tooltip("-1 left, +1 right.")] private int side = -1;
        [SerializeField, Tooltip("Posed from the probe: hangs below the hardpoint, turns with the steering, spins with the road.")]
        private Transform visual;
        [SerializeField, Tooltip("How far below the hardpoint the wheel hangs with the car parked.")]
        private float restLength = 0.16f;

        private float spinDegrees;

        public bool Steered => steered;
        public bool Driven => driven;
        public int Axle => axle;
        public int Side => side;
        public Transform Visual => visual;
        public float RestLength => restLength;

        // ----- written by the controller each physics step -----
        public bool Grounded { get; private set; }
        /// <summary>Metres from the hardpoint down to the wheel centre: full travel with the wheel hanging, less as it compresses.</summary>
        public float Length { get; private set; }
        /// <summary>Metres the wheel has risen from full extension.</summary>
        public float Compression { get; private set; }
        public float PreviousCompression { get; private set; }
        public Vector3 Hardpoint { get; private set; }
        public Vector3 ContactPoint { get; private set; }
        public Vector3 ContactNormal { get; private set; }
        public float Load { get; set; }
        public float SteerAngle { get; set; }
        /// <summary>Contact patch speed across the tyre, m/s.</summary>
        public float LateralSlip { get; set; }
        /// <summary>Contact patch speed along the tyre, m/s.</summary>
        public float LongitudinalSpeed { get; set; }
        public bool Sliding { get; set; }
        /// <summary>Tyre force along the wheel this step, N.</summary>
        public float LongitudinalForce { get; set; }
        /// <summary>Tyre force across the wheel this step, N.</summary>
        public float LateralForce { get; set; }
        /// <summary>Held by the handbrake: no spin, no grip to speak of.</summary>
        public bool Locked { get; set; }

        public void Configure(bool isSteered, bool isDriven, int axleIndex, int sideSign, Transform wheelVisual, float parkedLength)
        {
            steered = isSteered;
            driven = isDriven;
            axle = axleIndex;
            side = sideSign;
            visual = wheelVisual;
            restLength = parkedLength;
        }

        /// <summary>
        /// Probe for the road: a sphere the size of the tyre swept down the suspension from just
        /// above the hardpoint, so a kerb edge is met by the tyre's curve and climbed rather than
        /// stepped onto. A hit whose surface is steeper than 60° is a wall, not ground; a plain ray
        /// then checks straight down so a wheel pressed against a kerb still stands on the road.
        /// </summary>
        public void Cast(Vector3 up, float radius, float travel, int mask)
        {
            PreviousCompression = Compression;
            Hardpoint = transform.position;
            float reach = travel + radius;
            bool found = Physics.SphereCast(Hardpoint + up * radius, radius, -up, out RaycastHit hit, reach, mask, QueryTriggerInteraction.Ignore)
                         && Vector3.Dot(hit.normal, up) >= 0.5f;
            if (!found)
            {
                found = Physics.Raycast(Hardpoint, -up, out hit, reach, mask, QueryTriggerInteraction.Ignore);
            }

            if (found)
            {
                Grounded = true;
                Length = Mathf.Clamp(hit.distance - radius, -radius * 0.5f, travel);
                Compression = travel - Length;
                ContactPoint = hit.point;
                ContactNormal = hit.normal;
            }
            else
            {
                Grounded = false;
                Length = travel;
                Compression = 0f;
                ContactPoint = Hardpoint - up * reach;
                ContactNormal = up;
            }
        }

        /// <summary>Forget the last step's compression so a teleport or a fresh start does not read as a violent bump.</summary>
        public void ResetHistory()
        {
            PreviousCompression = Compression;
            LateralSlip = 0f;
            LongitudinalSpeed = 0f;
            Sliding = false;
            Locked = false;
            Load = 0f;
        }

        /// <summary>Put the visual where the probe says the wheel is, steered and spinning.</summary>
        public void Pose(float radius, float dt)
        {
            if (visual == null)
            {
                return;
            }

            if (!Locked)
            {
                spinDegrees += LongitudinalSpeed / Mathf.Max(0.05f, radius) * Mathf.Rad2Deg * dt;
                spinDegrees = Mathf.Repeat(spinDegrees, 360f);
            }

            visual.localPosition = new Vector3(0f, -Length, 0f);
            visual.localRotation = Quaternion.Euler(0f, SteerAngle, 0f) * Quaternion.Euler(spinDegrees, 0f, 0f);
        }

        /// <summary>The parked pose: hanging at the rest length, straight ahead.</summary>
        public void PoseAtRest()
        {
            if (visual != null)
            {
                visual.localPosition = new Vector3(0f, -restLength, 0f);
                visual.localRotation = Quaternion.identity;
            }
        }
    }
}
