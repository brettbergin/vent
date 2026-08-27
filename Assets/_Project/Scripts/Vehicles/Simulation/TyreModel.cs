using UnityEngine;

namespace Vent.Vehicles.Simulation
{
    /// <summary>The shape of a tyre's grip against slip. Plain numbers so the model can be tested without an asset.</summary>
    public readonly struct TyreSpec
    {
        /// <summary>Slip angle (degrees) at which lateral grip peaks; below it grip rises linearly, like a cornering stiffness.</summary>
        public readonly float PeakSlipDegrees;
        /// <summary>Slip angle (degrees) beyond which the tyre is fully sliding.</summary>
        public readonly float SlideSlipDegrees;
        /// <summary>Grip left while sliding, as a fraction of the peak: below one so a slide is a slide, above about 0.6 so it is recoverable.</summary>
        public readonly float SlideGrip;

        public TyreSpec(float peakSlipDegrees, float slideSlipDegrees, float slideGrip)
        {
            PeakSlipDegrees = Mathf.Max(0.5f, peakSlipDegrees);
            SlideSlipDegrees = Mathf.Max(PeakSlipDegrees + 0.5f, slideSlipDegrees);
            SlideGrip = Mathf.Clamp01(slideGrip);
        }
    }

    /// <summary>What one tyre pushes on the car this step, in the contact frame.</summary>
    public readonly struct TyreForces
    {
        /// <summary>Along the wheel's rolling direction, N (positive drives forward).</summary>
        public readonly float Longitudinal;
        /// <summary>Across the wheel, N (positive toward the wheel's right).</summary>
        public readonly float Lateral;
        /// <summary>True when the tyre is past its grip: it wants more than friction allows, or its slip angle is beyond the peak.</summary>
        public readonly bool Sliding;

        public TyreForces(float longitudinal, float lateral, bool sliding)
        {
            Longitudinal = longitudinal;
            Lateral = lateral;
            Sliding = sliding;
        }
    }

    /// <summary>
    /// One tyre, as arithmetic. The lateral force follows a grip-against-slip-angle curve scaled by
    /// the load on the wheel (so an unloaded wheel does nothing and a heavy one grips hard) and is
    /// never allowed to push the contact patch past zero sideways velocity in a single step, which is
    /// what keeps a car standing still from jittering. Drive, brakes and rolling resistance act along
    /// the wheel; the friction circle then scales both directions down together so a locked wheel
    /// (all of its budget spent braking) has nothing left to steer with — that is the handbrake turn.
    /// </summary>
    public static class TyreModel
    {
        /// <summary>Lateral grip as a fraction of μ·N at a slip angle: linear to the peak, easing to the sliding fraction beyond it.</summary>
        public static float LateralGrip(float slipAngleDegrees, in TyreSpec spec)
        {
            float a = Mathf.Abs(slipAngleDegrees);
            if (a <= spec.PeakSlipDegrees)
            {
                return a / spec.PeakSlipDegrees;
            }

            if (a >= spec.SlideSlipDegrees)
            {
                return spec.SlideGrip;
            }

            float t = (a - spec.PeakSlipDegrees) / (spec.SlideSlipDegrees - spec.PeakSlipDegrees);
            return Mathf.Lerp(1f, spec.SlideGrip, t * t * (3f - 2f * t));
        }

        /// <summary>Slip angle in degrees, 0..90, from the contact patch's velocity in the wheel frame.</summary>
        public static float SlipAngle(float longitudinalVelocity, float lateralVelocity)
        {
            return Mathf.Atan2(Mathf.Abs(lateralVelocity), Mathf.Abs(longitudinalVelocity)) * Mathf.Rad2Deg;
        }

        /// <param name="longitudinalVelocity">Contact patch speed along the wheel, m/s.</param>
        /// <param name="lateralVelocity">Contact patch speed across the wheel, m/s.</param>
        /// <param name="load">Normal force on the wheel, N (≤ 0 means airborne).</param>
        /// <param name="mu">Friction coefficient: the total force budget is μ·load.</param>
        /// <param name="driveForce">Requested drive along the wheel, N (negative in reverse).</param>
        /// <param name="brakeForce">Requested braking, N; always opposes motion and never reverses it.</param>
        /// <param name="rollingResistance">Fraction of the load lost to rolling, e.g. 0.015.</param>
        /// <param name="massShare">The mass this wheel is allowed to stop sideways in one step (the car's mass over its wheel count).</param>
        /// <param name="dt">Physics step, s.</param>
        public static TyreForces Solve(float longitudinalVelocity, float lateralVelocity, float load, float mu, float driveForce, float brakeForce,
            float rollingResistance, float massShare, float dt, in TyreSpec spec)
        {
            if (load <= 0f || dt <= 0f)
            {
                return default;
            }

            float limit = mu * load;
            float slip = SlipAngle(longitudinalVelocity, lateralVelocity);
            float lateralWanted = limit * LateralGrip(slip, spec);
            float lateralCancel = Mathf.Abs(lateralVelocity) * massShare / dt;
            float lateral = -Mathf.Sign(lateralVelocity) * Mathf.Min(lateralWanted, lateralCancel);
            if (lateralVelocity == 0f)
            {
                lateral = 0f;
            }

            float resistWanted = rollingResistance * load + Mathf.Max(0f, brakeForce);
            float resistCancel = Mathf.Abs(longitudinalVelocity) * massShare / dt;
            float resist = longitudinalVelocity == 0f ? 0f : -Mathf.Sign(longitudinalVelocity) * Mathf.Min(resistWanted, resistCancel);
            float longitudinal = driveForce + resist;

            float total = Mathf.Sqrt(lateral * lateral + longitudinal * longitudinal);
            bool overBudget = total > limit;
            if (overBudget && total > 0f)
            {
                float k = limit / total;
                lateral *= k;
                longitudinal *= k;
            }

            return new TyreForces(longitudinal, lateral, overBudget || slip > spec.PeakSlipDegrees);
        }
    }
}
