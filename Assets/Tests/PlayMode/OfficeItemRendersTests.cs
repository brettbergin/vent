using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vent.Core;
using Vent.Core.Events;
using Vent.Core.Services;
using Vent.Core.Utility;
using Vent.Gameplay.Flow;
using Vent.Gameplay.World;

namespace Vent.Tests.PlayMode
{
    /// <summary>
    /// The rear-view mirror actually renders: start a run, take the mirror, and read the rear
    /// camera's texture back — it must be a real picture of the room, not a flat colour. The frame
    /// is written to Logs/render-rearview.png for eyes. Editor-only evidence, like SceneRendersTests.
    /// </summary>
    public sealed class OfficeItemRendersTests
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
        public IEnumerator TheMirrorRendersTheRoomBehindThePlayer()
        {
            GameServices.Clear();
            yield return SceneManager.LoadSceneAsync(SceneNames.Boot, LoadSceneMode.Single);
            GameManager manager = null;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < deadline && !(GameServices.TryGet(out manager) && manager.State == GameState.MainMenu))
            {
                yield return null;
            }

            Assert.IsNotNull(manager);
            VoidEventChannel play = null;
            foreach (VoidEventChannel channel in Resources.FindObjectsOfTypeAll<VoidEventChannel>())
            {
                if (channel.name == "Evt_PlayRequested")
                {
                    play = channel;
                }
            }

            play.Raise();
            deadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < deadline && manager.State != GameState.Playing)
            {
                yield return null;
            }

            Assert.AreEqual(GameState.Playing, manager.State);
            Assert.IsTrue(GameServices.TryGet(out OfficeItemDirector items));
            items.ActiveMirror.Interact();
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.IsTrue(GameServices.TryGet(out IRearViewSource rear) && rear.IsActive && rear.View != null, "the rear camera is on with a texture");
            var tex = new Texture2D(rear.View.width, rear.View.height, TextureFormat.RGB24, false);
            RenderTexture.active = rear.View;
            tex.ReadPixels(new Rect(0, 0, rear.View.width, rear.View.height), 0, 0);
            RenderTexture.active = null;
            Color32[] px = tex.GetPixels32();
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllBytes("Logs/render-rearview.png", tex.EncodeToPNG());
            var distinct = new System.Collections.Generic.HashSet<int>();
            foreach (Color32 c in px)
            {
                distinct.Add((c.r >> 3 << 10) | (c.g >> 3 << 5) | (c.b >> 3));
            }

            Assert.Greater(distinct.Count, 20, "the mirror shows the room, not a flat colour");
            Cursor.lockState = CursorLockMode.None;
            Object.Destroy(manager.gameObject);
            yield return null;
        }
    }
}
