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
            ConfigureBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[Vent] Project settings applied.");
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
        private static void ConfigureCollisionMatrix()
        {
            int weaponView = Layers.WeaponViewIndex;
            int zombie = Layers.ZombieIndex;
            int vent = Layers.VentIndex;
            if (weaponView < 0 || zombie < 0 || vent < 0)
            {
                throw new System.InvalidOperationException("Layers were not created; run EnsureLayers first.");
            }

            for (int layer = 0; layer < 32; layer++)
            {
                Physics.IgnoreLayerCollision(weaponView, layer, true);
            }

            Physics.IgnoreLayerCollision(zombie, zombie, true);
            Physics.IgnoreLayerCollision(zombie, vent, true);

            // Physics.IgnoreLayerCollision updates the live matrix; write the serialized matrix too so
            // the change is guaranteed to reach DynamicsManager.asset in a headless -quit run.
            Object dynamics = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0];
            var so = new SerializedObject(dynamics);
            SerializedProperty matrix = so.FindProperty("m_LayerCollisionMatrix");
            if (matrix != null && matrix.arraySize == 32)
            {
                for (int i = 0; i < 32; i++)
                {
                    SerializedProperty row = matrix.GetArrayElementAtIndex(i);
                    uint bits = row.uintValue;
                    bits &= ~(1u << weaponView);
                    if (i == weaponView)
                    {
                        bits = 0u;
                    }

                    if (i == zombie)
                    {
                        bits &= ~(1u << zombie);
                        bits &= ~(1u << vent);
                    }

                    if (i == vent)
                    {
                        bits &= ~(1u << zombie);
                    }

                    row.uintValue = bits;
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
