using UnityEngine;
using Vent.Vehicles.Simulation;

namespace Vent.Vehicles.Data
{
    /// <summary>
    /// Everything that makes one kind of car drive, sound and kill the way it does. The numbers feed
    /// the engine-free models in <c>Vent.Vehicles.Simulation</c> (tyres, suspension, steering,
    /// drivetrain); the controller only wires them to a Rigidbody. Arcade tuning with a physical
    /// shape: a sedan feels quick and a van feels heavy, both are forgiving, and neither can be
    /// rolled by steering alone. Shipped values live in <see cref="ApplyDefaults"/> so they are
    /// reviewable; the asset is regenerated from them.
    /// </summary>
    [CreateAssetMenu(menuName = "Vent/Vehicles/Vehicle Definition", fileName = "Vehicle")]
    public sealed class VehicleDefinition : ScriptableObject
    {
        [Header("Body")]
        [SerializeField] private string displayName = "Sedan";
        [SerializeField] private VehicleShape shape = VehicleShape.Sedan;
        [SerializeField, Min(100f)] private float mass = 1350f;
        [SerializeField, Tooltip("Root-local; the root sits on the ground plane. Low keeps it planted over kerbs.")]
        private Vector3 centreOfMass = new(0f, 0.30f, 0f);
        [SerializeField, Min(0.5f)] private float wheelbase = 2.8f;
        [SerializeField, Min(0.3f)] private float track = 1.6f;

        [Header("Suspension (m, N/m, N·s/m)")]
        [SerializeField, Min(0.1f)] private float wheelRadius = 0.34f;
        [SerializeField, Min(0.02f), Tooltip("Usable travel from full extension to the bump stop.")]
        private float suspensionTravel = 0.24f;
        [SerializeField, Min(1000f)] private float suspensionSpring = 42000f;
        [SerializeField, Min(100f)] private float suspensionDamper = 4200f;
        [SerializeField, Min(0f), Tooltip("Force moved across an axle per metre of compression difference; keeps the body flat over kerbs.")]
        private float antiRollStiffness = 25000f;
        [SerializeField, Min(0f), Tooltip("Extra spring rate beyond the travel.")]
        private float bumpStopSpring = 160000f;

        [Header("Tyres")]
        [SerializeField, Range(0.2f, 4f), Tooltip("Friction coefficient: the force budget of a wheel is this times its load. Arcade: twice a road tyre, so a street corner can be taken at speed.")]
        private float gripMu = 2.2f;
        [SerializeField, Range(0.5f, 1.5f), Tooltip("Rear grip relative to the front; below one the tail steps out first.")]
        private float rearGripScale = 1f;
        [SerializeField, Range(1f, 30f)] private float peakSlipDegrees = 8f;
        [SerializeField, Range(5f, 60f)] private float slideSlipDegrees = 25f;
        [SerializeField, Range(0.3f, 1f)] private float slideGrip = 0.75f;
        [SerializeField, Range(0.1f, 1f), Tooltip("Rear grip while the handbrake is on: below the normal grip so the back steps out.")]
        private float handbrakeGripScale = 0.55f;
        [SerializeField, Range(0f, 0.1f)] private float rollingResistance = 0.015f;
        [SerializeField, Range(0f, 1f), Tooltip("Where cornering forces act: 0 at the contact patch (rolls the body), 1 at the centre of mass (cornering can never roll the car).")]
        private float tyreForceHeight = 1f;

        [Header("Engine and gearbox")]
        [SerializeField, Min(100f)] private float idleRpm = 900f;
        [SerializeField, Min(1000f)] private float redlineRpm = 6500f;
        [SerializeField, Min(0f), Tooltip("Peak engine torque, N·m. Tuned with the gearing for a game car, not copied from a road car.")]
        private float peakTorque = 120f;
        [SerializeField] private float[] gearRatios = { 3.4f, 2.0f, 1.35f, 1.0f };
        [SerializeField, Min(0.1f)] private float finalDrive = 8f;
        [SerializeField, Min(0.1f)] private float reverseRatioScale = 1f;
        [SerializeField, Range(0.1f, 1f)] private float transmissionEfficiency = 0.85f;
        [SerializeField, Min(500f)] private float shiftUpRpm = 6100f;
        [SerializeField, Min(200f)] private float shiftDownRpm = 2600f;
        [SerializeField, Min(0f)] private float shiftSeconds = 0.18f;
        [SerializeField, Min(0.1f)] private float clutchSpeed = 3f;
        [SerializeField, Min(0.1f)] private float rpmResponse = 9f;

        [Header("Drive (N, m/s)")]
        [SerializeField, Min(1f)] private float topSpeed = 26f;
        [SerializeField, Min(1f)] private float reverseTopSpeed = 8f;
        [SerializeField, Range(0.01f, 1f)] private float limiterBand = 0.08f;
        [SerializeField, Min(0f), Tooltip("Total braking force with the pedal down.")]
        private float brakeForce = 13000f;
        [SerializeField, Range(0f, 1f), Tooltip("Share of the braking on the front axle.")]
        private float brakeBias = 0.62f;
        [SerializeField, Range(0.1f, 1f), Tooltip("Anti-lock: a wheel never brakes with more than this fraction of its grip, so it can still steer.")]
        private float absGrip = 0.9f;
        [SerializeField, Min(0f), Tooltip("Handbrake force on the rear axle; enough to lock it.")]
        private float handbrakeForce = 9000f;
        [SerializeField, Min(0f), Tooltip("Engine braking with the throttle off, N per m/s.")]
        private float engineBrakePerMps = 140f;
        [SerializeField, Min(0f), Tooltip("Aerodynamic drag, N per (m/s)².")]
        private float drag = 0.45f;
        [SerializeField, Min(0f), Tooltip("Newtons of downforce per m/s of speed; keeps the car planted over kerbs.")]
        private float downforcePerMps = 30f;

        [Header("Steering")]
        [SerializeField, Range(5f, 45f)] private float maxSteerDegrees = 34f;
        [SerializeField, Range(0.5f, 10f), Tooltip("Lock never drops below this at speed.")]
        private float minSteerDegrees = 5f;
        [SerializeField, Range(0.2f, 4f), Tooltip("The cornering full lock asks of the tyres at speed, in g; lock at speed is derived from it. A road car manages one; a 14 m street corner at 50 km/h needs two.")]
        private float maxLateralG = 2.0f;
        [SerializeField, Min(1f), Tooltip("Degrees per second into a corner.")]
        private float steerRateIn = 140f;
        [SerializeField, Min(1f), Tooltip("Degrees per second back to centre.")]
        private float steerRateReturn = 320f;
        [SerializeField, Min(0.5f), Tooltip("Smoothing on the stick or key before it becomes an angle, 1/s.")]
        private float steerInputSharpness = 12f;

        [Header("Assists")]
        [SerializeField, Min(0f), Tooltip("How hard the car is turned toward the yaw rate the steering asks for, 1/s. Off while the handbrake is on.")]
        private float yawAssist = 4f;
        [SerializeField, Min(0f), Tooltip("Cap on the assist, rad/s².")]
        private float yawAssistMaxAccel = 4.5f;
        [SerializeField, Min(0f), Tooltip("Torque that levels the car to the road (or the sky when airborne), rad/s² per radian of tilt.")]
        private float uprightGain = 12f;
        [SerializeField, Min(0f), Tooltip("Damping on roll and pitch rates, 1/s.")]
        private float uprightDamping = 2.5f;

        [Header("Feel")]
        [SerializeField, Min(0f), Tooltip("Sideways tyre speed at which the skid sound and marks begin, m/s.")]
        private float skidSlipStart = 2.5f;
        [SerializeField, Min(0.1f), Tooltip("Sideways tyre speed at which the skid is at full volume, m/s.")]
        private float skidSlipFull = 6f;
        [SerializeField, Min(0f), Tooltip("Cosmetic body roll per g of cornering, degrees.")]
        private float bodyRollPerG = 3f;
        [SerializeField, Min(0f), Tooltip("Cosmetic body pitch per g of acceleration or braking, degrees.")]
        private float bodyPitchPerG = 2f;

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
        public float Wheelbase => wheelbase;
        public float Track => track;
        public float WheelRadius => wheelRadius;
        public float SuspensionTravel => suspensionTravel;
        public float SuspensionSpring => suspensionSpring;
        public float SuspensionDamper => suspensionDamper;
        public float AntiRollStiffness => antiRollStiffness;
        public float BumpStopSpring => bumpStopSpring;
        public float GripMu => gripMu;
        public float RearGripScale => rearGripScale;
        public float HandbrakeGripScale => handbrakeGripScale;
        public float RollingResistance => rollingResistance;
        public float TyreForceHeight => tyreForceHeight;
        public float TopSpeed => topSpeed;
        public float ReverseTopSpeed => reverseTopSpeed;
        public float BrakeForce => brakeForce;
        public float BrakeBias => brakeBias;
        public float AbsGrip => absGrip;
        public float HandbrakeForce => handbrakeForce;
        public float EngineBrakePerMps => engineBrakePerMps;
        public float Drag => drag;
        public float DownforcePerMps => downforcePerMps;
        public float MaxSteerDegrees => maxSteerDegrees;
        public float SteerInputSharpness => steerInputSharpness;
        public float YawAssist => yawAssist;
        public float YawAssistMaxAccel => yawAssistMaxAccel;
        public float UprightGain => uprightGain;
        public float UprightDamping => uprightDamping;
        public float SkidSlipStart => skidSlipStart;
        public float SkidSlipFull => skidSlipFull;
        public float BodyRollPerG => bodyRollPerG;
        public float BodyPitchPerG => bodyPitchPerG;
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

        /// <summary>The engine-free specs the controller hands to the models each step.</summary>
        public TyreSpec Tyres => new(peakSlipDegrees, slideSlipDegrees, slideGrip);
        public SteeringSpec Steering => new(maxSteerDegrees, minSteerDegrees, maxLateralG, wheelbase, track, steerRateIn, steerRateReturn);
        public DrivetrainSpec Drivetrain => new(idleRpm, redlineRpm, peakTorque, gearRatios, finalDrive, reverseRatioScale, transmissionEfficiency, wheelRadius,
            shiftUpRpm, shiftDownRpm, shiftSeconds, clutchSpeed, rpmResponse, topSpeed, reverseTopSpeed, limiterBand);

        /// <summary>How far each corner sits into its travel with the car parked: the prefab hangs the wheels at this height.</summary>
        public float RestCompression => SuspensionModel.RestCompression(mass, suspensionSpring, 4);

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

        /// <summary>Steering lock in degrees at a speed in m/s: full at rest, only what the tyres can use at speed.</summary>
        public float SteerAngle(float speedMps) => SteeringModel.MaxAngle(speedMps, Steering);

        /// <summary>
        /// The shipped numbers for a body shape. Light cars (sedan, hatchback) are quick and eager;
        /// the SUV sits between; the pickup and the van are heavy, softer and shorter-geared. All are
        /// arcade — twice a road tyre's grip, so a street corner can be taken at speed — and all
        /// roadkill the same way.
        /// </summary>
        public void ApplyDefaults(VehicleShape body)
        {
            shape = body;
            bool heavy = body == VehicleShape.Van || body == VehicleShape.Pickup;
            bool mid = body == VehicleShape.Suv;
            switch (body)
            {
                case VehicleShape.Hatchback:
                    displayName = "Hatchback"; mass = 1150f; wheelbase = 2.55f; track = 1.55f; wheelRadius = 0.32f; topSpeed = 24f; peakTorque = 105f; break;
                case VehicleShape.Suv:
                    displayName = "SUV"; mass = 1900f; wheelbase = 2.9f; track = 1.68f; wheelRadius = 0.38f; topSpeed = 24f; peakTorque = 150f; break;
                case VehicleShape.Pickup:
                    displayName = "Pickup"; mass = 2200f; wheelbase = 3.3f; track = 1.7f; wheelRadius = 0.38f; topSpeed = 23f; peakTorque = 165f; break;
                case VehicleShape.Van:
                    displayName = "Van"; mass = 2100f; wheelbase = 3.4f; track = 1.7f; wheelRadius = 0.36f; topSpeed = 22f; peakTorque = 160f; break;
                default:
                    displayName = "Sedan"; mass = 1350f; wheelbase = 2.8f; track = 1.6f; wheelRadius = 0.34f; topSpeed = 26f; peakTorque = 120f; break;
            }

            centreOfMass = new Vector3(0f, heavy ? 0.38f : mid ? 0.36f : 0.30f, 0f);
            suspensionTravel = heavy || mid ? 0.27f : 0.24f;
            suspensionSpring = heavy ? 62000f : mid ? 56000f : mass > 1200f ? 42000f : 38000f;
            suspensionDamper = heavy ? 6500f : mid ? 5800f : mass > 1200f ? 4200f : 3800f;
            antiRollStiffness = heavy ? 35000f : mid ? 30000f : 25000f;
            bumpStopSpring = heavy || mid ? 240000f : 160000f;

            gripMu = heavy ? 1.8f : mid ? 1.9f : 2.2f;
            rearGripScale = 1f;
            peakSlipDegrees = 8f;
            slideSlipDegrees = 25f;
            slideGrip = 0.75f;
            handbrakeGripScale = 0.55f;
            rollingResistance = 0.015f;
            tyreForceHeight = 1f;

            idleRpm = heavy ? 800f : 900f;
            redlineRpm = heavy ? 5200f : mid ? 5800f : 6500f;
            gearRatios = heavy ? new[] { 3.2f, 1.9f, 1.3f, 1.0f } : new[] { 3.4f, 2.0f, 1.35f, 1.0f };
            finalDrive = heavy ? 7.5f : 8f;
            reverseRatioScale = 1f;
            transmissionEfficiency = 0.85f;
            shiftUpRpm = redlineRpm - 400f;
            shiftDownRpm = heavy ? 2300f : 2600f;
            shiftSeconds = heavy ? 0.22f : 0.18f;
            clutchSpeed = 3f;
            rpmResponse = heavy ? 7f : 9f;

            reverseTopSpeed = heavy ? 7f : 8f;
            limiterBand = 0.08f;
            brakeForce = mass * 9.81f * 1.0f;
            brakeBias = 0.62f;
            absGrip = 0.9f;
            handbrakeForce = mass * 6.5f;
            engineBrakePerMps = heavy ? 200f : mid ? 170f : 140f;
            drag = heavy ? 0.7f : mid ? 0.6f : 0.45f;
            downforcePerMps = heavy || mid ? 40f : 30f;

            maxSteerDegrees = heavy ? 30f : 34f;
            minSteerDegrees = 5f;
            maxLateralG = heavy ? 1.6f : mid ? 1.7f : 2.0f;
            steerRateIn = heavy ? 110f : 140f;
            steerRateReturn = heavy ? 260f : 320f;
            steerInputSharpness = 12f;

            yawAssist = heavy ? 3.5f : 4f;
            yawAssistMaxAccel = heavy ? 3.5f : 4.5f;
            uprightGain = heavy ? 10f : 12f;
            uprightDamping = 2.5f;

            skidSlipStart = 2.5f;
            skidSlipFull = 6f;
            bodyRollPerG = heavy || mid ? 4f : 3f;
            bodyPitchPerG = heavy || mid ? 2.5f : 2f;

            occupantDamageFactor = heavy ? 0.30f : 0.35f;
            roadkillMinSpeed = 4f;
            roadkillLethalSpeed = heavy ? 8f : 9f;
            roadkillMinDamage = 20f;
            roadkillMaxDamage = 80f;
            roadkillLethalDamage = 10000f;
            roadkillSpeedLoss = heavy ? 0.05f : 0.07f;
            rehitSeconds = 0.5f;
            engineMinPitch = heavy ? 0.6f : 0.7f;
            engineMaxPitch = heavy ? 1.7f : 2.0f;
            engineMinVolume = heavy ? 0.25f : 0.2f;
            engineMaxVolume = heavy ? 0.6f : 0.55f;
            selfRightAfterSeconds = 2f;
            settleSeconds = 1.5f;
            killFloorY = -8f;
        }

        private void Reset() => ApplyDefaults(VehicleShape.Sedan);
    }
}
