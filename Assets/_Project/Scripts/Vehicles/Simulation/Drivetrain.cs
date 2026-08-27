using UnityEngine;

namespace Vent.Vehicles.Simulation
{
    /// <summary>The engine and gearbox numbers, engine-free.</summary>
    public readonly struct DrivetrainSpec
    {
        public readonly float IdleRpm;
        public readonly float RedlineRpm;
        /// <summary>Peak engine torque, N·m. Multiplied by the gearing this is what reaches the road.</summary>
        public readonly float PeakTorque;
        /// <summary>Gearbox ratios, first gear first.</summary>
        public readonly float[] GearRatios;
        public readonly float FinalDrive;
        /// <summary>Reverse uses first gear scaled by this.</summary>
        public readonly float ReverseRatioScale;
        /// <summary>Fraction of the torque that survives the transmission.</summary>
        public readonly float Efficiency;
        public readonly float WheelRadius;
        public readonly float ShiftUpRpm;
        public readonly float ShiftDownRpm;
        /// <summary>Seconds of no drive while a gear goes in.</summary>
        public readonly float ShiftSeconds;
        /// <summary>Below this speed the clutch slips, so the engine revs with the pedal rather than with the wheels.</summary>
        public readonly float ClutchSpeed;
        /// <summary>How fast the engine chases its target speed, 1/s.</summary>
        public readonly float RpmResponse;
        public readonly float TopSpeed;
        public readonly float ReverseTopSpeed;
        /// <summary>Fraction of the top speed over which the drive fades out, so the limiter is a wall you lean on rather than hit.</summary>
        public readonly float LimiterBand;

        public DrivetrainSpec(float idleRpm, float redlineRpm, float peakTorque, float[] gearRatios, float finalDrive, float reverseRatioScale, float efficiency,
            float wheelRadius, float shiftUpRpm, float shiftDownRpm, float shiftSeconds, float clutchSpeed, float rpmResponse, float topSpeed, float reverseTopSpeed, float limiterBand)
        {
            IdleRpm = Mathf.Max(100f, idleRpm);
            RedlineRpm = Mathf.Max(IdleRpm + 500f, redlineRpm);
            PeakTorque = Mathf.Max(0f, peakTorque);
            GearRatios = gearRatios != null && gearRatios.Length > 0 ? gearRatios : new[] { 1f };
            FinalDrive = Mathf.Max(0.1f, finalDrive);
            ReverseRatioScale = Mathf.Max(0.1f, reverseRatioScale);
            Efficiency = Mathf.Clamp01(efficiency);
            WheelRadius = Mathf.Max(0.05f, wheelRadius);
            ShiftUpRpm = Mathf.Clamp(shiftUpRpm, IdleRpm, RedlineRpm);
            ShiftDownRpm = Mathf.Clamp(shiftDownRpm, IdleRpm, ShiftUpRpm);
            ShiftSeconds = Mathf.Max(0f, shiftSeconds);
            ClutchSpeed = Mathf.Max(0.1f, clutchSpeed);
            RpmResponse = Mathf.Max(0.1f, rpmResponse);
            TopSpeed = Mathf.Max(1f, topSpeed);
            ReverseTopSpeed = Mathf.Max(0.5f, reverseTopSpeed);
            LimiterBand = Mathf.Clamp(limiterBand, 0.01f, 1f);
        }
    }

    /// <summary>Engine speed and the gear that is in. Kept by the controller between steps.</summary>
    public struct DrivetrainState
    {
        public float Rpm;
        /// <summary>1..N forward, -1 reverse.</summary>
        public int Gear;
        /// <summary>Seconds left in the current shift; no drive until it reaches zero.</summary>
        public float ShiftTimer;

        public static DrivetrainState AtIdle(in DrivetrainSpec spec) => new() { Rpm = spec.IdleRpm, Gear = 1, ShiftTimer = 0f };

        /// <summary>0 at idle, 1 at the redline.</summary>
        public float Rpm01(in DrivetrainSpec spec) => Mathf.Clamp01((Rpm - spec.IdleRpm) / (spec.RedlineRpm - spec.IdleRpm));
    }

    /// <summary>One step of the drivetrain: the new state plus what the driven wheels should push with.</summary>
    public readonly struct DrivetrainOutput
    {
        public readonly DrivetrainState State;
        /// <summary>Total force at the road across the driven wheels, N (negative in reverse).</summary>
        public readonly float WheelForce;
        /// <summary>A gear went in this step.</summary>
        public readonly bool Shifted;

        public DrivetrainOutput(DrivetrainState state, float wheelForce, bool shifted)
        {
            State = state;
            WheelForce = wheelForce;
            Shifted = shifted;
        }
    }

    /// <summary>
    /// An engine with a torque curve and an automatic gearbox, as arithmetic. The gearing is chosen
    /// for a game car whose top speed is a hundred km/h, not a road car: short ratios and a modest
    /// peak torque give three or four audible shifts on the way up and a wheel force that tapers
    /// naturally with speed. The engine chases the speed the wheels imply through the current gear;
    /// near a standstill the clutch slips, so revving on the spot sounds like revving.
    /// </summary>
    public static class Drivetrain
    {
        /// <summary>Torque available at an engine speed, N·m: rising from idle to a peak two thirds of the way up, then falling away toward the redline.</summary>
        public static float TorqueAt(float rpm, in DrivetrainSpec spec)
        {
            if (rpm >= spec.RedlineRpm)
            {
                return 0f; // the limiter
            }

            float x = Mathf.Clamp01((rpm - spec.IdleRpm) / (spec.RedlineRpm - spec.IdleRpm));
            const float peakAt = 0.66f;
            float shape = x <= peakAt ? Mathf.Lerp(0.55f, 1f, x / peakAt) : Mathf.Lerp(1f, 0.8f, (x - peakAt) / (1f - peakAt));
            return spec.PeakTorque * shape;
        }

        /// <summary>Engine speed the wheels imply in a gear, rpm.</summary>
        public static float RpmFromSpeed(float speedMps, int gear, in DrivetrainSpec spec)
        {
            float wheelRpm = Mathf.Abs(speedMps) / (2f * Mathf.PI * spec.WheelRadius) * 60f;
            return wheelRpm * TotalRatio(gear, spec);
        }

        /// <summary>Engine turns per wheel turn in a gear; reverse counts as first.</summary>
        public static float TotalRatio(int gear, in DrivetrainSpec spec)
        {
            if (gear < 0)
            {
                return spec.GearRatios[0] * spec.ReverseRatioScale * spec.FinalDrive;
            }

            int index = Mathf.Clamp(gear - 1, 0, spec.GearRatios.Length - 1);
            return spec.GearRatios[index] * spec.FinalDrive;
        }

        /// <summary>
        /// Turn the pedals into a plain throttle, brake and direction. Throttle against the direction
        /// of travel is a brake, not a burnout; once the car has all but stopped, holding it starts
        /// the car moving the other way.
        /// </summary>
        public static void ResolvePedals(float pedal, float forwardSpeed, out float throttle01, out float brake01, out bool reverse)
        {
            const float deadzone = 0.05f, stopped = 0.5f;
            throttle01 = 0f;
            brake01 = 0f;
            reverse = forwardSpeed < -stopped;
            if (pedal > deadzone)
            {
                if (forwardSpeed < -stopped)
                {
                    brake01 = pedal;
                }
                else
                {
                    throttle01 = pedal;
                    reverse = false;
                }
            }
            else if (pedal < -deadzone)
            {
                if (forwardSpeed > stopped)
                {
                    brake01 = -pedal;
                }
                else
                {
                    throttle01 = -pedal;
                    reverse = true;
                }
            }
        }

        public static DrivetrainOutput Step(in DrivetrainState previous, float forwardSpeed, float throttle01, bool reverse, float dt, in DrivetrainSpec spec)
        {
            DrivetrainState state = previous;
            float speed = Mathf.Abs(forwardSpeed);
            bool shifted = false;
            state.ShiftTimer = Mathf.Max(0f, state.ShiftTimer - dt);

            if (reverse)
            {
                if (state.Gear != -1)
                {
                    state.Gear = -1;
                }
            }
            else
            {
                if (state.Gear < 1)
                {
                    state.Gear = 1;
                }

                if (state.ShiftTimer <= 0f)
                {
                    float rpmNow = RpmFromSpeed(speed, state.Gear, spec);
                    if (rpmNow > spec.ShiftUpRpm && state.Gear < spec.GearRatios.Length)
                    {
                        state.Gear++;
                        state.ShiftTimer = spec.ShiftSeconds;
                        shifted = true;
                    }
                    else if (rpmNow < spec.ShiftDownRpm && state.Gear > 1)
                    {
                        state.Gear--;
                        state.ShiftTimer = spec.ShiftSeconds * 0.5f;
                        shifted = true;
                    }
                }
            }

            // Engine speed: the wheels through the gear, or the pedal through a slipping clutch near rest.
            float fromWheels = Mathf.Max(spec.IdleRpm, RpmFromSpeed(speed, state.Gear, spec));
            float clutch = Mathf.Clamp01(speed / spec.ClutchSpeed);
            float revving = spec.IdleRpm + throttle01 * (spec.RedlineRpm - spec.IdleRpm) * 0.6f;
            float target = Mathf.Lerp(revving, fromWheels, clutch);
            if (state.ShiftTimer > 0f)
            {
                target *= 0.85f; // the revs dip while the next gear goes in
            }

            target = Mathf.Clamp(target, spec.IdleRpm, spec.RedlineRpm);
            state.Rpm = Mathf.Lerp(state.Rpm, target, 1f - Mathf.Exp(-spec.RpmResponse * dt));

            float force = 0f;
            if (state.ShiftTimer <= 0f && throttle01 > 0f)
            {
                float torque = TorqueAt(state.Rpm, spec) * throttle01;
                force = torque * TotalRatio(state.Gear, spec) * spec.Efficiency / spec.WheelRadius;
                float top = reverse ? spec.ReverseTopSpeed : spec.TopSpeed;
                force *= Mathf.Clamp01((top - speed) / (top * spec.LimiterBand));
                if (reverse)
                {
                    force = -force;
                }
            }

            return new DrivetrainOutput(state, force, shifted);
        }
    }
}
