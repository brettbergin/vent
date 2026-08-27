using NUnit.Framework;
using UnityEngine;
using Vent.Vehicles.Simulation;

namespace Vent.Tests.EditMode
{
    /// <summary>
    /// The engine-free car models: the tyre never pushes past zero slip or past its friction budget,
    /// the suspension never pulls, the steering lock shrinks with speed to a lateral-g budget, and
    /// the drivetrain shifts up on the way to a top speed it fades into rather than hits.
    /// </summary>
    public sealed class VehicleModelTests
    {
        private static readonly TyreSpec Tyre = new(peakSlipDegrees: 8f, slideSlipDegrees: 25f, slideGrip: 0.75f);
        private const float Dt = 0.02f, MassShare = 1350f / 4f, Load = 3311f, Mu = 1.05f;

        // ---------------------------------------------------------------- tyres

        [Test]
        public void LateralGripRisesToThePeakThenEasesToTheSlide()
        {
            Assert.AreEqual(0f, TyreModel.LateralGrip(0f, Tyre));
            Assert.AreEqual(0.5f, TyreModel.LateralGrip(4f, Tyre), 1e-4f, "linear below the peak");
            Assert.AreEqual(1f, TyreModel.LateralGrip(8f, Tyre), 1e-4f);
            Assert.Less(TyreModel.LateralGrip(16f, Tyre), 1f);
            Assert.Greater(TyreModel.LateralGrip(16f, Tyre), 0.75f);
            Assert.AreEqual(0.75f, TyreModel.LateralGrip(40f, Tyre), 1e-4f, "sliding grip is flat");
            Assert.AreEqual(TyreModel.LateralGrip(-6f, Tyre), TyreModel.LateralGrip(6f, Tyre), "symmetric");
        }

        [Test]
        public void ATyreAtRestPushesNothing()
        {
            TyreForces f = TyreModel.Solve(0f, 0f, Load, Mu, 0f, 0f, 0.015f, MassShare, Dt, Tyre);
            Assert.AreEqual(0f, f.Lateral);
            Assert.AreEqual(0f, f.Longitudinal);
            Assert.IsFalse(f.Sliding);
            Assert.AreEqual(0f, TyreModel.Solve(5f, 1f, 0f, Mu, 1000f, 0f, 0.015f, MassShare, Dt, Tyre).Lateral, "no load, no force");
        }

        [Test]
        public void LateralForceOpposesSlipAndNeverExceedsTheBudget()
        {
            TyreForces gentle = TyreModel.Solve(15f, 0.5f, Load, Mu, 0f, 0f, 0f, MassShare, Dt, Tyre);
            Assert.Less(gentle.Lateral, 0f, "pushes back against sliding right");
            Assert.LessOrEqual(Mathf.Abs(gentle.Lateral), Mu * Load);
            Assert.IsFalse(gentle.Sliding);

            TyreForces hard = TyreModel.Solve(15f, 9f, Load, Mu, 0f, 0f, 0f, MassShare, Dt, Tyre);
            Assert.IsTrue(hard.Sliding, "31° of slip is past the peak");
            Assert.AreEqual(Mu * Load * 0.75f, Mathf.Abs(hard.Lateral), 5f, "sliding at the slide grip");
            Assert.Greater(TyreModel.Solve(15f, -9f, Load, Mu, 0f, 0f, 0f, MassShare, Dt, Tyre).Lateral, 0f, "and the other way");
        }

        [Test]
        public void ATyreNeverPushesTheContactPatchPastZeroSideways()
        {
            // Crawling with a little sideways drift: the slip angle is huge but the force may only cancel the drift.
            const float lateral = 0.05f;
            TyreForces f = TyreModel.Solve(0.1f, lateral, Load, Mu, 0f, 0f, 0f, MassShare, Dt, Tyre);
            Assert.AreEqual(-lateral * MassShare / Dt, f.Lateral, 1e-3f);
        }

        [Test]
        public void BrakingOpposesMotionAndNeverReversesIt()
        {
            TyreForces rolling = TyreModel.Solve(10f, 0f, Load, Mu, 0f, 2000f, 0f, MassShare, Dt, Tyre);
            Assert.AreEqual(-2000f, rolling.Longitudinal, 1e-3f);
            TyreForces nearlyStopped = TyreModel.Solve(0.02f, 0f, Load, Mu, 0f, 2000f, 0f, MassShare, Dt, Tyre);
            Assert.AreEqual(-0.02f * MassShare / Dt, nearlyStopped.Longitudinal, 1e-3f, "only what stops it this step");
            Assert.Greater(TyreModel.Solve(-10f, 0f, Load, Mu, 0f, 2000f, 0f, MassShare, Dt, Tyre).Longitudinal, 0f, "reversing brakes forward");
        }

        [Test]
        public void ALockedWheelHasNothingLeftToSteerWith()
        {
            // Handbrake: more braking than the budget, plus sideways slip. The circle scales both down; the lateral share shrinks with it.
            TyreForces f = TyreModel.Solve(12f, 3f, Load, Mu * 0.55f, 0f, 4500f, 0f, MassShare, Dt, Tyre);
            float total = Mathf.Sqrt(f.Lateral * f.Lateral + f.Longitudinal * f.Longitudinal);
            Assert.AreEqual(Mu * 0.55f * Load, total, 1f, "on the friction circle");
            Assert.IsTrue(f.Sliding);
            Assert.Less(Mathf.Abs(f.Lateral), Mathf.Abs(f.Longitudinal));
        }

        [Test]
        public void DriveIsCappedByTheFrictionCircle()
        {
            TyreForces f = TyreModel.Solve(2f, 0f, Load, Mu, 20000f, 0f, 0f, MassShare, Dt, Tyre);
            Assert.AreEqual(Mu * Load, f.Longitudinal, 1f);
            Assert.IsTrue(f.Sliding, "wheelspin");
        }

        // ---------------------------------------------------------------- suspension

        [Test]
        public void SuspensionPushesButNeverPulls()
        {
            Assert.AreEqual(0f, SuspensionModel.Force(-0.05f, 0f, 0.24f, 42000f, 4200f, 160000f), "hanging wheel");
            Assert.AreEqual(42000f * 0.1f, SuspensionModel.Force(0.1f, 0f, 0.24f, 42000f, 4200f, 160000f), 1e-3f);
            Assert.AreEqual(0f, SuspensionModel.Force(0.01f, -2f, 0.24f, 42000f, 4200f, 160000f), "rebounding fast: the damper would pull, so it is clamped");
            float atTravel = SuspensionModel.Force(0.24f, 0f, 0.24f, 42000f, 4200f, 160000f);
            float pastTravel = SuspensionModel.Force(0.26f, 0f, 0.24f, 42000f, 4200f, 160000f);
            Assert.Greater(pastTravel - atTravel, 42000f * 0.02f * 2f, "the bump stop is much stiffer than the spring");
        }

        [Test]
        public void RestCompressionIsTheWeightOverTheSprings()
        {
            Assert.AreEqual(1350f * 9.81f / (4f * 42000f), SuspensionModel.RestCompression(1350f, 42000f, 4), 1e-6f);
            Assert.AreEqual(2000f, SuspensionModel.AntiRoll(0.1f, 0.06f, 50000f), 1e-3f, "the lower side gets the difference");
        }

        // ---------------------------------------------------------------- steering

        private static readonly SteeringSpec Steering = new(maxDegrees: 34f, minDegrees: 3f, maxLateralG: 1f, wheelbase: 2.8f, track: 1.6f, rateIn: 140f, rateReturn: 320f);

        [Test]
        public void LockShrinksWithSpeedToALateralGBudget()
        {
            Assert.AreEqual(34f, SteeringModel.MaxAngle(0f, Steering), 1e-3f, "full lock at rest");
            float at5 = SteeringModel.MaxAngle(5f, Steering), at15 = SteeringModel.MaxAngle(15f, Steering), at26 = SteeringModel.MaxAngle(26f, Steering);
            Assert.Greater(at5, at15);
            Assert.Greater(at15, at26);
            Assert.GreaterOrEqual(at26, 3f, "never below the floor");
            // The lock at 15 m/s asks the tyres for one g: v²·tan(δ)/L = g.
            Assert.AreEqual(9.81f, 15f * 15f * Mathf.Tan(at15 * Mathf.Deg2Rad) / 2.8f, 0.05f);
        }

        [Test]
        public void TheWheelReturnsToCentreFasterThanItTurnsIn()
        {
            float turnedIn = SteeringModel.Step(0f, 34f, Steering, 0.1f);
            Assert.AreEqual(14f, turnedIn, 1e-3f);
            float returned = SteeringModel.Step(34f, 0f, Steering, 0.1f);
            Assert.AreEqual(2f, returned, 1e-3f, "320°/s back toward zero");
            Assert.AreEqual(-18f, SteeringModel.Step(14f, -30f, Steering, 0.1f), 1e-3f, "crossing centre uses the return rate");
            Assert.AreEqual(-14f, SteeringModel.Step(0f, -34f, Steering, 0.1f), 1e-3f, "turning in to the left from centre is as slow as to the right");
        }

        [Test]
        public void AckermannTurnsTheInnerWheelMore()
        {
            SteeringModel.Ackermann(20f, Steering, out float left, out float right);
            Assert.Greater(right, left, "turning right: the right wheel is inside");
            Assert.Greater(left, 0f);
            SteeringModel.Ackermann(-20f, Steering, out left, out right);
            Assert.Less(left, right);
            Assert.Less(right, 0f);
            SteeringModel.Ackermann(0f, Steering, out left, out right);
            Assert.AreEqual(0f, left);
            Assert.AreEqual(0f, right);
        }

        [Test]
        public void DesiredYawRateFlipsWhenReversing()
        {
            float forward = SteeringModel.DesiredYawRate(10f, 10f, 2.8f);
            Assert.Greater(forward, 0f, "steering right turns right");
            Assert.AreEqual(-forward, SteeringModel.DesiredYawRate(-10f, 10f, 2.8f), 1e-5f);
        }

        // ---------------------------------------------------------------- drivetrain

        private static readonly DrivetrainSpec Engine = new(idleRpm: 900f, redlineRpm: 6500f, peakTorque: 120f, gearRatios: new[] { 3.4f, 2.0f, 1.35f, 1.0f }, finalDrive: 8f,
            reverseRatioScale: 1f, efficiency: 0.85f, wheelRadius: 0.34f, shiftUpRpm: 6100f, shiftDownRpm: 2600f, shiftSeconds: 0.18f, clutchSpeed: 3f, rpmResponse: 9f,
            topSpeed: 26f, reverseTopSpeed: 8f, limiterBand: 0.08f);

        [Test]
        public void PedalsResolveToThrottleBrakeAndDirection()
        {
            Drivetrain.ResolvePedals(1f, 10f, out float throttle, out float brake, out bool reverse);
            Assert.AreEqual(1f, throttle);
            Assert.AreEqual(0f, brake);
            Assert.IsFalse(reverse);

            Drivetrain.ResolvePedals(-1f, 10f, out throttle, out brake, out reverse);
            Assert.AreEqual(0f, throttle, "pulling back while rolling forward is the brake");
            Assert.AreEqual(1f, brake);
            Assert.IsFalse(reverse);

            Drivetrain.ResolvePedals(-1f, 0f, out throttle, out brake, out reverse);
            Assert.AreEqual(1f, throttle, "stopped: pulling back is reverse");
            Assert.IsTrue(reverse);

            Drivetrain.ResolvePedals(1f, -5f, out throttle, out brake, out reverse);
            Assert.AreEqual(1f, brake, "pushing forward while reversing is the brake");
            Assert.IsTrue(reverse);

            Drivetrain.ResolvePedals(0f, 5f, out throttle, out brake, out _);
            Assert.AreEqual(0f, throttle);
            Assert.AreEqual(0f, brake);
        }

        [Test]
        public void TorqueCurveHasAPeakAndALimiter()
        {
            Assert.Greater(Drivetrain.TorqueAt(900f, Engine), 0f);
            Assert.AreEqual(120f, Drivetrain.TorqueAt(900f + 0.66f * 5600f, Engine), 0.5f, "peak two thirds up");
            Assert.Less(Drivetrain.TorqueAt(6400f, Engine), 120f);
            Assert.AreEqual(0f, Drivetrain.TorqueAt(6500f, Engine), "the limiter cuts at the redline");
        }

        [Test]
        public void ItPullsAwayShiftsUpAndFadesIntoTheTopSpeed()
        {
            DrivetrainState state = DrivetrainState.AtIdle(Engine);
            DrivetrainOutput fromRest = Drivetrain.Step(state, 0f, 1f, false, Dt, Engine);
            Assert.Greater(fromRest.WheelForce, 3000f, "first gear pulls hard");
            Assert.AreEqual(1, fromRest.State.Gear);

            // Roll the speed up and let the gearbox follow it.
            state = fromRest.State;
            int highest = 1, shifts = 0;
            for (float v = 0f; v <= 26f; v += 0.5f)
            {
                for (int i = 0; i < 10; i++)
                {
                    DrivetrainOutput o = Drivetrain.Step(state, v, 1f, false, Dt, Engine);
                    state = o.State;
                    shifts += o.Shifted ? 1 : 0;
                    Assert.That(state.Rpm, Is.InRange(900f, 6500f), "the engine lives between idle and the redline");
                }

                highest = Mathf.Max(highest, state.Gear);
            }

            Assert.GreaterOrEqual(highest, 3, "it has shifted up on the way to the top speed");
            Assert.GreaterOrEqual(shifts, 2);
            Assert.AreEqual(0f, Drivetrain.Step(state, 26f, 1f, false, Dt, Engine).WheelForce, 1e-3f, "nothing left at the top speed");
            Assert.Greater(Drivetrain.Step(state, 20f, 1f, false, Dt, Engine).WheelForce, 0f);
        }

        [Test]
        public void ReverseIsAGearThatPushesBackwards()
        {
            DrivetrainState state = DrivetrainState.AtIdle(Engine);
            DrivetrainOutput o = Drivetrain.Step(state, 0f, 1f, true, Dt, Engine);
            Assert.AreEqual(-1, o.State.Gear);
            Assert.Less(o.WheelForce, 0f);
            Assert.AreEqual(0f, Drivetrain.Step(o.State, -8f, 1f, true, Dt, Engine).WheelForce, 1e-3f, "reverse has its own top speed");
        }

        [Test]
        public void RevvingOnTheSpotRaisesTheEngineSpeed()
        {
            DrivetrainState state = DrivetrainState.AtIdle(Engine);
            for (int i = 0; i < 100; i++)
            {
                state = Drivetrain.Step(state, 0f, 1f, false, Dt, Engine).State;
            }

            Assert.Greater(state.Rpm01(Engine), 0.4f, "the clutch slips and the engine revs with the pedal");
            for (int i = 0; i < 100; i++)
            {
                state = Drivetrain.Step(state, 0f, 0f, false, Dt, Engine).State;
            }

            Assert.Less(state.Rpm01(Engine), 0.05f, "and settles back to idle");
        }
    }
}
