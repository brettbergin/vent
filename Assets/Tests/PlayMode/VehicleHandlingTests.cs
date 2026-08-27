using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vent.Core.Services;
using Vent.Core.Utility;
using Vent.Gameplay.Flow;
using Vent.Vehicles.Data;
using Vent.Vehicles.Runtime;

namespace Vent.Tests.PlayMode
{
    /// <summary>
    /// How the car handles, measured on a proving ground: a flat slab 400 m long, inside the physics
    /// world bounds but clear of the district (fourteen seconds of full throttle is a quarter of a kilometre). The sedan pulls away through its gears to its top speed, brakes to a
    /// stop, holds full lock at top speed without lifting a wheel, slides its tail on the handbrake,
    /// takes a kerb at speed without leaving the ground, reverses from rest, runs straight hands-off
    /// and straightens up when the wheel is let go. Input callbacks do not fire in -batchmode, so the
    /// tests drive <see cref="VehicleInput"/> directly.
    /// </summary>
    public sealed class VehicleHandlingTests
    {
        private static readonly Vector3 Origin = new(280f, 0f, 0f);
        private VehicleController car;
        private GameObject ground;
        private GameObject kerb;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            GameServices.Clear();
            yield return SceneManager.LoadSceneAsync(SceneNames.Building, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(GameServices.TryGet(out BuildingSceneController _));
            foreach (VehicleController candidate in UnityEngine.Object.FindObjectsByType<VehicleController>(FindObjectsSortMode.None))
            {
                if (candidate.Definition != null && candidate.Definition.Shape == VehicleShape.Sedan)
                {
                    car = candidate;
                    break;
                }
            }

            Assert.IsNotNull(car, "a sedan is parked in the district");

            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "ProvingGround";
            ground.layer = Layers.EnvironmentIndex;
            ground.transform.position = Origin + Vector3.down;
            ground.transform.localScale = new Vector3(240f, 2f, 400f);
            Place(Origin + new Vector3(0f, 0.02f, -150f), 0f);
        }

        [TearDown]
        public void TearDown()
        {
            if (car != null)
            {
                car.SetInput(default);
                car.SetOccupied(false);
            }

            if (ground != null)
            {
                UnityEngine.Object.Destroy(ground);
            }

            if (kerb != null)
            {
                UnityEngine.Object.Destroy(kerb);
            }

            Time.timeScale = 1f;
        }

        private void Place(Vector3 position, float yaw)
        {
            car.SetOccupied(false);
            car.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            Physics.SyncTransforms();
        }

        /// <summary>Hold an input for a while, sampling the car every physics step.</summary>
        private IEnumerator Drive(float throttle, float steer, bool handbrake, float seconds, Action sample = null, Func<bool> until = null)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                car.SetInput(new VehicleInput(throttle, steer, handbrake));
                yield return new WaitForFixedUpdate();
                sample?.Invoke();
                if (until != null && until())
                {
                    yield break;
                }
            }
        }

        private float Upright => Vector3.Dot(car.transform.up, Vector3.up);

        [UnityTest]
        public IEnumerator PullsAwayThroughTheGearsToItsTopSpeed()
        {
            car.SetOccupied(true);
            int highestGear = 0;
            float highestRpm = 0f;
            yield return Drive(1f, 0f, false, 14f, () =>
            {
                highestGear = Mathf.Max(highestGear, car.Gear);
                highestRpm = Mathf.Max(highestRpm, car.Rpm01);
            });
            Assert.Greater(car.ForwardSpeed, car.Definition.TopSpeed * 0.9f, "reaches its top speed");
            Assert.LessOrEqual(car.ForwardSpeed, car.Definition.TopSpeed * 1.05f, "and no more");
            Assert.GreaterOrEqual(highestGear, 3, "shifted up on the way");
            Assert.Greater(highestRpm, 0.5f, "the engine was heard working");
            Assert.Greater(Upright, 0.99f);
            Assert.AreEqual(4, car.GroundedWheels);
        }

        [UnityTest]
        public IEnumerator BrakesToAStopWithoutReversing()
        {
            car.SetOccupied(true);
            yield return Drive(1f, 0f, false, 6f);
            float speed = car.ForwardSpeed;
            Assert.Greater(speed, 15f);
            Vector3 brakePoint = car.transform.position;
            yield return Drive(-1f, 0f, false, 6f, until: () => car.ForwardSpeed < 0.3f);
            Assert.Less(Mathf.Abs(car.ForwardSpeed), 0.5f, "stopped");
            float distance = Vector3.Distance(brakePoint, car.transform.position);
            Assert.Less(distance, speed * speed / (2f * 6f), "stops within what 0.6 g would take");
            Assert.GreaterOrEqual(car.ForwardSpeed, -0.5f, "holding the brake at a stop does not reverse until the car has stopped");
        }

        [UnityTest]
        public IEnumerator FullLockAtTopSpeedNeverLiftsAWheel()
        {
            car.SetOccupied(true);
            yield return Drive(1f, 0f, false, 8f);
            Assert.Greater(car.ForwardSpeed, 20f, "up to speed");
            float startYaw = car.transform.eulerAngles.y;
            float minUpright = 1f, turned = 0f, lastYaw = startYaw;
            int minGrounded = 4;
            yield return Drive(1f, 1f, false, 5f, () =>
            {
                minUpright = Mathf.Min(minUpright, Upright);
                minGrounded = Mathf.Min(minGrounded, car.GroundedWheels);
                float yaw = car.transform.eulerAngles.y;
                turned += Mathf.DeltaAngle(lastYaw, yaw);
                lastYaw = yaw;
            });
            Assert.Greater(minUpright, 0.9f, "never tipped past 25°");
            Assert.GreaterOrEqual(minGrounded, 3, "never lifted more than one wheel");
            Assert.Greater(turned, 100f, "and it did turn");
            Assert.Greater(car.ForwardSpeed, 8f, "still moving");
        }

        [UnityTest]
        public IEnumerator HandbrakeTurnSlidesTheTail()
        {
            car.SetOccupied(true);
            yield return Drive(1f, 0f, false, 4f);
            Assert.Greater(car.ForwardSpeed, 10f);
            float peakYawRate = 0f, peakSkid = 0f, minUpright = 1f;
            yield return Drive(0f, 1f, true, 1.5f, () =>
            {
                peakYawRate = Mathf.Max(peakYawRate, Mathf.Abs(car.Body.angularVelocity.y));
                peakSkid = Mathf.Max(peakSkid, car.SkidIntensity);
                minUpright = Mathf.Min(minUpright, Upright);
            });
            Assert.Greater(peakYawRate, 0.8f, "the car swings round");
            Assert.Greater(peakSkid, 0.3f, "the tyres are heard");
            Assert.Greater(minUpright, 0.9f, "on its wheels throughout");
        }

        [UnityTest]
        public IEnumerator AKerbAtSpeedIsAbsorbedNotLaunched()
        {
            kerb = GameObject.CreatePrimitive(PrimitiveType.Cube);
            kerb.name = "Kerb";
            kerb.layer = Layers.EnvironmentIndex;
            kerb.transform.position = Origin + new Vector3(0f, 0.075f, -40f);
            kerb.transform.localScale = new Vector3(40f, 0.15f, 3f);
            Physics.SyncTransforms();

            car.SetOccupied(true);
            float startY = car.transform.position.y;
            float maxY = startY, minUpright = 1f, speedAtKerb = 0f;
            bool crossed = false;
            yield return Drive(1f, 0f, false, 12f, () =>
            {
                float z = car.transform.position.z - Origin.z;
                if (!crossed && z > -42f)
                {
                    speedAtKerb = car.ForwardSpeed;
                    crossed = true;
                }

                if (crossed)
                {
                    maxY = Mathf.Max(maxY, car.transform.position.y);
                    minUpright = Mathf.Min(minUpright, Upright);
                }
            }, until: () => car.transform.position.z - Origin.z > -10f);
            Assert.IsTrue(crossed, "reached the kerb");
            Assert.Greater(speedAtKerb, 12f, "hit it at speed");
            Assert.Less(maxY - startY, 0.35f, "rode over the 15 cm kerb without launching");
            Assert.Greater(minUpright, 0.95f, "stayed level");
            Assert.Greater(car.ForwardSpeed, 8f, "and kept going");
            yield return Drive(1f, 0f, false, 1f);
            Assert.AreEqual(4, car.GroundedWheels, "back on all four");
        }

        [UnityTest]
        public IEnumerator ReversesWhenHeldFromRest()
        {
            car.SetOccupied(true);
            yield return Drive(-1f, 0f, false, 2.5f);
            Assert.Less(car.ForwardSpeed, -2f, "backing up");
            Assert.AreEqual(-1, car.Gear);
            Assert.GreaterOrEqual(car.ForwardSpeed, -car.Definition.ReverseTopSpeed * 1.05f);
        }

        [UnityTest]
        public IEnumerator RunsStraightHandsOff()
        {
            car.SetOccupied(true);
            float startX = car.transform.position.x;
            yield return Drive(1f, 0f, false, 6f);
            Assert.Less(Mathf.Abs(car.transform.position.x - startX), 1f, "no drift sideways");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(0f, car.transform.eulerAngles.y)), 3f, "no yaw");
        }

        [UnityTest]
        public IEnumerator LettingGoOfTheWheelStraightensTheCar()
        {
            car.SetOccupied(true);
            yield return Drive(1f, 0f, false, 3f);
            yield return Drive(1f, 1f, false, 1f);
            Assert.Greater(Mathf.Abs(car.Body.angularVelocity.y), 0.3f, "turning");
            yield return Drive(1f, 0f, false, 2f);
            Assert.Less(Mathf.Abs(car.Body.angularVelocity.y), 0.15f, "straight again");
            Assert.Less(Mathf.Abs(car.SteerAngle), 0.5f, "the wheel is centred");
        }
    }
}
