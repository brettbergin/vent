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
            var rt = new RenderTexture(160, 90, 24);
            RenderTexture previous = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = previous;

            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = null;
            Color32[] px = tex.GetPixels32();
            Object.Destroy(tex);
            Object.Destroy(rt);

            var distinct = new System.Collections.Generic.HashSet<int>();
            foreach (Color32 c in px) distinct.Add((c.r >> 3 << 10) | (c.g >> 3 << 5) | (c.b >> 3));
            Debug.Log($"[SceneRendersTests] {distinct.Count} distinct colours, centre sample={px[px.Length / 2]}");
            Assert.Greater(distinct.Count, 20, "frame is nearly flat — the building is not drawing");
        }
    }
}
