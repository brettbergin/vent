using System;
using UnityEngine;
using Vent.Core.Utility;
using Vent.Vehicles.Data;
using Vent.Vehicles.Simulation;

namespace Vent.Vehicles.Runtime
{
    /// <summary>
    /// The car: a Rigidbody with four probe wheels and no WheelColliders. Each physics step it
    /// resolves the pedals, moves the steering toward the lock the speed allows, steps the engine
    /// and gearbox, sweeps each wheel down to the road and applies a spring at the hardpoint and a
    /// tyre force at the contact — lifted to the height of the centre of mass, so cornering pushes
    /// the car sideways but never over. Anti-roll bars keep it flat over kerbs, a yaw assist turns
    /// it toward the line the steering asks for, and an upright torque levels it after a kerb or a
    /// knock. The models it calls (<see cref="TyreModel"/>, <see cref="SuspensionModel"/>,
    /// <see cref="SteeringModel"/>, <see cref="Drivetrain"/>) are engine-free and unit tested; this
    /// class is the plumbing between them and PhysX.
    ///
    /// Parked cars are kinematic — immovable to the player on foot, asleep for the physics engine —
    /// and only the one being driven simulates. If it ends up on its roof it rights itself; if it
    /// falls out of the world it goes home. Nothing here knows about the player: see the gameplay
    /// assembly's VehicleDriver for the hand-off.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class VehicleController : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private VehicleDefinition definition;

        [Header("Wiring")]
        [SerializeField, Tooltip("Front-left, front-right, rear-left, rear-right.")]
        private VehicleWheel[] wheels = new VehicleWheel[4];
        [SerializeField, Tooltip("Body panels that take the paint colour; the placer picks one per car.")]
        private Renderer[] paintRenderers;

        private Rigidbody body;
        private VehicleInput input;
        private bool occupied;
        private float steerCommand;
        private float steer;
        private DrivetrainState drivetrain;
        private TyreSpec tyreSpec;
        private SteeringSpec steeringSpec;
        private DrivetrainSpec drivetrainSpec;
        private float settleTimer;
        private float flippedTimer;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private Vector3 previousVelocity;
        private Vector3 localAcceleration;
        private float skid;
        private int groundedCount;
        private float throttle01;
        private float brake01;
        private bool reversing;
        private bool handbrake;
        private float driveForce;

        public VehicleDefinition Definition => definition;
        public Rigidbody Body => body;
        public bool IsOccupied => occupied;
        public Renderer[] PaintRenderers => paintRenderers;
        public VehicleWheel[] Wheels => wheels;
        /// <summary>Where it was parked at scene load; the kill-floor recovery returns it here.</summary>
        public Vector3 HomePosition => homePosition;
        public Quaternion HomeRotation => homeRotation;

        /// <summary>Signed speed along the nose, m/s (negative reversing).</summary>
        public float ForwardSpeed => body != null && !body.isKinematic ? Vector3.Dot(body.linearVelocity, transform.forward) : 0f;

        /// <summary>|forward speed| as a fraction of top speed.</summary>
        public float Speed01 => definition != null ? Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / definition.TopSpeed) : 0f;

        /// <summary>Engine speed from idle (0) to the redline (1); drives the engine audio.</summary>
        public float Rpm01 => definition != null && !body.isKinematic ? drivetrain.Rpm01(drivetrainSpec) : 0f;
        public float Rpm => drivetrain.Rpm;
        /// <summary>1..N forward, -1 reverse.</summary>
        public int Gear => drivetrain.Gear;
        /// <summary>Steered angle at the front axle, degrees, positive right.</summary>
        public float SteerAngle => steer;
        public float Throttle01 => throttle01;
        /// <summary>What the drivetrain asked the driven wheels for this step, N.</summary>
        public float DriveForce => driveForce;
        public bool IsBraking => brake01 > 0.05f || handbrake;
        /// <summary>Actually moving backwards under reverse gear (reversing lamps).</summary>
        public bool IsReversing => reversing && ForwardSpeed < -0.2f;
        /// <summary>At least three wheels on the road.</summary>
        public bool IsGrounded => groundedCount >= 3;
        public int GroundedWheels => groundedCount;
        /// <summary>Filtered acceleration in car space, m/s²: x sideways (right), z along the nose. Cosmetic roll and pitch read it.</summary>
        public Vector3 LocalAcceleration => localAcceleration;
        /// <summary>0..1 how hard the tyres are sliding sideways or locked; drives the skid loop.</summary>
        public float SkidIntensity => skid;

        /// <summary>Mean suspension compression across the wheels, m.</summary>
        public float AverageCompression
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < wheels.Length; i++)
                {
                    sum += wheels[i] != null ? wheels[i].Compression : 0f;
                }

                return wheels.Length > 0 ? sum / wheels.Length : 0f;
            }
        }

        /// <summary>Raised on a collision with something solid; payload is the relative speed in m/s.</summary>
        public event Action<float> Impact;

        /// <summary>Raised when the driver gets in (true) or out (false).</summary>
        public event Action<bool> OccupiedChanged;

        /// <summary>A gear went in.</summary>
        public event Action GearShifted;

        /// <summary>Editor-time wiring used by the prefab factory.</summary>
        public void Configure(VehicleDefinition def, VehicleWheel[] wheelSet, Renderer[] paint)
        {
            definition = def;
            wheels = wheelSet;
            paintRenderers = paint;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            homePosition = transform.position;
            homeRotation = transform.rotation;
            if (definition != null)
            {
                body.mass = definition.Mass;
                body.centerOfMass = definition.CentreOfMass;
                CacheSpecs();
                drivetrain = DrivetrainState.AtIdle(drivetrainSpec);
            }

            body.isKinematic = true; // parked until someone gets in
        }

        private void CacheSpecs()
        {
            tyreSpec = definition.Tyres;
            steeringSpec = definition.Steering;
            drivetrainSpec = definition.Drivetrain;
        }

        public void SetInput(in VehicleInput value) => input = value;

        /// <summary>Someone sat down (true) or got out (false). Getting out brakes the car until it settles and parks.</summary>
        public void SetOccupied(bool value)
        {
            if (occupied == value)
            {
                return;
            }

            occupied = value;
            input = default;
            settleTimer = 0f;
            flippedTimer = 0f;
            steerCommand = 0f;
            if (definition != null)
            {
                CacheSpecs();
                drivetrain = DrivetrainState.AtIdle(drivetrainSpec);
            }

            if (occupied)
            {
                body.isKinematic = false;
                body.WakeUp();
                previousVelocity = body.linearVelocity;
                SeedWheels();
            }

            OccupiedChanged?.Invoke(occupied);
        }

        /// <summary>Probe once and forget the history so the first simulated step sees no phantom bump.</summary>
        private void SeedWheels()
        {
            if (definition == null)
            {
                return;
            }

            Vector3 up = transform.up;
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] == null)
                {
                    continue;
                }

                wheels[i].Cast(up, definition.WheelRadius, definition.SuspensionTravel, Layers.DriveableMask);
                wheels[i].ResetHistory();
            }
        }

        private void FixedUpdate()
        {
            if (body.isKinematic || definition == null)
            {
                return;
            }

            float dt = Time.fixedDeltaTime;
            Vector3 up = transform.up;
            Vector3 forward = transform.forward;
            Vector3 velocity = body.linearVelocity;
            float forwardSpeed = Vector3.Dot(velocity, forward);
            float speed = velocity.magnitude;

            // --- Pedals and wheel: throttle against the direction of travel brakes; lock shrinks with speed.
            Drivetrain.ResolvePedals(occupied ? input.Throttle : 0f, forwardSpeed, out throttle01, out brake01, out reversing);
            handbrake = occupied && input.Handbrake;
            if (!occupied)
            {
                brake01 = 1f; // the driver has left: the car stops where it is
            }

            steerCommand = MathUtil.Damp(steerCommand, occupied ? input.Steer : 0f, definition.SteerInputSharpness, dt);
            float steerTarget = steerCommand * SteeringModel.MaxAngle(Mathf.Abs(forwardSpeed), steeringSpec);
            steer = SteeringModel.Step(steer, steerTarget, steeringSpec, dt);
            SteeringModel.Ackermann(steer, steeringSpec, out float steerLeft, out float steerRight);

            // --- Engine and gearbox.
            DrivetrainOutput drive = Drivetrain.Step(drivetrain, forwardSpeed, throttle01, reversing, dt, drivetrainSpec);
            drivetrain = drive.State;
            driveForce = drive.WheelForce;
            if (drive.Shifted)
            {
                GearShifted?.Invoke();
            }

            // --- Where is the road under each wheel?
            float radius = definition.WheelRadius;
            float travel = definition.SuspensionTravel;
            int mask = Layers.DriveableMask;
            groundedCount = 0;
            int drivenGrounded = 0;
            Vector3 groundNormal = Vector3.zero;
            for (int i = 0; i < wheels.Length; i++)
            {
                VehicleWheel w = wheels[i];
                w.SteerAngle = w.Steered ? (w.Side < 0 ? steerLeft : steerRight) : 0f;
                w.Cast(up, radius, travel, mask);
                if (w.Grounded)
                {
                    groundedCount++;
                    groundNormal += w.ContactNormal;
                    if (w.Driven)
                    {
                        drivenGrounded++;
                    }
                }
            }

            // --- Springs at the hardpoints, tyres at the contacts (lifted to the centre of mass).
            float massShare = body.mass / wheels.Length;
            Vector3 com = body.worldCenterOfMass;
            float maxSlip = 0f;
            float engineBrake = throttle01 <= 0f && !handbrake && drivenGrounded > 0 ? definition.EngineBrakePerMps * Mathf.Abs(forwardSpeed) / drivenGrounded : 0f;
            for (int i = 0; i < wheels.Length; i++)
            {
                VehicleWheel w = wheels[i];
                bool rear = w.Axle == 1;
                bool locked = handbrake && rear;
                w.Locked = locked;
                if (!w.Grounded)
                {
                    w.Load = 0f;
                    w.LateralSlip = 0f;
                    w.LongitudinalSpeed = forwardSpeed;
                    w.Sliding = false;
                    w.LongitudinalForce = 0f;
                    w.LateralForce = 0f;
                    continue;
                }

                float compressionVelocity = Mathf.Clamp((w.Compression - w.PreviousCompression) / dt, -5f, 5f);
                float load = SuspensionModel.Force(w.Compression, compressionVelocity, travel, definition.SuspensionSpring, definition.SuspensionDamper, definition.BumpStopSpring);
                w.Load = load;
                body.AddForceAtPosition(up * load, w.Hardpoint, ForceMode.Force);

                Vector3 wheelForward = Quaternion.AngleAxis(w.SteerAngle, up) * forward;
                Vector3 tyreForward = Vector3.ProjectOnPlane(wheelForward, w.ContactNormal).normalized;
                Vector3 tyreRight = Vector3.Cross(w.ContactNormal, tyreForward);
                Vector3 patchVelocity = body.GetPointVelocity(w.ContactPoint);
                float vLong = Vector3.Dot(patchVelocity, tyreForward);
                float vLat = Vector3.Dot(patchVelocity, tyreRight);

                float mu = definition.GripMu * (rear ? definition.RearGripScale : 1f) * (locked ? definition.HandbrakeGripScale : 1f);
                float driveForce = w.Driven && !locked && drivenGrounded > 0 ? drive.WheelForce / drivenGrounded : 0f;
                float brakeForce;
                if (locked)
                {
                    brakeForce = definition.HandbrakeForce / 2f;
                }
                else
                {
                    float axleShare = rear ? 1f - definition.BrakeBias : definition.BrakeBias;
                    brakeForce = Mathf.Min(brake01 * definition.BrakeForce * axleShare / 2f, definition.AbsGrip * mu * load);
                    if (w.Driven)
                    {
                        brakeForce += engineBrake;
                    }
                }

                TyreForces tyre = TyreModel.Solve(vLong, vLat, load, mu, driveForce, brakeForce, definition.RollingResistance, massShare, dt, tyreSpec);
                float lift = Vector3.Dot(com - w.ContactPoint, up) * definition.TyreForceHeight;
                body.AddForceAtPosition(tyreForward * tyre.Longitudinal + tyreRight * tyre.Lateral, w.ContactPoint + up * lift, ForceMode.Force);

                w.LateralSlip = vLat;
                w.LongitudinalSpeed = vLong;
                w.Sliding = tyre.Sliding;
                w.LongitudinalForce = tyre.Longitudinal;
                w.LateralForce = tyre.Lateral;
                float slip = Mathf.Abs(vLat);
                if (locked)
                {
                    slip = Mathf.Max(slip, Mathf.Abs(vLong));
                }

                maxSlip = Mathf.Max(maxSlip, slip);
            }

            // --- Anti-roll bars: the higher side lends the lower side its spring.
            for (int axle = 0; axle < 2 && axle * 2 + 1 < wheels.Length; axle++)
            {
                VehicleWheel left = wheels[axle * 2], right = wheels[axle * 2 + 1];
                if (left.Grounded && right.Grounded)
                {
                    float transfer = SuspensionModel.AntiRoll(left.Compression, right.Compression, definition.AntiRollStiffness);
                    body.AddForceAtPosition(up * transfer, left.Hardpoint, ForceMode.Force);
                    body.AddForceAtPosition(-up * transfer, right.Hardpoint, ForceMode.Force);
                }
            }

            // --- Aero.
            body.AddForce(-velocity * (speed * definition.Drag), ForceMode.Force);
            body.AddForce(-up * (definition.DownforcePerMps * Mathf.Abs(forwardSpeed)), ForceMode.Force);

            // --- Assists, as angular accelerations so they are independent of mass.
            Vector3 angular = body.angularVelocity;
            Vector3 targetUp = groundedCount > 0 ? (groundNormal / groundedCount).normalized : Vector3.up;
            if (groundedCount >= 3 && !handbrake && definition.YawAssist > 0f)
            {
                float desired = SteeringModel.DesiredYawRate(forwardSpeed, steer, definition.Wheelbase);
                float yawRate = Vector3.Dot(angular, up);
                float accel = Mathf.Clamp((desired - yawRate) * definition.YawAssist, -definition.YawAssistMaxAccel, definition.YawAssistMaxAccel);
                body.AddTorque(up * accel, ForceMode.Acceleration);
            }

            Vector3 tilt = Vector3.Cross(up, targetUp);
            Vector3 horizontalSpin = angular - up * Vector3.Dot(angular, up);
            body.AddTorque(tilt * definition.UprightGain - horizontalSpin * definition.UprightDamping, ForceMode.Acceleration);

            // --- Recovery: on its roof and still → flip back; through the floor → back to the parking spot.
            bool flipped = Vector3.Dot(up, Vector3.up) < 0.2f;
            flippedTimer = flipped && speed < 1f ? flippedTimer + dt : 0f;
            if (flippedTimer >= definition.SelfRightAfterSeconds)
            {
                flippedTimer = 0f;
                Reposition(body.position + Vector3.up * 1.0f, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
                Impact?.Invoke(4f);
            }

            if (body.position.y < definition.KillFloorY)
            {
                Reposition(homePosition + Vector3.up * 0.5f, homeRotation);
            }

            // --- Telemetry for the presentation layers.
            Vector3 accelerationWorld = (velocity - previousVelocity) / dt;
            previousVelocity = velocity;
            localAcceleration = MathUtil.Damp(localAcceleration, transform.InverseTransformDirection(accelerationWorld), 8f, dt);
            float skidTarget = IsGrounded ? Mathf.InverseLerp(definition.SkidSlipStart, definition.SkidSlipFull, maxSlip) : 0f;
            skid = MathUtil.Damp(skid, skidTarget, 10f, dt);

            // --- Parking: once the driver has left and the car has stopped upright, freeze it.
            if (!occupied)
            {
                bool still = speed < 0.3f && angular.magnitude < 0.3f;
                settleTimer = still && !flipped ? settleTimer + dt : 0f;
                if (settleTimer >= definition.SettleSeconds)
                {
                    body.isKinematic = true;
                    skid = 0f;
                    localAcceleration = Vector3.zero;
                }
            }
        }

        private void Reposition(Vector3 position, Quaternion rotation)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            Physics.SyncTransforms();
            previousVelocity = Vector3.zero;
            localAcceleration = Vector3.zero;
            SeedWheels();
        }

        private void Update()
        {
            if (body.isKinematic || definition == null)
            {
                return; // parked cars keep the prefab's rest pose
            }

            float dt = Time.deltaTime;
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] != null)
                {
                    wheels[i].Pose(definition.WheelRadius, dt);
                }
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            float speed = collision.relativeVelocity.magnitude;
            if (speed > 3f)
            {
                Impact?.Invoke(speed);
            }
        }
    }
}
