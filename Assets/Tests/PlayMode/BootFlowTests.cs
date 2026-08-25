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

namespace Vent.Tests.PlayMode
{
    /// <summary>
    /// Smoke test for the application flow: Boot must reach the main menu on its own, and a
    /// Play request must land the player in the Building scene in the Playing state.
    /// Exercises the persistent App root, the UI documents and the input module wiring.
    /// </summary>
    public sealed class BootFlowTests
    {
        [UnityTest]
        public IEnumerator BootReachesMenuThenPlayStartsARun()
        {
            GameServices.Clear();
            yield return SceneManager.LoadSceneAsync(SceneNames.Boot, LoadSceneMode.Single);

            float deadline = Time.realtimeSinceStartup + 10f;
            GameManager manager = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (GameServices.TryGet(out manager) && manager.State == GameState.MainMenu)
                {
                    break;
                }

                yield return null;
            }

            Assert.IsNotNull(manager, "GameManager must register itself");
            Assert.AreEqual(GameState.MainMenu, manager.State, "Boot must auto-load the main menu");
            Assert.AreEqual(SceneNames.MainMenu, SceneManager.GetActiveScene().name);

            // The UI raises this when the Play button is clicked; raising it directly exercises the same path.
            var play = Resources.FindObjectsOfTypeAll<VoidEventChannel>();
            VoidEventChannel playRequested = null;
            foreach (VoidEventChannel channel in play)
            {
                if (channel.name == "Evt_PlayRequested")
                {
                    playRequested = channel;
                }
            }

            Assert.IsNotNull(playRequested, "Evt_PlayRequested asset must be loaded with the Boot scene");
            playRequested.Raise();

            deadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < deadline && manager.State != GameState.Playing)
            {
                yield return null;
            }

            Assert.AreEqual(GameState.Playing, manager.State, "Play request must start a run");
            Assert.AreEqual(SceneNames.Building, SceneManager.GetActiveScene().name);
            Assert.IsTrue(GameServices.TryGet(out BuildingSceneController building) && building.Director.IsRunning);
            Assert.AreEqual(CursorLockMode.Locked, Cursor.lockState);

            // Leave things tidy for the next test: back to the menu.
            Cursor.lockState = CursorLockMode.None;
            Object.Destroy(manager.gameObject);
            yield return null;
        }
    }
}
