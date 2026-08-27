using UnityEngine;

namespace Vent.Vehicles.Simulation
{
    /// <summary>The steering numbers, engine-free.</summary>
    public readonly struct SteeringSpec
    {
        /// <summary>Lock at a standstill, degrees.</summary>
        public readonly float MaxDegrees;
        /// <summary>Lock never drops below this at any speed, degrees, so the wheel always does something.</summary>
        public readonly float MinDegrees;
        /// <summary>The cornering the steering will ask of the tyres at speed, in g. Lock at speed is derived from it.</summary>
        public readonly float MaxLateralG;
        public readonly float Wheelbase;
        public readonly float Track;
        /// <summary>Degrees per second the wheels turn into a corner.</summary>
        public readonly float RateIn;
        /// <summary>Degrees per second the wheels come back to centre; faster than turning in, so letting go straightens the car.</summary>
        public readonly float RateReturn;

        public SteeringSpec(float maxDegrees, float minDegrees, float maxLateralG, float wheelbase, float track, float rateIn, float rateReturn)
        {
            MaxDegrees = Mathf.Max(1f, maxDegrees);
            MinDegrees = Mathf.Clamp(minDegrees, 0.5f, MaxDegrees);
            MaxLateralG = Mathf.Max(0.1f, maxLateralG);
            Wheelbase = Mathf.Max(0.5f, wheelbase);
            Track = Mathf.Max(0.3f, track);
            RateIn = Mathf.Max(1f, rateIn);
            RateReturn = Mathf.Max(1f, rateReturn);
        }
    }

    /// <summary>
    /// Steering as arithmetic. The lock available shrinks with speed so that a full turn of the
    /// wheel asks the tyres for a fixed lateral acceleration rather than a fixed angle — full lock
    /// at 25 m/s is a few degrees, which is all the tyres could ever use, and is why the car neither
    /// spins nor rolls when the player holds the key down. The wheels then move toward that target
    /// at a limited rate (into the corner slower than back out), and the inner wheel turns more than
    /// the outer (Ackermann), so both roll instead of scrubbing.
    /// </summary>
    public static class SteeringModel
    {
        private const float Gravity = 9.81f;

        /// <summary>Lock available at a speed, degrees: full at rest, then whatever gives <see cref="SteeringSpec.MaxLateralG"/> on a bicycle model.</summary>
        public static float MaxAngle(float speedMps, in SteeringSpec spec)
        {
            float v2 = speedMps * speedMps;
            if (v2 < 0.01f)
            {
                return spec.MaxDegrees;
            }

            float byGrip = Mathf.Atan(spec.MaxLateralG * Gravity * spec.Wheelbase / v2) * Mathf.Rad2Deg;
            return Mathf.Clamp(byGrip, spec.MinDegrees, spec.MaxDegrees);
        }

        /// <summary>Move the steered angle toward its target at the turn-in rate, or the return rate when heading back to centre.</summary>
        public static float Step(float current, float target, in SteeringSpec spec, float dt)
        {
            // Back toward centre, or across it: the return rate. From centre outward, either way: the turn-in rate.
            bool returning = Mathf.Abs(target) < Mathf.Abs(current) || target * current < 0f;
            float rate = returning ? spec.RateReturn : spec.RateIn;
            return Mathf.MoveTowards(current, target, rate * dt);
        }

        /// <summary>Per-wheel angles for a steered angle: the inner wheel follows a tighter circle than the outer.</summary>
        public static void Ackermann(float steerDegrees, in SteeringSpec spec, out float leftDegrees, out float rightDegrees)
        {
            if (Mathf.Abs(steerDegrees) < 0.01f)
            {
                leftDegrees = rightDegrees = 0f;
                return;
            }

            float radius = spec.Wheelbase / Mathf.Tan(Mathf.Abs(steerDegrees) * Mathf.Deg2Rad);
            float inner = Mathf.Atan(spec.Wheelbase / Mathf.Max(0.1f, radius - spec.Track / 2f)) * Mathf.Rad2Deg;
            float outer = Mathf.Atan(spec.Wheelbase / (radius + spec.Track / 2f)) * Mathf.Rad2Deg;
            if (steerDegrees > 0f)
            {
                rightDegrees = inner;
                leftDegrees = outer;
            }
            else
            {
                leftDegrees = -inner;
                rightDegrees = -outer;
            }
        }

        /// <summary>The yaw rate (rad/s, positive turning right) a bicycle model would have at this speed and steer angle; negative speed reverses it, as reversing does.</summary>
        public static float DesiredYawRate(float forwardSpeedMps, float steerDegrees, float wheelbase)
        {
            return forwardSpeedMps * Mathf.Tan(steerDegrees * Mathf.Deg2Rad) / Mathf.Max(0.5f, wheelbase);
        }
    }
}
