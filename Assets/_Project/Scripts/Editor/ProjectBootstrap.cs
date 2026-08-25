using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Vent.Core.Utility;

namespace Vent.Editor
{
    /// <summary>
    /// Applies project-wide settings that would otherwise be clicked through in the editor:
    /// physics layers, the collision matrix, player settings and the build scene list.
    /// Idempotent; safe to run on every rebuild.
    /// </summary>
    public static class ProjectBootstrap
    {
        private const int FirstUserLayer = 8;

        [MenuItem("Vent/1. Apply Project Settings")]
        public static void Apply()
        {
            EnsureFolders();
            EnsureLayers();
            ConfigureCollisionMatrix();
            ConfigurePlayerSettings();
            ConfigureAmbientOcclusion();
            ConfigureBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[Vent] Project settings applied.");
        }

        /// <summary>
        /// Screen-space ambient occlusion on the PC renderer: the dark crease where a desk meets the
        /// floor. The template ships it weak; this makes it read.
        /// </summary>
        public static void ConfigureAmbientOcclusion()
        {
            const string rendererPath = "Assets/Settings/PC_Renderer.asset";
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(rendererPath))
            {
                if (sub == null || sub.GetType().Name != "ScreenSpaceAmbientOcclusion")
                {
                    continue;
                }

                var so = new SerializedObject(sub);
                SerializedProperty settings = so.FindProperty("m_Settings");
                settings.FindPropertyRelative("Intensity").floatValue = 1.1f;
                settings.FindPropertyRelative("Radius").floatValue = 0.45f;
                settings.FindPropertyRelative("DirectLightingStrength").floatValue = 0.35f;
                settings.FindPropertyRelative("Samples").intValue = 2; // high
                settings.FindPropertyRelative("Downsample").boolValue = false;
                so.FindProperty("m_Active").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(sub);
            }
        }

        public static void EnsureFolders()
        {
            foreach (string folder in Paths.Folders)
            {
                EnsureFolder(folder);
            }
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string name = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        /// <summary>Writes the project's layer names into TagManager starting at the first user layer.</summary>
        private static void EnsureLayers()
        {
            Object tagManagerAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            var tagManager = new SerializedObject(tagManagerAsset);
            SerializedProperty layers = tagManager.FindProperty("layers");

            for (int i = 0; i < Layers.All.Length; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(FirstUserLayer + i);
                if (slot.stringValue != Layers.All[i])
                {
                    slot.stringValue = Layers.All[i];
                }
            }

            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The view-model layer is render-only: nothing collides with it. Zombies do not
        /// collide with each other (NavMesh avoidance handles crowding) or with bullets' targets
        /// twice; everything else uses the defaults.
        /// </summary>
        /// <summary>
        /// Defines which physics layers may generate contacts. Three pairs are disabled:
        ///  - the view-model layer collides with nothing (it is render-only);
        ///  - zombies do not collide with each other or with vents (they separate via NavMesh
        ///    avoidance, not physics);
        ///  - zombies do not collide with the player. This last one matters: the player is a
        ///    CharacterController, and Move()'s penetration recovery ejects it out of any colliders
        ///    it overlaps. A crowd of zombies pressing in could otherwise squeeze the controller
        ///    straight through a wall and out of the building. Zombie damage is applied in code
        ///    (see Zombie.TryStrike), so removing the physical contact costs nothing.
        /// </summary>
        private static void ConfigureCollisionMatrix()
        {
            int weaponView = Layers.WeaponViewIndex;
            int zombie = Layers.ZombieIndex;
            int vent = Layers.VentIndex;
            int player = Layers.PlayerIndex;
            if (weaponView < 0 || zombie < 0 || vent < 0 || player < 0)
            {
                throw new System.InvalidOperationException("Layers were not created; run EnsureLayers first.");
            }

            var ignored = new List<(int A, int B)>();
            for (int layer = 0; layer < 32; layer++)
            {
                ignored.Add((weaponView, layer));
            }

            ignored.Add((zombie, zombie));
            ignored.Add((zombie, vent));
            ignored.Add((zombie, player));

            foreach ((int a, int b) in ignored)
            {
                Physics.IgnoreLayerCollision(a, b, true);
            }

            // Persist to DynamicsManager so a headless -quit run writes the change to disk.
            // A set bit means "these layers collide"; start from all-collide and clear the ignored pairs.
            Object dynamics = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0];
            var so = new SerializedObject(dynamics);
            SerializedProperty matrix = so.FindProperty("m_LayerCollisionMatrix");
            if (matrix != null && matrix.arraySize == 32)
            {
                var rows = new uint[32];
                for (int i = 0; i < 32; i++)
                {
                    rows[i] = 0xFFFFFFFFu;
                }

                foreach ((int a, int b) in ignored)
                {
                    rows[a] &= ~(1u << b);
                    rows[b] &= ~(1u << a);
                }

                for (int i = 0; i < 32; i++)
                {
                    matrix.GetArrayElementAtIndex(i).uintValue = rows[i];
                }

                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.productName = "Vent";
            PlayerSettings.companyName = "Vent Studio";
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
        }

        /// <summary>Scene list in build order. Re-run after scenes are generated so the entries carry real GUIDs.</summary>
        public static void ConfigureBuildScenes()
        {
            var scenes = new EditorBuildSettingsScene[SceneNames.BuildOrder.Length];
            for (int i = 0; i < scenes.Length; i++)
            {
                string path = $"{Paths.Scenes}/{SceneNames.BuildOrder[i]}.unity";
                // A scene saved moments ago may not be imported yet; the GUID-based entry is what
                // the editor and player use to resolve scenes by name, so make sure it exists.
                if (System.IO.File.Exists(path))
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                }

                GUID guid = AssetDatabase.GUIDFromAssetPath(path);
                scenes[i] = guid.Empty()
                    ? new EditorBuildSettingsScene(path, true)
                    : new EditorBuildSettingsScene(guid, true);
            }

            EditorBuildSettings.scenes = scenes;
        }
    }
}
