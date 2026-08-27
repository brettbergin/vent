using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vent.Core.Services;
using Vent.Core.Utility;
using Vent.Gameplay.Flow;
using Vent.Gameplay.Vehicles;
using Vent.Player;
using Vent.Vehicles.Runtime;

namespace Vent.Tests.PlayMode
{
    /// <summary>
    /// Drives the hero car with the real hand-off (driver, chase camera, arm out of the window) and
    /// captures what the player sees — pulling away, mid-corner with the body leaning and the wheels
    /// turned, and braking with the tail lamps lit — plus a kerbside view of the headlamp beams.
    /// The frames land in Logs/render-drive-*.png for eyes; the asserts are that the chase camera
    /// draws a real scene and no shader fails. Editor-only evidence, like SceneRendersTests.
    /// </summary>
    public sealed class VehicleRendersTests
    {
        private static (int distinct, float magentaPct) Capture(Camera cam, string file, int w = 960, int h = 540)
        {
            var rt = new RenderTexture(w, h, 24);
            RenderTexture previous = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = previous;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = null;
            Color32[] px = tex.GetPixels32();
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllBytes($"Logs/{file}", tex.EncodeToPNG());
            Object.Destroy(tex);
            Object.Destroy(rt);
            var distinct = new System.Collections.Generic.HashSet<int>();
            int magenta = 0;
            foreach (Color32 c in px)
            {
                distinct.Add((c.r >> 3 << 10) | (c.g >> 3 << 5) | (c.b >> 3));
                if (c.r > 200 && c.b > 200 && c.g < 80) magenta++;
            }

            return (distinct.Count, magenta * 100f / px.Length);
        }

        [SetUp]
        public void RequireGpu()
        {
            if (Application.isBatchMode)
            {
                Assert.Ignore("Needs a GPU-backed editor; skipped in -batchmode.");
            }
        }

        [UnityTest]
        public IEnumerator DrivingDrawsFromTheChaseCamera()
        {
            GameServices.Clear();
            yield return SceneManager.LoadSceneAsync(SceneNames.Building, LoadSceneMode.Single);
            yield return null;
            var building = GameServices.Get<BuildingSceneController>();
            var player = GameServices.Get<PlayerCharacter>();
            var driver = (VehicleDriver)GameServices.Get<IVehicleOccupant>();
            building.BeginRun();
            Object.FindFirstObjectByType<Vent.Enemies.Spawning.ZombieSpawner>().StopSpawning();
            yield return null;

            // Outdoors the Atmosphere thins the office haze once the door opens; do the same here.
            RenderSettings.fogDensity = 0.0032f;

            VehicleSeat hero = null;
            float best = float.MaxValue;
            foreach (VehicleSeat seat in Object.FindObjectsByType<VehicleSeat>(FindObjectsSortMode.None))
            {
                float d = Vector3.Distance(seat.transform.position, player.Position);
                if (d < best)
                {
                    best = d;
                    hero = seat;
                }
            }

            Assert.IsNotNull(hero);
            VehicleController car = hero.Controller;
            // Put the hero car on the avenue outside the office, nose north, before getting in.
            car.transform.SetPositionAndRotation(new Vector3(62f, -0.13f, -60f), Quaternion.identity);
            Physics.SyncTransforms();
            Assert.IsTrue(driver.TryEnter(hero));
            for (int i = 0; i < 10; i++) yield return null;

            Camera cam = Camera.main;
            float worstMagenta = 0f;
            void Check((int distinct, float magenta) result, string what)
            {
                worstMagenta = Mathf.Max(worstMagenta, result.magenta);
                Assert.Greater(result.distinct, 40, $"{what}: the chase camera draws a real scene");
            }

            // A kerbside camera for the headlamps: ahead and to the left of the car, looking back at it.
            var sideGo = new GameObject("KerbsideCamera");
            var side = sideGo.AddComponent<Camera>();
            side.CopyFrom(cam);
            side.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            side.enabled = false;
            sideGo.transform.position = car.transform.position + car.transform.forward * 9f - car.transform.right * 5f + Vector3.up * 1.4f;
            sideGo.transform.LookAt(car.transform.position + Vector3.up * 0.8f);
            Check(Capture(side, "render-drive-headlamps.png"), "headlamps");

            Check(Capture(cam, "render-drive-0-seated.png"), "seated");
            yield return Drive(car, 1f, 0f, false, 2.5f);
            Check(Capture(cam, "render-drive-1-pulling-away.png"), "pulling away");
            Assert.Greater(car.ForwardSpeed, 5f, "the driver's input reaches the car");
            yield return Drive(car, 1f, 1f, false, 1.2f);
            Check(Capture(cam, "render-drive-2-cornering.png"), "cornering");
            Assert.Greater(Mathf.Abs(car.SteerAngle), 2f, "the wheels are turned");
            yield return Drive(car, -1f, 0f, false, 0.6f);
            Check(Capture(cam, "render-drive-3-braking.png"), "braking");
            Assert.IsTrue(car.IsBraking);
            sideGo.transform.position = car.transform.position - car.transform.forward * 8f + car.transform.right * 3f + Vector3.up * 1.3f;
            sideGo.transform.LookAt(car.transform.position + Vector3.up * 0.7f);
            Check(Capture(side, "render-drive-4-taillamps.png"), "tail lamps");

            Assert.Less(worstMagenta, 0.5f, "magenta pixels while driving: a material's shader failed");
            Assert.Greater(Vector3.Dot(car.transform.up, Vector3.up), 0.95f, "still on its wheels");
            driver.Exit();
            Object.Destroy(sideGo);
            yield return null;
        }

        /// <summary>Hold an input for a while. The driver writes the (idle) controls in Update; a coroutine resumes after Update, so this wins for the next physics steps.</summary>
        private static IEnumerator Drive(VehicleController car, float throttle, float steer, bool handbrake, float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                car.SetInput(new VehicleInput(throttle, steer, handbrake));
                yield return null;
            }
        }
    }
}
