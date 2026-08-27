using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Vent.Core;
using Vent.Core.Audio;
using Vent.Core.Pooling;
using Vent.Enemies.Spawning;
using Vent.Gameplay.Flow;
using Vent.Gameplay.Levels;
using Vent.Gameplay.Perks;
using Vent.Gameplay.Vehicles;
using Vent.Gameplay.World;
using Vent.Player;
using Vent.Vehicles.Runtime;
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
        private static VolumeProfile postFx;

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
            a.ShellCasingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Paths.Prefabs}/VFX_ShellCasing.prefab");
            a.SedanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Paths.Prefabs}/Vehicle_Sedan.prefab");
            a.VanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Paths.Prefabs}/Vehicle_Van.prefab");
        }

        public static void BuildAll(GameAssets a)
        {
            LightingSettings lighting = EnsureLightingSettings();
            postFx = BuildPostProcessProfile();
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
            hud.Configure(a.Health, a.WeaponHud, a.WeaponLevelUp, a.Hit, a.Level, a.KillsThisLevel, a.PerkCollected, a.Prompt, a.Announcement, a.VehicleSpeed, a.Objective);
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
            // Reload PanelSettings from disk rather than trusting a.PanelSettings: reimporting the
            // theme (.tss) between build phases destroys the cached instance, leaving that field a
            // Unity "fake null". The UXML below is reloaded by path for the same reason. A document
            // saved with a null PanelSettings renders nothing at runtime.
            PanelSettings panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(Paths.PanelSettings);
            if (panel == null)
            {
                throw new System.InvalidOperationException($"PanelSettings not found at {Paths.PanelSettings}; run AssetFactory first.");
            }

            var doc = go.AddComponent<UIDocument>();
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{Paths.UI}/{uxml}");
            doc.sortingOrder = order;
            if (doc.visualTreeAsset == null)
            {
                throw new System.InvalidOperationException($"UXML not found: {Paths.UI}/{uxml}");
            }

            PrefabFactory.SetPrivate(doc, "m_PanelSettings", panel);

            return go.AddComponent<T>();
        }

        /// <summary>
        /// Point the UI input module at our own asset's UI map, using the importer's sub-asset references.
        /// The module's <c>actionsAsset</c> setter tries to re-create references for whatever it already
        /// holds (the package defaults assigned by Reset), which throws for a freshly imported asset; so
        /// clear its references first, swap the asset, then assign the persistent sub-asset references.
        /// </summary>
        private static void WireUiActions(InputSystemUIInputModule module, InputActionAsset asset)
        {
            InputActionReference[] refs = AssetDatabase.LoadAllAssetsAtPath(Paths.InputActions).OfType<InputActionReference>().ToArray();
            InputActionReference Find(string name)
            {
                InputActionReference found = refs.FirstOrDefault(r => r.action != null && r.action.actionMap.name == "UI" && r.action.name == name);
                if (found == null)
                {
                    Debug.LogWarning($"[Vent] UI action '{name}' not found in {Paths.InputActions}; UI input for it will be inert.");
                }

                return found;
            }

            module.point = null;
            module.leftClick = null;
            module.rightClick = null;
            module.middleClick = null;
            module.scrollWheel = null;
            module.move = null;
            module.submit = null;
            module.cancel = null;
            module.trackedDevicePosition = null;
            module.trackedDeviceOrientation = null;

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

            var layout = new BuildingLayout { Columns = 1, Rows = 1, Seed = 42, BakeNavMesh = false, FrontDoor = false };
            BuildingGenerator.Result room = BuildingGenerator.Generate(a, layout);

            // A dormant zombie under a vent, for atmosphere.
            if (room.Vents.Count > 0 && a.ZombiePrefab != null)
            {
                var zombie = (GameObject)PrefabUtility.InstantiatePrefab(a.ZombiePrefab);
                zombie.name = "MenuZombie";
                Vector3 pos = room.Vents[0].FloorPosition;
                zombie.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(room.Vents[0].Facing));
                zombie.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = false; // no NavMesh in the menu
            }

            var camGo = new GameObject("MenuCamera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 60f;
            camGo.AddComponent<AudioListener>();
            UniversalAdditionalCameraData camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            var orbit = camGo.AddComponent<MenuCameraOrbit>();
            orbit.Configure(Vector3.up * 1.2f, 3.2f, 0.5f); // height is relative to the pivot

            Save(scene, Paths.MainMenuScene);
            Bake(scene, Paths.MainMenuScene);
        }

        // ------------------------------------------------------------------ Building

        private static void BuildBuilding(GameAssets a, LightingSettings lighting)
        {
            Scene scene = NewScene(lighting);
            ApplyInteriorRenderSettings();

            // The building first (its skyline pushed out past the district), then the district's streets,
            // then one NavMesh over everything walkable, then the door leaves (which must not be baked
            // in) — see BuildingGenerator.BakeNavMesh.
            var districtLayout = new DistrictLayout();
            var layout = new BuildingLayout { BakeNavMesh = false, Apron = false, ExteriorClearHalfExtents = DistrictGenerator.HalfExtents(districtLayout) };
            BuildingGenerator.Result building = BuildingGenerator.Generate(a, layout);
            DistrictGenerator.Result district = DistrictGenerator.Generate(a, districtLayout, building);
            BuildingGenerator.BakeNavMesh(building.Root);
            BuildingGenerator.BuildFrontDoor(a, building, a.Level, a.Announcement, a.KeyFound, district.ExteriorVents);
            // After the NavMesh bake for the same reason the door leaves are: its drawers slide and
            // its coils come and go, so none of it may be static or carved into the walkable surface.
            KeyHuntDirector keyHunt = BuildingGenerator.BuildKeyQuest(a, building, a.Objective, a.Announcement, a.KeyFound);
            VehiclePlacer.Place(a, district.ParkingSpots, null, districtLayout.Seed);

            var systems = new GameObject("Systems");
            systems.transform.position = building.PlayerSpawn; // pooled instances are parked here, on the NavMesh
            var pools = systems.AddComponent<PoolRegistry>();
            SetPrewarm(pools, (a.ZombiePrefab, 12), (a.TracerPrefab, 24), (a.MuzzleFlashPrefab, 6), (a.MuzzleSmokePrefab, 12), (a.ImpactPrefab, 24), (a.BloodImpactPrefab, 24), (a.ShellCasingPrefab, 40), (a.PerkPickupPrefab, 4));

            var spawner = systems.AddComponent<ZombieSpawner>();
            spawner.Configure(a.Difficulty, a.Zombie, a.Zombies, a.Vents, a.Level);

            var director = systems.AddComponent<LevelDirector>();
            director.Configure(a.Difficulty, spawner, a.Kill, a.Level, a.KillsThisLevel);

            var perks = systems.AddComponent<PerkSystem>();
            perks.Configure(a.PerkDrops, a.PerkPickupPrefab, a.Zombies, a.Kill, a.PerkCollected, a.Level);

            var spawnGo = new GameObject("PlayerSpawn");
            spawnGo.transform.SetPositionAndRotation(building.PlayerSpawn, Quaternion.Euler(0f, building.PlayerYaw, 0f));
            var spawnPoint = spawnGo.AddComponent<PlayerSpawnPoint>();

            var playerGo = (GameObject)PrefabUtility.InstantiatePrefab(a.PlayerPrefab);
            playerGo.transform.SetPositionAndRotation(building.PlayerSpawn, Quaternion.Euler(0f, building.PlayerYaw, 0f));
            var player = playerGo.GetComponent<PlayerCharacter>();

            systems.AddComponent<BuildingSceneController>().Configure(director, spawnPoint, player, keyHunt);

            // Outdoors is graded a touch brighter and less muted; the Atmosphere fades this volume in
            // (and the fog out) as the player steps through the front door.
            var outdoorGo = new GameObject("PostProcessingOutdoor");
            Volume outdoor = outdoorGo.AddComponent<Volume>();
            outdoor.isGlobal = true;
            outdoor.priority = 1f;
            outdoor.weight = 0f;
            outdoor.sharedProfile = BuildOutdoorPostProfile();
            systems.AddComponent<Atmosphere>().Configure(building.Footprint, player.Inventory, outdoor);

            var chaseGo = new GameObject("ChaseCamera");
            chaseGo.transform.SetParent(systems.transform, false);
            var chase = chaseGo.AddComponent<VehicleChaseCamera>();
            systems.AddComponent<VehicleDriver>().Configure(player, a.InputReader, chase, a.VehicleSpeed, a.Prompt, a.PlayerDied, a.Level);

            Save(scene, Paths.BuildingScene);
            Bake(scene, Paths.BuildingScene);
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

        /// <summary>
        /// Indoor mood: flat dim ambient, light fog so distant rooms recede, and a global post-processing
        /// volume (bloom off the light panels, ACES tonemap, vignette, grain).
        /// </summary>
        private static void ApplyInteriorRenderSettings()
        {
            // Trilight ambient: brighter from above, darker from below, so unlit corners still read as a room.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.57f, 0.64f);
            RenderSettings.ambientEquatorColor = new Color(0.44f, 0.44f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.23f, 0.25f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.018f;
            RenderSettings.fogColor = new Color(0.10f, 0.08f, 0.09f); // dusk haze, matches the sky seen through the glass
            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>($"{Paths.Materials}/M_Skybox.mat");
            // The sky is for looking at through the glass, not for reflecting off every floor: no default
            // (skybox) reflection probe indoors, or the whole building takes on the dusk tint.
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;

            var volumeGo = new GameObject("PostProcessing");
            Volume volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = postFx != null ? postFx : AssetDatabase.LoadAssetAtPath<VolumeProfile>(Paths.PostProcessProfile);
        }

        /// <summary>
        /// The one post-processing profile both lit scenes share. Rebuilt from code every run so the
        /// look is reviewable in a diff; components are sub-assets, so the profile is a single file.
        /// </summary>
        private static VolumeProfile BuildPostProcessProfile()
        {
            AssetDatabase.DeleteAsset(Paths.PostProcessProfile);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "VentPostFx";
            AssetDatabase.CreateAsset(profile, Paths.PostProcessProfile);

            T Add<T>() where T : VolumeComponent
            {
                T component = profile.Add<T>();
                component.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(component, profile);
                return component;
            }

            Tonemapping tone = Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);

            Bloom bloom = Add<Bloom>();
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.65f);

            // Grading with intent: a cold fluorescent office, warm dusk leaking through the glass. Shadows
            // pull blue, highlights pull amber, and the whole frame sits a touch under-saturated and
            // contrasty so the perk colours and blood read against it.
            ColorAdjustments color = Add<ColorAdjustments>();
            color.postExposure.Override(0.7f); // baked bounce brightened the rooms; pull back a little
            color.contrast.Override(12f);
            color.saturation.Override(-10f);
            color.colorFilter.Override(new Color(0.95f, 0.97f, 1f));

            WhiteBalance balance = Add<WhiteBalance>();
            balance.temperature.Override(-3f);
            balance.tint.Override(1f);

            SplitToning toning = Add<SplitToning>();
            toning.shadows.Override(new Color(0.66f, 0.71f, 0.86f));
            toning.highlights.Override(new Color(1f, 0.92f, 0.80f));
            toning.balance.Override(-5f);

            LiftGammaGain lgg = Add<LiftGammaGain>();
            lgg.lift.Override(new Vector4(1f, 1f, 1f, -0.02f)); // crush the blacks slightly
            lgg.gain.Override(new Vector4(1f, 1f, 1f, 0.02f));

            Vignette vignette = Add<Vignette>();
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.45f);

            FilmGrain grain = Add<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.2f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>The outdoor overlay on top of the shared profile: dusk is brighter and warmer than the office.</summary>
        private static VolumeProfile BuildOutdoorPostProfile()
        {
            AssetDatabase.DeleteAsset(Paths.OutdoorPostProcessProfile);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "VentPostFxOutdoor";
            AssetDatabase.CreateAsset(profile, Paths.OutdoorPostProcessProfile);

            ColorAdjustments color = profile.Add<ColorAdjustments>();
            color.name = nameof(ColorAdjustments);
            AssetDatabase.AddObjectToAsset(color, profile);
            color.postExposure.Override(1.0f);
            color.saturation.Override(-4f);
            color.colorFilter.Override(new Color(1f, 0.97f, 0.94f));

            Vignette vignette = profile.Add<Vignette>();
            vignette.name = nameof(Vignette);
            AssetDatabase.AddObjectToAsset(vignette, profile);
            vignette.intensity.Override(0.12f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static LightingSettings EnsureLightingSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(Paths.LightingSettings);
            if (settings == null)
            {
                settings = new LightingSettings { name = "VentLighting" };
                AssetDatabase.CreateAsset(settings, Paths.LightingSettings);
            }

            // Baked indirect only: the lights stay real-time for direct light and shadows, the
            // lightmaps and probes carry the bounce that fills corners. Tuned for a headless CPU bake
            // in minutes, not hours: the geometry is boxes.
            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
            settings.lightmapResolution = 6f;
            settings.lightmapPadding = 2;
            settings.lightmapMaxSize = 2048;
            settings.directSampleCount = 16;
            settings.indirectSampleCount = 128;
            settings.environmentSampleCount = 64;
            settings.maxBounces = 2;
            settings.ao = true;
            settings.aoMaxDistance = 1.2f;
            settings.aoExponentIndirect = 1f;
            settings.aoExponentDirect = 0f;
            settings.filteringMode = LightingSettings.FilterMode.Auto;
            settings.lightmapCompression = LightmapCompression.NormalQuality;
            settings.prioritizeView = false;
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

        /// <summary>Bake lightmaps, light probes and reflection probes for the open (saved) scene, then save the result.</summary>
        private static void Bake(Scene scene, string path)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Lightmapping.ClearLightingDataAsset();
            if (!Lightmapping.Bake())
            {
                throw new System.InvalidOperationException($"Lighting bake failed for {path}");
            }

            Debug.Log($"[Vent] Baked lighting for {System.IO.Path.GetFileName(path)} in {clock.Elapsed.TotalSeconds:0}s");

            // Reflection probes are realtime (rendered on load); the bake still writes cubemaps for them, and a
            // headless editor cannot render probes correctly anyway. Drop the files so nothing can pick them up.
            string lightingFolder = path.Substring(0, path.Length - ".unity".Length);
            foreach (string guid in AssetDatabase.FindAssets("ReflectionProbe-", new[] { lightingFolder }))
            {
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            }

            Save(scene, path);
        }
    }
}
