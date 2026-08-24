using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Vent.Core.Audio;
using Vent.Core.Data;
using Vent.Core.Events;
using Vent.Enemies.Data;
using Vent.Enemies.Runtime;
using Vent.Enemies.Spawning;
using Vent.Player.Input;
using Vent.Weapons.Data;

namespace Vent.Editor
{
    /// <summary>
    /// Creates (or refreshes) every ScriptableObject and material the game needs. Assets are
    /// looked up by path first so re-running never breaks references: the GUID of an existing
    /// asset is preserved and only its contents are rewritten.
    /// </summary>
    public static class AssetFactory
    {
        [MenuItem("Vent/2. Generate Data & Materials")]
        public static void GenerateMenu()
        {
            ProjectBootstrap.EnsureFolders();
            CreateAll();
            AssetDatabase.SaveAssets();
            Debug.Log("[Vent] Data & materials generated.");
        }

        public static GameAssets CreateAll()
        {
            var a = new GameAssets();

            // ---- Event channels -------------------------------------------------------
            a.Kill = Event<KillEventChannel>("Evt_ZombieKilled", "Raised by a zombie when it dies. Payload: position, killer, headshot, XP.");
            a.Level = Event<LevelEventChannel>("Evt_LevelChanged", "Raised by the LevelDirector when the level changes (including level 1 at run start).");
            a.KillsThisLevel = Event<IntEventChannel>("Evt_KillsThisLevel", "Kills accumulated toward the next level.");
            a.Health = Event<HealthEventChannel>("Evt_PlayerHealth", "Raised on every player health change.");
            a.PlayerDied = Event<VoidEventChannel>("Evt_PlayerDied", "Raised once when player health reaches zero.");
            a.WeaponHud = Event<WeaponHudEventChannel>("Evt_WeaponHud", "Snapshot of the active weapon for the HUD.");
            a.WeaponLevelUp = Event<WeaponLevelUpEventChannel>("Evt_WeaponLevelUp", "A weapon gained a level.");
            a.Hit = Event<BoolEventChannel>("Evt_HitConfirmed", "A shot damaged something. Payload: headshot.");
            a.GameState = Event<GameStateEventChannel>("Evt_GameState", "Application state changed.");
            a.RunSummary = Event<RunSummaryEventChannel>("Evt_RunEnded", "Final tally for the game-over screen.");
            a.BestLevel = Event<IntEventChannel>("Evt_BestLevel", "Best level on record; raised when the menu opens.");
            a.PlayRequested = Event<VoidEventChannel>("Evt_PlayRequested", "UI → GameManager: start a run.");
            a.ResumeRequested = Event<VoidEventChannel>("Evt_ResumeRequested", "UI → GameManager: unpause.");
            a.RestartRequested = Event<VoidEventChannel>("Evt_RestartRequested", "UI → GameManager: restart the run.");
            a.MenuRequested = Event<VoidEventChannel>("Evt_MenuRequested", "UI → GameManager: back to the main menu.");
            a.QuitRequested = Event<VoidEventChannel>("Evt_QuitRequested", "UI → GameManager: quit the application.");

            // ---- Runtime sets ----------------------------------------------------------
            a.Zombies = GetOrCreate<ZombieRuntimeSet>($"{Paths.Data}/Set_Zombies.asset");
            a.Vents = GetOrCreate<VentRuntimeSet>($"{Paths.Data}/Set_Vents.asset");

            // ---- Tuning data -----------------------------------------------------------
            a.Difficulty = GetOrCreate<DifficultyProfile>($"{Paths.Data}/DifficultyProfile.asset", d => d.ApplyDefaults());
            a.Zombie = GetOrCreate<ZombieDefinition>($"{Paths.Data}/Zombie.asset");
            a.WeaponLevels = GetOrCreate<WeaponLevelCurve>($"{Paths.Data}/WeaponLevels_Standard.asset", c => c.ApplyDefaults(25));

            a.Smg = GetOrCreate<WeaponDefinition>($"{Paths.Data}/Weapon_SMG.asset", w => w.Configure(
                "SMG", WeaponSlot.Primary, FireMode.Automatic,
                baseDamage: 22f, rpm: 720f, magSize: 30, reserve: 150, reserveCap: 240, reload: 1.9f, draw: 0.4f,
                spreadBase: 0.7f, spreadMove: 2.2f, spreadShot: 0.28f, bloomMax: 3.5f, recovery: 9f, aimScale: 0.4f,
                vKick: new Vector2(0.45f, 0.8f), hKick: new Vector2(-0.35f, 0.35f),
                sound: SoundId.SmgShot, curve: a.WeaponLevels));

            a.Pistol = GetOrCreate<WeaponDefinition>($"{Paths.Data}/Weapon_Pistol.asset", w => w.Configure(
                "Pistol", WeaponSlot.Secondary, FireMode.SemiAutomatic,
                baseDamage: 38f, rpm: 400f, magSize: 12, reserve: 72, reserveCap: 120, reload: 1.3f, draw: 0.3f,
                spreadBase: 0.35f, spreadMove: 1.6f, spreadShot: 0.6f, bloomMax: 3f, recovery: 11f, aimScale: 0.3f,
                vKick: new Vector2(1.2f, 1.8f), hKick: new Vector2(-0.5f, 0.5f),
                sound: SoundId.PistolShot, curve: a.WeaponLevels));

            // ---- Input -----------------------------------------------------------------
            a.InputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(Paths.InputActions);
            if (a.InputActions == null)
            {
                throw new InvalidOperationException($"Input actions asset not found at {Paths.InputActions}");
            }

            a.InputReader = GetOrCreate<InputReader>($"{Paths.Data}/InputReader.asset", r =>
            {
                var so = new SerializedObject(r);
                so.FindProperty("actions").objectReferenceValue = a.InputActions;
                so.ApplyModifiedPropertiesWithoutUndo();
            });

            // ---- UI panel --------------------------------------------------------------
            a.PanelSettings = CreatePanelSettings();

            // ---- Materials -------------------------------------------------------------
            a.Floor = Lit("M_Floor", new Color(0.32f, 0.33f, 0.34f), smoothness: 0.25f);
            a.Wall = Lit("M_Wall", new Color(0.72f, 0.70f, 0.64f), smoothness: 0.1f);
            a.Ceiling = Lit("M_Ceiling", new Color(0.22f, 0.23f, 0.25f), smoothness: 0.05f);
            a.Trim = Lit("M_Trim", new Color(0.12f, 0.12f, 0.13f), smoothness: 0.3f);
            a.Prop = Lit("M_Prop", new Color(0.45f, 0.36f, 0.25f), smoothness: 0.2f);
            a.PropAlt = Lit("M_PropAlt", new Color(0.30f, 0.38f, 0.32f), smoothness: 0.35f, metallic: 0.3f);
            a.VentMetal = Lit("M_VentMetal", new Color(0.20f, 0.21f, 0.22f), smoothness: 0.6f, metallic: 0.8f);
            a.LightPanel = Lit("M_LightPanel", new Color(0.9f, 0.9f, 0.85f), smoothness: 0.5f, emission: new Color(1.0f, 0.95f, 0.8f) * 2.2f);
            a.ZombieSkin = Lit("M_ZombieSkin", new Color(0.42f, 0.55f, 0.36f), smoothness: 0.35f);
            a.ZombieHead = Lit("M_ZombieHead", new Color(0.48f, 0.58f, 0.40f), smoothness: 0.4f);
            a.GunMetal = Lit("M_GunMetal", new Color(0.16f, 0.17f, 0.19f), smoothness: 0.55f, metallic: 0.35f);
            a.GunAccent = Lit("M_GunAccent", new Color(0.55f, 0.45f, 0.30f), smoothness: 0.4f, metallic: 0.2f);
            a.Concrete = Lit("M_Concrete", new Color(0.5f, 0.5f, 0.5f), smoothness: 0.1f);
            a.Tracer = Unlit("M_Tracer", new Color(1f, 0.85f, 0.45f));
            a.Flash = Unlit("M_Flash", new Color(1f, 0.75f, 0.3f));
            a.Spark = Particle("M_Spark", new Color(0.9f, 0.85f, 0.7f));
            a.Blood = Particle("M_Blood", new Color(0.45f, 0.05f, 0.05f));

            return a;
        }

        // ------------------------------------------------------------------ helpers

        private static T Event<T>(string name, string description) where T : EventChannelBase
        {
            return GetOrCreate<T>($"{Paths.Events}/{name}.asset", e =>
            {
                var so = new SerializedObject(e);
                so.FindProperty("description").stringValue = description;
                so.ApplyModifiedPropertiesWithoutUndo();
            });
        }

        /// <summary>Load the asset at <paramref name="path"/> or create it, then apply <paramref name="configure"/>.</summary>
        public static T GetOrCreate<T>(string path, Action<T> configure = null) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                ProjectBootstrap.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                AssetDatabase.CreateAsset(asset, path);
            }

            configure?.Invoke(asset);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static PanelSettings CreatePanelSettings()
        {
            // UI Toolkit needs a theme; the default runtime theme is a one-line TSS import.
            string themeFullPath = Path.Combine(Directory.GetCurrentDirectory(), Paths.Theme);
            if (!File.Exists(themeFullPath))
            {
                File.WriteAllText(themeFullPath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(Paths.Theme, ImportAssetOptions.ForceSynchronousImport);
            }

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Paths.Theme);
            return GetOrCreate<PanelSettings>(Paths.PanelSettings, p =>
            {
                p.themeStyleSheet = theme;
                p.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                p.referenceResolution = new Vector2Int(1920, 1080);
                p.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                p.match = 0.5f;
                p.sortingOrder = 0;
            });
        }

        private static Material Lit(string name, Color baseColor, float smoothness = 0.3f, float metallic = 0f, Color? emission = null)
        {
            Material m = GetOrCreateMaterial(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", baseColor);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }

            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Unlit(string name, Color color)
        {
            Material m = GetOrCreateMaterial(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Particle(string name, Color color)
        {
            Material m = GetOrCreateMaterial(name, "Universal Render Pipeline/Particles/Unlit");
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material GetOrCreateMaterial(string name, string shaderName)
        {
            string path = $"{Paths.Materials}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"Shader '{shaderName}' not found. Is URP installed?");
            }

            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }

            return m;
        }
    }
}
