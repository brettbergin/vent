using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Vent.Core;
using Vent.Core.Services;
using Vent.Core.Utility;
using Vent.Gameplay.Flow;

namespace Vent.Tests.PlayMode
{
    /// <summary>
    /// Proves the main menu actually renders at runtime — the regression behind "the room spins but
    /// no menu appears". A UIDocument with a null PanelSettings builds no panel, so its
    /// <c>rootVisualElement</c> stays null and nothing is cloned or laid out. This test asserts the
    /// live visual tree exists, contains the Play button, and that the button is displayed and laid
    /// out (non-zero world rect), which is only possible once the panel renders.
    /// </summary>
    public sealed class MenuRendersTests
    {
        [UnityTest]
        public IEnumerator MainMenuRendersItsVisualTree()
        {
            GameServices.Clear();
            yield return SceneManager.LoadSceneAsync(SceneNames.Boot, LoadSceneMode.Single);

            GameManager manager = null;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (GameServices.TryGet(out manager) && manager.State == GameState.MainMenu)
                {
                    break;
                }

                yield return null;
            }

            Assert.IsNotNull(manager, "GameManager must register itself");
            Assert.AreEqual(GameState.MainMenu, manager.State, "Boot must reach the main menu");

            // Let UI Toolkit build panels and run a couple of layout passes.
            for (int i = 0; i < 4; i++)
            {
                yield return null;
            }

            UIDocument[] docs = Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            Assert.IsNotEmpty(docs, "Boot must contain UIDocuments");

            UIDocument menu = docs.FirstOrDefault(d =>
                d.panelSettings != null && d.rootVisualElement != null && d.rootVisualElement.Q<Button>("play") != null);

            Assert.IsNotNull(menu, "The main-menu document must have a PanelSettings and a live visual tree containing the Play button.");

            Button play = menu.rootVisualElement.Q<Button>("play");
            Assert.AreNotEqual(DisplayStyle.None, play.resolvedStyle.display, "Play button must be displayed in the menu state.");
            Assert.AreEqual("PLAY", play.text.ToUpperInvariant(), "Play button text must be present (theme + UXML loaded).");

            // worldBound is only non-empty once the panel has a real size and has laid out.
            Rect bound = play.worldBound;
            Debug.Log($"[Vent][test] Play button worldBound = {bound}");
            Assert.Greater(bound.width, 0f, "Play button must be laid out with a real width (panel rendered).");
            Assert.Greater(bound.height, 0f, "Play button must be laid out with a real height (panel rendered).");

            // The version label exists so a screenshot or a bug report names the build.
            Label version = menu.rootVisualElement.Q<Label>("version-label");
            Assert.IsNotNull(version, "The menu must show which version is running.");
            Assert.IsNotEmpty(version.text, "The version label must be filled in.");
            StringAssert.StartsWith("v", version.text);
            Debug.Log($"[Vent][test] version label = {version.text}");

            // The update banner is laid out but must stay out of the way until a check finds
            // something. UpdateService does not exist in the editor at all, so this is also the
            // guard that a null service leaves the menu looking normal.
            VisualElement updatePanel = menu.rootVisualElement.Q<VisualElement>("update-panel");
            Assert.IsNotNull(updatePanel, "The update banner must be present in the menu tree.");
            Assert.AreEqual(DisplayStyle.None, updatePanel.resolvedStyle.display,
                            "The update banner must be hidden when there is no update.");

            Object.Destroy(manager.gameObject);
            yield return null;
        }
    }
}
