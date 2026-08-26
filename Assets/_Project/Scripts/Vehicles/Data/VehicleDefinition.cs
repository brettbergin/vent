using UnityEngine;

namespace Vent.Vehicles.Data
{
    /// <summary>
    /// Everything that makes one kind of car drive, sound and kill the way it does. Arcade numbers,
    /// not a simulation: the point is that a sedan feels quick and a van feels heavy, and that both
    /// are forgiving on a WheelCollider. Shipped values live in <see cref="ApplyDefaults"/> so they
    /// are reviewable; the asset is regenerated from them.
    /// </summary>
    [CreateAssetMenu(menuName = "Vent/Vehicles/Vehicle Definition", fileName = "Vehicle")]
    public sealed class VehicleDefinition : ScriptableObject
    {
        [Header("Body")]
        [SerializeField] private string displayName = "Sedan";
        [SerializeField] private VehicleShape shape = VehicleShape.Sedan;
        [SerializeField, Min(100f)] private float mass = 1350f;
        [SerializeField, Tooltip("Root-local; the root sits on the ground plane. Low and central keeps it on four wheels.")]
        private Vector3 centreOfMass = new(0f, 0.35f, 0f);

        [Header("Drive (N·m, m/s)")]
        [SerializeField, Min(0f), Tooltip("Total motor torque, split over the four wheels.")]
        private float motorTorque = 1800f;
        [SerializeField, Range(0.1f, 1f)] private float reverseTorqueScale = 0.5f;
        [SerializeField, Min(0f)] private float brakeTorque = 3500f;
        [SerializeField, Min(0f)] private float handbrakeTorque = 6000f;
        [SerializeField, Min(0f), Tooltip("Rolling resistance stand-in: applied whenever the throttle is off.")]
        private float idleBrakeTorque = 250f;
        [SerializeField, Min(1f)] private float topSpeed = 26f;
        [SerializeField, Min(1f)] private float reverseTopSpeed = 8f;
        [SerializeField, Min(0f), Tooltip("Newtons of downforce per m/s of speed; keeps the car planted over kerbs.")]
        private float downforcePerMps = 40f;

        [Header("Steering")]
        [SerializeField, Range(5f, 45f)] private float maxSteerDegrees = 32f;
        [SerializeField, Range(0.1f, 1f), Tooltip("Steering lock at top speed relative to standstill; stops the car spinning out at speed.")]
        private float highSpeedSteerScale = 0.35f;
        [SerializeField, Min(0.5f)] private float steerSharpness = 8f;

        [Header("Wheels")]
        [SerializeField, Min(0.1f)] private float wheelRadius = 0.34f;
        [SerializeField, Min(0.02f)] private float suspensionDistance = 0.18f;
        [SerializeField, Min(1000f)] private float suspensionSpring = 38000f;
        [SerializeField, Min(100f)] private float suspensionDamper = 4500f;
        [SerializeField, Min(0.1f)] private float forwardStiffness = 1.6f;
        [SerializeField, Min(0.1f)] private float sidewaysStiffness = 2.0f;
        [SerializeField, Min(0.1f), Tooltip("Rear grip while the handbrake is on: below the normal stiffness so the back steps out.")]
        private float handbrakeSidewaysStiffness = 0.7f;

        [Header("Occupant")]
        [SerializeField, Range(0f, 1f), Tooltip("Fraction of a zombie's hit that reaches the driver through the bodywork.")]
        private float occupantDamageFactor = 0.35f;

        [Header("Roadkill (m/s, HP)")]
        [SerializeField, Min(0f), Tooltip("Below this speed a car merely nudges a zombie.")]
        private float roadkillMinSpeed = 4f;
        [SerializeField, Min(0f), Tooltip("At or above this speed a hit is fatal.")]
        private float roadkillLethalSpeed = 9f;
        [SerializeField, Min(0f)] private float roadkillMinDamage = 20f;
        [SerializeField, Min(0f)] private float roadkillMaxDamage = 80f;
        [SerializeField, Min(0f)] private float roadkillLethalDamage = 10000f;
        [SerializeField, Range(0f, 0.5f), Tooltip("Fraction of speed lost per body hit; the car feels the impact without stopping.")]
        private float roadkillSpeedLoss = 0.07f;
        [SerializeField, Min(0f), Tooltip("A zombie is not hit twice within this many seconds.")]
        private float rehitSeconds = 0.5f;

        [Header("Engine audio")]
        [SerializeField, Min(0.1f)] private float engineMinPitch = 0.7f;
        [SerializeField, Min(0.1f)] private float engineMaxPitch = 2.0f;
        [SerializeField, Range(0f, 1f)] private float engineMinVolume = 0.2f;
        [SerializeField, Range(0f, 1f)] private float engineMaxVolume = 0.55f;

        [Header("Recovery")]
        [SerializeField, Min(0f), Tooltip("Seconds on its roof (or side) before the car rights itself.")]
        private float selfRightAfterSeconds = 2f;
        [SerializeField, Min(0f), Tooltip("Seconds of stillness after the driver leaves before the car parks (goes kinematic).")]
        private float settleSeconds = 1.5f;
        [SerializeField, Tooltip("Falling below this world Y teleports the car back to where it was parked.")]
        private float killFloorY = -8f;

        public string DisplayName => displayName;
        public VehicleShape Shape => shape;
        public float Mass => mass;
        public Vector3 CentreOfMass => centreOfMass;
        public float MotorTorque => motorTorque;
        public float ReverseTorqueScale => reverseTorqueScale;
        public float BrakeTorque => brakeTorque;
        public float HandbrakeTorque => handbrakeTorque;
        public float IdleBrakeTorque => idleBrakeTorque;
        public float TopSpeed => topSpeed;
        public float ReverseTopSpeed => reverseTopSpeed;
        public float DownforcePerMps => downforcePerMps;
        public float MaxSteerDegrees => maxSteerDegrees;
        public float HighSpeedSteerScale => highSpeedSteerScale;
        public float SteerSharpness => steerSharpness;
        public float WheelRadius => wheelRadius;
        public float SuspensionDistance => suspensionDistance;
        public float SuspensionSpring => suspensionSpring;
        public float SuspensionDamper => suspensionDamper;
        public float ForwardStiffness => forwardStiffness;
        public float SidewaysStiffness => sidewaysStiffness;
        public float HandbrakeSidewaysStiffness => handbrakeSidewaysStiffness;
        public float OccupantDamageFactor => occupantDamageFactor;
        public float RoadkillMinSpeed => roadkillMinSpeed;
        public float RoadkillLethalSpeed => roadkillLethalSpeed;
        public float RoadkillSpeedLoss => roadkillSpeedLoss;
        public float RehitSeconds => rehitSeconds;
        public float EngineMinPitch => engineMinPitch;
        public float EngineMaxPitch => engineMaxPitch;
        public float EngineMinVolume => engineMinVolume;
        public float EngineMaxVolume => engineMaxVolume;
        public float SelfRightAfterSeconds => selfRightAfterSeconds;
        public float SettleSeconds => settleSeconds;
        public float KillFloorY => killFloorY;

        /// <summary>Damage dealt to something hit at <paramref name="speed"/> m/s: nothing below the minimum, a ramp, then fatal.</summary>
        public float RoadkillDamage(float speed)
        {
            speed = Mathf.Abs(speed);
            if (speed < roadkillMinSpeed)
            {
                return 0f;
            }

            if (speed >= roadkillLethalSpeed)
            {
                return roadkillLethalDamage;
            }

            float t = Mathf.InverseLerp(roadkillMinSpeed, roadkillLethalSpeed, speed);
            return Mathf.Lerp(roadkillMinDamage, roadkillMaxDamage, t);
        }

        /// <summary>Steering lock in degrees at a fraction of top speed: full at rest, reduced at speed.</summary>
        public float SteerAngle(float speed01) => maxSteerDegrees * Mathf.Lerp(1f, highSpeedSteerScale, Mathf.Clamp01(speed01));

        /// <summary>The shipped numbers for a body shape. A van is heavier, slower and softer; both roadkill the same way.</summary>
        public void ApplyDefaults(VehicleShape body)
        {
            shape = body;
            bool van = body == VehicleShape.Van;
            displayName = van ? "Van" : "Sedan";
            mass = van ? 2100f : 1350f;
            centreOfMass = new Vector3(0f, van ? 0.45f : 0.35f, 0f);
            motorTorque = van ? 2600f : 1800f;
            reverseTorqueScale = 0.5f;
            brakeTorque = van ? 4500f : 3500f;
            handbrakeTorque = van ? 7000f : 6000f;
            idleBrakeTorque = van ? 300f : 250f;
            topSpeed = van ? 22f : 26f;
            reverseTopSpeed = van ? 7f : 8f;
            downforcePerMps = van ? 55f : 40f;
            maxSteerDegrees = van ? 28f : 32f;
            highSpeedSteerScale = 0.35f;
            steerSharpness = van ? 7f : 8f;
            wheelRadius = van ? 0.38f : 0.34f;
            suspensionDistance = van ? 0.2f : 0.18f;
            suspensionSpring = van ? 52000f : 38000f;
            suspensionDamper = van ? 6000f : 4500f;
            forwardStiffness = 1.6f;
            sidewaysStiffness = 2.0f;
            handbrakeSidewaysStiffness = 0.7f;
            occupantDamageFactor = van ? 0.30f : 0.35f;
            roadkillMinSpeed = 4f;
            roadkillLethalSpeed = van ? 8f : 9f;
            roadkillMinDamage = 20f;
            roadkillMaxDamage = 80f;
            roadkillLethalDamage = 10000f;
            roadkillSpeedLoss = van ? 0.05f : 0.07f;
            rehitSeconds = 0.5f;
            engineMinPitch = van ? 0.6f : 0.7f;
            engineMaxPitch = van ? 1.7f : 2.0f;
            engineMinVolume = van ? 0.25f : 0.2f;
            engineMaxVolume = van ? 0.6f : 0.55f;
            selfRightAfterSeconds = 2f;
            settleSeconds = 1.5f;
            killFloorY = -8f;
        }

        private void Reset() => ApplyDefaults(VehicleShape.Sedan);
    }
}
