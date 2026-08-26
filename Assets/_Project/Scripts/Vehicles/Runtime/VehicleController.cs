using System;
using UnityEngine;
using Vent.Core.Utility;
using Vent.Vehicles.Data;

namespace Vent.Vehicles.Runtime
{
    /// <summary>
    /// Arcade car on four <see cref="WheelCollider"/>s. The driver hands it a <see cref="VehicleInput"/>
    /// each frame; it turns that into steer angle, motor and brake torque, plus a little downforce so
    /// kerbs and roadkill never launch it. Parked cars are kinematic — immovable to the player on foot,
    /// asleep for the physics engine — and only the one being driven simulates. If it ends up on its
    /// roof it rights itself; if it falls out of the world it goes home. Nothing here knows about the
    /// player: see the gameplay assembly's VehicleDriver for the hand-off.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class VehicleController : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private VehicleDefinition definition;

        [Header("Wiring")]
        [SerializeField, Tooltip("Front-left, front-right, rear-left, rear-right.")]
        private WheelCollider[] wheels = new WheelCollider[4];
        [SerializeField, Tooltip("Visual wheel transforms in the same order; posed from the colliders every frame.")]
        private Transform[] wheelVisuals = new Transform[4];
        [SerializeField, Tooltip("Body panels that take the paint colour; the placer picks one per car.")]
        private Renderer[] paintRenderers;

        private Rigidbody body;
        private VehicleInput input;
        private float steer;
        private bool occupied;
        private float settleTimer;
        private float flippedTimer;
        private float rpm01;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private bool handbrakeApplied;

        public VehicleDefinition Definition => definition;
        public Rigidbody Body => body;
        public bool IsOccupied => occupied;
        public Renderer[] PaintRenderers => paintRenderers;
        /// <summary>Where it was parked at scene load; the kill-floor recovery returns it here.</summary>
        public Vector3 HomePosition => homePosition;
        public Quaternion HomeRotation => homeRotation;

        /// <summary>Signed speed along the nose, m/s (negative reversing).</summary>
        public float ForwardSpeed => body != null && !body.isKinematic ? Vector3.Dot(body.linearVelocity, transform.forward) : 0f;

        /// <summary>|forward speed| as a fraction of top speed.</summary>
        public float Speed01 => definition != null ? Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / definition.TopSpeed) : 0f;

        /// <summary>Engine load for audio: speed, or throttle when spinning up from rest.</summary>
        public float Rpm01 => rpm01;

        /// <summary>Raised on a collision with something solid; payload is the relative speed in m/s.</summary>
        public event Action<float> Impact;

        /// <summary>Raised when the driver gets in (true) or out (false).</summary>
        public event Action<bool> OccupiedChanged;

        /// <summary>Editor-time wiring used by the prefab factory.</summary>
        public void Configure(VehicleDefinition def, WheelCollider[] wheelColliders, Transform[] visuals, Renderer[] paint)
        {
            definition = def;
            wheels = wheelColliders;
            wheelVisuals = visuals;
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
            }

            body.isKinematic = true; // parked until someone gets in
        }

        private void Start()
        {
            if (wheels.Length > 0 && wheels[0] != null)
            {
                // Sub-stepping is the standard cure for wheels jittering on flat box colliders at low speed.
                wheels[0].ConfigureVehicleSubsteps(5f, 12, 15);
            }
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
            if (occupied)
            {
                body.isKinematic = false;
                body.WakeUp();
            }

            OccupiedChanged?.Invoke(occupied);
        }

        private void FixedUpdate()
        {
            if (body.isKinematic || definition == null)
            {
                return;
            }

            float dt = Time.fixedDeltaTime;
            float forward = ForwardSpeed;
            float speed01 = Speed01;

            // --- Steering: full lock at rest, a fraction of it at speed, smoothed so a tap never snaps the wheels.
            float steerTarget = occupied ? input.Steer * definition.SteerAngle(speed01) : 0f;
            steer = MathUtil.Damp(steer, steerTarget, definition.SteerSharpness, dt);
            wheels[0].steerAngle = steer;
            wheels[1].steerAngle = steer;

            // --- Drive: throttle against the direction of travel is a brake, not a burnout.
            float motor = 0f, brake = definition.IdleBrakeTorque;
            if (!occupied)
            {
                brake = definition.BrakeTorque;
            }
            else if (input.Throttle > 0.05f)
            {
                if (forward < -0.5f)
                {
                    brake = definition.BrakeTorque;
                }
                else
                {
                    motor = forward < definition.TopSpeed ? definition.MotorTorque / 4f * input.Throttle : 0f;
                    brake = 0f;
                }
            }
            else if (input.Throttle < -0.05f)
            {
                if (forward > 0.5f)
                {
                    brake = definition.BrakeTorque;
                }
                else
                {
                    motor = forward > -definition.ReverseTopSpeed ? definition.MotorTorque * definition.ReverseTorqueScale / 4f * input.Throttle : 0f;
                    brake = 0f;
                }
            }

            bool handbrake = occupied && input.Handbrake;
            for (int i = 0; i < wheels.Length; i++)
            {
                WheelCollider w = wheels[i];
                w.motorTorque = motor;
                w.brakeTorque = brake;
                if (i >= 2 && handbrake)
                {
                    w.motorTorque = 0f;
                    w.brakeTorque = definition.HandbrakeTorque;
                }
            }

            if (handbrake != handbrakeApplied)
            {
                // Looser rear grip while the handbrake is on: the tail steps out instead of the car stopping dead.
                handbrakeApplied = handbrake;
                for (int i = 2; i < wheels.Length; i++)
                {
                    WheelFrictionCurve f = wheels[i].sidewaysFriction;
                    f.stiffness = handbrake ? definition.HandbrakeSidewaysStiffness : definition.SidewaysStiffness;
                    wheels[i].sidewaysFriction = f;
                }
            }

            body.AddForce(-transform.up * (definition.DownforcePerMps * Mathf.Abs(forward)));

            // --- Recovery: on its roof and still → flip back; through the floor → back to the parking spot.
            bool flipped = Vector3.Dot(transform.up, Vector3.up) < 0.2f;
            flippedTimer = flipped && body.linearVelocity.magnitude < 1f ? flippedTimer + dt : 0f;
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

            // --- Engine load for the audio.
            float load = occupied ? Mathf.Max(speed01, Mathf.Abs(input.Throttle) * 0.45f) : 0f;
            rpm01 = MathUtil.Damp(rpm01, load, 6f, dt);

            // --- Parking: once the driver has left and the car has stopped upright, freeze it.
            if (!occupied)
            {
                bool still = body.linearVelocity.magnitude < 0.3f && body.angularVelocity.magnitude < 0.3f;
                settleTimer = still && !flipped ? settleTimer + dt : 0f;
                if (settleTimer >= definition.SettleSeconds)
                {
                    body.isKinematic = true;
                    rpm01 = 0f;
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
        }

        private void Update()
        {
            if (body.isKinematic)
            {
                return; // parked cars keep the prefab's rest pose
            }

            for (int i = 0; i < wheels.Length && i < wheelVisuals.Length; i++)
            {
                if (wheels[i] == null || wheelVisuals[i] == null)
                {
                    continue;
                }

                wheels[i].GetWorldPose(out Vector3 pos, out Quaternion rot);
                wheelVisuals[i].SetPositionAndRotation(pos, rot);
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
