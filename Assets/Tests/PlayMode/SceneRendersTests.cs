using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vent.Core.Services;
using Vent.Core.Utility;
using Vent.Gameplay.Flow;

namespace Vent.Tests.PlayMode
{
    /// <summary>
    /// Renders the player camera into a texture and asserts the frame is not a flat colour — the
    /// building must actually draw with the shipped pipeline settings. Editor-only evidence: a
    /// player build can still differ (shader stripping), so a visual check of the build remains
    /// part of verifying rendering changes.
    /// </summary>
    public sealed class SceneRendersTests
    {
        private static (int distinct, float magentaPct, Color32[] px) Capture(Camera cam, string file, int w = 640, int h = 360)
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

            return (distinct.Count, magenta * 100f / px.Length, px);
        }

        /// <summary>Fires the gun and captures the frames while the muzzle flash is alive: Logs/render-shot-*.png.</summary>
        [UnityTest]
        public IEnumerator ShotDrawsWithoutErrors()
        {
            GameServices.Clear();
            yield return SceneManager.LoadSceneAsync(SceneNames.Building, LoadSceneMode.Single);
            yield return null;
            var building = GameServices.Get<BuildingSceneController>();
            var player = GameServices.Get<Vent.Player.PlayerCharacter>();
            building.BeginRun();
            Object.FindFirstObjectByType<Vent.Enemies.Spawning.ZombieSpawner>().StopSpawning();
            for (int i = 0; i < 40; i++) yield return null; // weapon drawn and ready

            Camera cam = Camera.main;
            float worstMagenta = 0f;
            for (int shot = 0; shot < 3; shot++)
            {
                player.Inventory.PullTrigger();
                yield return null;
                player.Inventory.ReleaseTrigger();
                for (int frame = 0; frame < 2; frame++)
                {
                    (int distinct, float magenta, _) = Capture(cam, $"render-shot-{shot}-{frame}.png");
                    worstMagenta = Mathf.Max(worstMagenta, magenta);
                    Assert.Greater(distinct, 20);
                    yield return null;
                }

                for (int i = 0; i < 6; i++) yield return null;
            }

            Assert.Greater(player.Inventory.Current.ShotsFired, 0, "the gun fired");
            Assert.Less(worstMagenta, 0.5f, "magenta pixels during a shot: a VFX material's shader failed");
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
        public IEnumerator BuildingDraws()
        {
            GameServices.Clear();
            yield return SceneManager.LoadSceneAsync(SceneNames.Building, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(GameServices.TryGet(out BuildingSceneController building));
            building.BeginRun();
            for (int i = 0; i < 10; i++) yield return null;

            Camera cam = Camera.main;
            Assert.IsNotNull(cam, "player camera tagged MainCamera");
            var rt = new RenderTexture(640, 360, 24);
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
            System.IO.File.WriteAllBytes("Logs/render-player.png", tex.EncodeToPNG()); // for eyes; the asserts below are the contract
            Object.Destroy(tex);
            Object.Destroy(rt);

            var distinct = new System.Collections.Generic.HashSet<int>();
            int magenta = 0;
            foreach (Color32 c in px)
            {
                distinct.Add((c.r >> 3 << 10) | (c.g >> 3 << 5) | (c.b >> 3));
                if (c.r > 200 && c.b > 200 && c.g < 80) magenta++; // Unity's "shader failed" colour
            }

            Debug.Log($"[SceneRendersTests] {distinct.Count} distinct colours, magenta={magenta * 100f / px.Length:0.0}%, centre sample={px[px.Length / 2]}");
            Assert.Greater(distinct.Count, 20, "frame is nearly flat — the building is not drawing");
            Assert.Less(magenta * 100f / px.Length, 1f, "magenta pixels: a material's shader variant failed");
        }
    }
}
