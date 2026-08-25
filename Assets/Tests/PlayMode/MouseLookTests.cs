using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vent.Core.Services;
using Vent.Core.Utility;
using Vent.Gameplay.Flow;
using Vent.Player;
using Vent.Player.Input;
using Vent.Player.Movement;

namespace Vent.Tests.PlayMode
{
    /// <summary>
    /// Drives synthetic mouse and keyboard state through the REAL Input System (virtual devices,
    /// queued state events, the normal player-loop update) into the generated Building scene and
    /// asserts the player responds. WASD and mouse look take different paths (polled value vs.
    /// performed-callback delta consumed in LateUpdate), so both are covered. No InputTestFixture:
    /// its reset tears the state out from under the game's already-enabled action maps.
    /// </summary>
    public sealed class MouseLookTests
    {
        private PlayerCharacter player;
        private InputReader reader;
        private InputDevice device;
        private InputSettings.BackgroundBehavior previousBackground;
        private bool previousRunInBackground;

        [SetUp]
        public void IgnoreFocus()
        {
            if (Application.isBatchMode)
            {
                // A headless editor never dispatches Input System action callbacks (verified: the device
                // reads the queued delta but no action performs). Run these from the Editor or a
                // windowed `-runTests` session; tools/test.sh reports them as skipped.
                Assert.Ignore("Input action callbacks do not fire in -batchmode; run in a windowed editor.");
            }

            // Headless test runs are never focused; by default the Input System then disables every
            // non-background device, so virtual devices would go silent.
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousRunInBackground = Application.runInBackground;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.runInBackground = true;
        }

        [TearDown]
        public void RemoveDevice()
        {
            InputSystem.settings.backgroundBehavior = previousBackground;
            Application.runInBackground = previousRunInBackground;
            if (device != null)
            {
                InputSystem.RemoveDevice(device);
                device = null;
            }
        }

        private IEnumerator LoadAndStart()
        {
            GameServices.Clear();
            yield return SceneManager.LoadSceneAsync(SceneNames.Building, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(GameServices.TryGet(out BuildingSceneController building));
            Assert.IsTrue(GameServices.TryGet(out player));
            Cursor.lockState = CursorLockMode.None;

            reader = player.GetComponentInChildren<PlayerLook>(true).Input;
            Assert.IsNotNull(reader, "PlayerLook must reference the InputReader asset");
            building.BeginRun();
            reader.EnableGameplay();
            yield return null;
        }

        [UnityTest]
        public IEnumerator MouseDeltaTurnsThePlayer()
        {
            yield return LoadAndStart();
            var mouse = InputSystem.AddDevice<Mouse>("TestMouse");
            device = mouse;

            float yawBefore = player.transform.eulerAngles.y;
            for (int i = 0; i < 5; i++)
            {
                InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(40f, 0f) });
                InputSystem.Update(); // process the queued event now rather than waiting for the player loop
                yield return null;
            }

            float turned = Mathf.Abs(Mathf.DeltaAngle(yawBefore, player.transform.eulerAngles.y));
            Assert.Greater(turned, 2f, "five frames of +40px mouse delta should yaw the player noticeably");
        }

        [UnityTest]
        public IEnumerator KeyboardMovesThePlayer()
        {
            yield return LoadAndStart();
            var keyboard = InputSystem.AddDevice<Keyboard>("TestKeyboard");
            device = keyboard;

            Vector3 before = player.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            for (int i = 0; i < 20; i++)
            {
                InputSystem.Update(); // process the queued event now rather than waiting for the player loop
                yield return null;
            }

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            yield return null;
            Assert.Greater(Vector3.Distance(before, player.transform.position), 0.2f, "holding W should move the player");
        }
    }
}
