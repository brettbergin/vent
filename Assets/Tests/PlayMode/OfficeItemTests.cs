using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vent.Core;
using Vent.Core.Events;
using Vent.Core.Items;
using Vent.Core.Services;
using Vent.Core.Utility;
using Vent.Gameplay.Flow;
using Vent.Gameplay.World;
using Vent.UI.Screens;

namespace Vent.Tests.PlayMode
{
    /// <summary>
    /// The map and the mirror: one of each is put out per run, in different rooms and away from
    /// the spawn; taking the map gives the HUD a floor plan it shows on the map key; taking the
    /// mirror switches on the rear camera the HUD draws; and a new run takes both away.
    /// </summary>
    public sealed class OfficeItemTests
    {
        private BuildingSceneController building;
        private OfficeItemDirector items;
        private HudScreen hud;
        private VoidEventChannel mapToggled;

        private GameManager manager;

        /// <summary>The Building scene alone: enough for the director and the player's rear camera.</summary>
        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            GameServices.Clear();
            yield return SceneManager.LoadSceneAsync(SceneNames.Building, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(GameServices.TryGet(out building));
            Assert.IsTrue(GameServices.TryGet(out items), "the building scene has an office item director");
            foreach (VoidEventChannel channel in Resources.FindObjectsOfTypeAll<VoidEventChannel>())
            {
                if (channel.name == "Evt_MapToggled")
                {
                    mapToggled = channel;
                }
            }

            Assert.IsNotNull(mapToggled, "Evt_MapToggled exists");
        }

        [TearDown]
        public void TearDown()
        {
            if (manager != null)
            {
                Cursor.lockState = CursorLockMode.None;
                Object.Destroy(manager.gameObject);
                manager = null;
            }
        }

        /// <summary>
        /// The HUD lives in the persistent Boot scene, so the tests that read it start a run the way
        /// the player does: Boot → menu → Play, exactly as BootFlowTests does.
        /// </summary>
        private IEnumerator StartRunThroughBoot()
        {
            GameServices.Clear();
            yield return SceneManager.LoadSceneAsync(SceneNames.Boot, LoadSceneMode.Single);
            float deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline && !(GameServices.TryGet(out manager) && manager.State == GameState.MainMenu))
            {
                yield return null;
            }

            Assert.IsNotNull(manager, "Boot reaches the menu");
            VoidEventChannel playRequested = null;
            foreach (VoidEventChannel channel in Resources.FindObjectsOfTypeAll<VoidEventChannel>())
            {
                if (channel.name == "Evt_PlayRequested")
                {
                    playRequested = channel;
                }
            }

            Assert.IsNotNull(playRequested);
            playRequested.Raise();
            deadline = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < deadline && manager.State != GameState.Playing)
            {
                yield return null;
            }

            Assert.AreEqual(GameState.Playing, manager.State, "Play starts a run");
            Assert.IsTrue(GameServices.TryGet(out building));
            Assert.IsTrue(GameServices.TryGet(out items));
            hud = Object.FindFirstObjectByType<HudScreen>(FindObjectsInactive.Include);
            Assert.IsNotNull(hud, "the HUD came with Boot");
            yield return null;
        }

        private IEnumerator Run(int seed)
        {
            items.SeedOverride = seed;
            building.BeginRun();
            yield return null;
        }

        private static int Shown(System.Collections.Generic.IReadOnlyList<OfficeItemPickup> candidates)
        {
            int shown = 0;
            foreach (OfficeItemPickup item in candidates)
            {
                if (item.gameObject.activeInHierarchy)
                {
                    shown++;
                }
            }

            return shown;
        }

        [UnityTest]
        public IEnumerator ARunPutsOutOneMapAndOneMirrorApart()
        {
            yield return Run(2024);
            Assert.GreaterOrEqual(items.Maps.Count, 6, "enough map spots that it moves between runs");
            Assert.GreaterOrEqual(items.Mirrors.Count, 6);
            Assert.AreEqual(1, Shown(items.Maps), "exactly one map is out");
            Assert.AreEqual(1, Shown(items.Mirrors), "exactly one mirror is out");
            Assert.IsNotNull(items.ActiveMap);
            Assert.IsNotNull(items.ActiveMirror);
            Assert.AreNotEqual(items.ActiveMap.Room, items.ActiveMirror.Room, "not on the same shelf");
            Assert.Greater(Vector3.Distance(items.ActiveMap.transform.position, building.Player.Position), 8f, "the map is not at the spawn");
            Assert.IsNotNull(items.MapTexture, "the director carries the floor plan");
            Assert.Greater(items.MapWorldRect.width, 10f, "and the world extent it covers");
        }

        [UnityTest]
        public IEnumerator DifferentRunsMoveTheItems()
        {
            var mapSpots = new System.Collections.Generic.HashSet<OfficeItemPickup>();
            foreach (int seed in new[] { 1, 2, 3, 4, 5, 6 })
            {
                yield return Run(seed);
                mapSpots.Add(items.ActiveMap);
            }

            Assert.GreaterOrEqual(mapSpots.Count, 2, "the map is not always in the same place");
        }

        [UnityTest]
        public IEnumerator TakingTheMapGivesTheHudAMapOnTheMapKey()
        {
            yield return StartRunThroughBoot();
            yield return Run(99);
            Assert.IsFalse(hud.HasMap);
            mapToggled.Raise();
            Assert.IsFalse(hud.IsMapVisible, "the key does nothing without a map");

            OfficeItemInfo? received = null;
            var channel = Resources.FindObjectsOfTypeAll<OfficeItemEventChannel>()[0];
            channel.Subscribe(info => received = info);
            items.ActiveMap.Interact();
            yield return null;
            Assert.IsTrue(items.HasMap);
            Assert.IsTrue(received.HasValue, "the pickup was announced");
            Assert.AreEqual(OfficeItem.BuildingMap, received.Value.Kind);
            Assert.IsNotNull(received.Value.Map, "with the floor plan");
            Assert.IsFalse(items.ActiveMap.gameObject.activeInHierarchy, "and the sheet is gone from the desk");

            Assert.IsTrue(hud.HasMap);
            mapToggled.Raise();
            Assert.IsTrue(hud.IsMapVisible, "C opens the map");
            mapToggled.Raise();
            Assert.IsFalse(hud.IsMapVisible, "and closes it");
        }

        [UnityTest]
        public IEnumerator TakingTheMirrorSwitchesOnTheRearView()
        {
            yield return StartRunThroughBoot();
            yield return Run(123);
            Assert.IsTrue(GameServices.TryGet(out IRearViewSource rear), "the player carries a rear camera");
            Assert.IsFalse(rear.IsActive, "off until the mirror is found");

            items.ActiveMirror.Interact();
            yield return null;
            Assert.IsTrue(items.HasMirror);
            Assert.IsTrue(rear.IsActive, "the rear camera renders now");
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Assert.IsNotNull(rear.View, "into a texture the HUD can show");
            }
            Assert.IsTrue(hud.HasMirror);
        }

        [UnityTest]
        public IEnumerator ANewRunTakesTheItemsAway()
        {
            yield return StartRunThroughBoot();
            yield return Run(7);
            items.ActiveMap.Interact();
            items.ActiveMirror.Interact();
            yield return null;
            Assert.IsTrue(items.HasMap && items.HasMirror);

            building.EndRun();
            yield return Run(8);
            Assert.IsFalse(items.HasMap);
            Assert.IsFalse(items.HasMirror);
            Assert.IsFalse(hud.HasMap, "the HUD dropped the map");
            Assert.IsFalse(hud.IsMapVisible);
            Assert.IsTrue(GameServices.TryGet(out IRearViewSource rear));
            Assert.IsFalse(rear.IsActive, "the rear camera is off again");
            Assert.AreEqual(1, Shown(items.Maps), "and a fresh map is out");
        }
    }
}
