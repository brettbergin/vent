using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Vent.Core;
using Vent.Core.Audio;
using Vent.Core.Pooling;
using Vent.Enemies.Spawning;
using Vent.Gameplay.Flow;
using Vent.Gameplay.Levels;
using Vent.Player;
using Vent.UI;
using Vent.UI.Screens;

namespace Vent.Editor
{
    /// <summary>
    /// Assembles the three scenes:
    ///   Boot      – persistent app root (game manager, audio, event system, UI screens)
    ///   MainMenu  – a single lit room as a backdrop with an orbiting camera
    ///   Building  – the generated level plus its systems and the player
    /// </summary>
    public static class SceneBuilder
    {
        [MenuItem("Vent/4. Generate Scenes")]
        public static void GenerateMenu()
        {
            GameAssets a = AssetFactory.CreateAll();
            LoadPrefabs(a);
            BuildAll(a);
            Debug.Log("[Vent] Scenes generated.");
        }

        /// <summary>Resolve prefab handles when running without the prefab factory (menu path).</summary>
        public static void LoadPrefabs(GameAssets a)
        {
            a.PlayerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Paths.Prefabs}/Player.prefab");
            a.ZombiePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Paths.Prefabs}/Zombie.prefab");
            a.VentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Paths.Prefabs}/Vent.prefab");
            a.MuzzleFlashPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Paths.Prefabs}/VFX_MuzzleFlash.prefab");
            a.TracerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Paths.Prefabs}/VFX_Tracer.prefab");
            a.ImpactPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Paths.Prefabs}/VFX_Impact.prefab");
            a.BloodImpactPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Paths.Prefabs}/VFX_BloodImpact.prefab");
        }

        public static void BuildAll(GameAssets a)
        {
            LightingSettings lighting = EnsureLightingSettings();
            BuildBoot(a, lighting);
            BuildMainMenu(a, lighting);
            BuildBuilding(a, lighting);
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ Boot

        private static void BuildBoot(GameAssets a, LightingSettings lighting)
        {
            Scene scene = NewScene(lighting);

            var app = new GameObject("App");
            var manager = app.AddComponent<GameManager>();
            manager.Configure(a.InputReader, a.PlayRequested, a.ResumeRequested, a.RestartRequested, a.MenuRequested,
                a.QuitRequested, a.PlayerDied, a.GameState, a.RunSummary, a.BestLevel);

            var audio = new GameObject("Audio");
            audio.transform.SetParent(app.transform, false);
            audio.AddComponent<SfxPlayer>();

            var eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.transform.SetParent(app.transform, false);
            eventSystemGo.AddComponent<EventSystem>();
            var module = eventSystemGo.AddComponent<InputSystemUIInputModule>();
            WireUiActions(module, a.InputActions);

            var ui = new GameObject("UI");
            ui.transform.SetParent(app.transform, false);

            HudScreen hud = Screen<HudScreen>(ui.transform, "HUD", a, "Hud.uxml", 0);
            hud.Configure(a.Health, a.WeaponHud, a.WeaponLevelUp, a.Hit, a.Level, a.KillsThisLevel);
            hud.ConfigureVisibility(a.GameState, GameState.Playing);

            MainMenuScreen menu = Screen<MainMenuScreen>(ui.transform, "MainMenu", a, "MainMenu.uxml", 5);
            menu.Configure(a.PlayRequested, a.QuitRequested, a.BestLevel);
            menu.ConfigureVisibility(a.GameState, GameState.MainMenu);

            PauseScreen pause = Screen<PauseScreen>(ui.transform, "Pause", a, "Pause.uxml", 10);
            pause.Configure(a.ResumeRequested, a.RestartRequested, a.MenuRequested);
            pause.ConfigureVisibility(a.GameState, GameState.Paused);

            GameOverScreen over = Screen<GameOverScreen>(ui.transform, "GameOver", a, "GameOver.uxml", 10);
            over.Configure(a.RestartRequested, a.MenuRequested, a.RunSummary);
            over.ConfigureVisibility(a.GameState, GameState.GameOver);

            Save(scene, Paths.BootScene);
        }

        private static T Screen<T>(Transform parent, string name, GameAssets a, string uxml, int order) where T : UIScreen
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = a.PanelSettings;
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{Paths.UI}/{uxml}");
            doc.sortingOrder = order;
            if (doc.visualTreeAsset == null)
            {
                throw new System.InvalidOperationException($"UXML not found: {Paths.UI}/{uxml}");
            }

            return go.AddComponent<T>();
        }

        /// <summary>Point the UI input module at our own asset's UI map, using the importer's sub-asset references.</summary>
        private static void WireUiActions(InputSystemUIInputModule module, InputActionAsset asset)
        {
            InputActionReference[] refs = AssetDatabase.LoadAllAssetsAtPath(Paths.InputActions).OfType<InputActionReference>().ToArray();
            InputActionReference Find(string name) => refs.FirstOrDefault(r => r.action != null && r.action.actionMap.name == "UI" && r.action.name == name);

            module.actionsAsset = asset;
            module.point = Find("Point");
            module.leftClick = Find("Click");
            module.rightClick = Find("RightClick");
            module.middleClick = Find("MiddleClick");
            module.scrollWheel = Find("ScrollWheel");
            module.move = Find("Navigate");
            module.submit = Find("Submit");
            module.cancel = Find("Cancel");
        }

        // ------------------------------------------------------------------ Main menu

        private static void BuildMainMenu(GameAssets a, LightingSettings lighting)
        {
            Scene scene = NewScene(lighting);
            ApplyInteriorRenderSettings();

            var layout = new BuildingLayout { Columns = 1, Rows = 1, Seed = 42, BakeNavMesh = false };
            BuildingGenerator.Result room = BuildingGenerator.Generate(a, layout);

            // A dormant zombie under a vent, for atmosphere.
            if (room.Vents.Count > 0 && a.ZombiePrefab != null)
            {
                var zombie = (GameObject)PrefabUtility.InstantiatePrefab(a.ZombiePrefab);
                zombie.name = "MenuZombie";
                Vector3 pos = room.Vents[0].FloorPosition;
                zombie.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-room.Vents[0].Facing));
            }

            var camGo = new GameObject("MenuCamera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 60f;
            camGo.AddComponent<AudioListener>();
            var orbit = camGo.AddComponent<MenuCameraOrbit>();
            orbit.Configure(Vector3.up * 1.2f, 3.2f, 1.7f);

            Save(scene, Paths.MainMenuScene);
        }

        // ------------------------------------------------------------------ Building

        private static void BuildBuilding(GameAssets a, LightingSettings lighting)
        {
            Scene scene = NewScene(lighting);
            ApplyInteriorRenderSettings();

            BuildingGenerator.Result building = BuildingGenerator.Generate(a, new BuildingLayout());

            var systems = new GameObject("Systems");
            var pools = systems.AddComponent<PoolRegistry>();
            SetPrewarm(pools, (a.ZombiePrefab, 12), (a.TracerPrefab, 24), (a.MuzzleFlashPrefab, 6), (a.ImpactPrefab, 24), (a.BloodImpactPrefab, 24));

            var spawner = systems.AddComponent<ZombieSpawner>();
            spawner.Configure(a.Difficulty, a.Zombie, a.Zombies, a.Vents, a.Level);

            var director = systems.AddComponent<LevelDirector>();
            director.Configure(a.Difficulty, spawner, a.Kill, a.Level, a.KillsThisLevel);

            var spawnGo = new GameObject("PlayerSpawn");
            spawnGo.transform.SetPositionAndRotation(building.PlayerSpawn, Quaternion.Euler(0f, building.PlayerYaw, 0f));
            var spawnPoint = spawnGo.AddComponent<PlayerSpawnPoint>();

            var playerGo = (GameObject)PrefabUtility.InstantiatePrefab(a.PlayerPrefab);
            playerGo.transform.SetPositionAndRotation(building.PlayerSpawn, Quaternion.Euler(0f, building.PlayerYaw, 0f));
            var player = playerGo.GetComponent<PlayerCharacter>();

            systems.AddComponent<BuildingSceneController>().Configure(director, spawnPoint, player);

            Save(scene, Paths.BuildingScene);
        }

        private static void SetPrewarm(PoolRegistry registry, params (GameObject prefab, int count)[] entries)
        {
            var so = new SerializedObject(registry);
            SerializedProperty list = so.FindProperty("prewarm");
            list.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Prefab").objectReferenceValue = entries[i].prefab;
                element.FindPropertyRelative("Count").intValue = entries[i].count;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ common

        private static Scene NewScene(LightingSettings lighting)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Lightmapping.lightingSettings = lighting;
            return scene;
        }

        /// <summary>Indoor mood: flat dim ambient and light fog so distant rooms recede.</summary>
        private static void ApplyInteriorRenderSettings()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.17f, 0.2f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.03f;
            RenderSettings.fogColor = new Color(0.04f, 0.045f, 0.05f);
            RenderSettings.skybox = null;
        }

        private static LightingSettings EnsureLightingSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(Paths.LightingSettings);
            if (settings == null)
            {
                settings = new LightingSettings { name = "VentLighting" };
                AssetDatabase.CreateAsset(settings, Paths.LightingSettings);
            }

            settings.bakedGI = false;
            settings.realtimeGI = false;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static void Save(Scene scene, string path)
        {
            if (!EditorSceneManager.SaveScene(scene, path))
            {
                throw new System.InvalidOperationException($"Failed to save scene {path}");
            }
        }
    }
}
