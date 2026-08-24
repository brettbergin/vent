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
            for (int layer = 0; layer < 32; layer++)
            {
                Physics.IgnoreLayerCollision(weaponView, layer, true);
            }

            Physics.IgnoreLayerCollision(Layers.ZombieIndex, Layers.ZombieIndex, true);
            Physics.IgnoreLayerCollision(Layers.ZombieIndex, Layers.VentIndex, true);
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

        private static void ConfigureBuildScenes()
        {
            var scenes = new EditorBuildSettingsScene[SceneNames.BuildOrder.Length];
            for (int i = 0; i < scenes.Length; i++)
            {
                scenes[i] = new EditorBuildSettingsScene($"{Paths.Scenes}/{SceneNames.BuildOrder[i]}.unity", true);
            }

            EditorBuildSettings.scenes = scenes;
        }
    }
}
