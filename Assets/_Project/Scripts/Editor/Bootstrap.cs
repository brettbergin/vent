using System;
using UnityEditor;
using UnityEngine;

namespace Vent.Editor
{
    /// <summary>
    /// One-shot regeneration of the whole project from code. Run from the menu, or headless:
    /// <c>Unity -batchmode -quit -projectPath . -executeMethod Vent.Editor.Bootstrap.RebuildAll</c>
    /// </summary>
    public static class Bootstrap
    {
        /// <summary>
        /// Data, materials and prefabs only, in a minute instead of four. The generated scenes hold
        /// prefab instances, so a prefab change reaches them without a scene rebuild; use this while
        /// iterating on a prefab (a car, a gun) and the full rebuild before shipping.
        /// </summary>
        [MenuItem("Vent/Rebuild Assets and Prefabs", priority = -99)]
        public static void RebuildAssetsAndPrefabs()
        {
            try
            {
                Debug.Log("[Vent] Rebuild: data & materials");
                GameAssets assets = AssetFactory.CreateAll();
                AssetDatabase.SaveAssets();

                Debug.Log("[Vent] Rebuild: prefabs");
                PrefabFactory.CreateAll(assets);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[Vent] Prefab rebuild complete.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }

                throw;
            }
        }

        [MenuItem("Vent/Rebuild Everything", priority = -100)]
        public static void RebuildAll()
        {
            try
            {
                Debug.Log("[Vent] Rebuild: project settings");
                ProjectBootstrap.Apply();

                Debug.Log("[Vent] Rebuild: data & materials");
                GameAssets assets = AssetFactory.CreateAll();
                AssetDatabase.SaveAssets();

                Debug.Log("[Vent] Rebuild: prefabs");
                PrefabFactory.CreateAll(assets);
                AssetDatabase.SaveAssets();

                Debug.Log("[Vent] Rebuild: scenes");
                SceneBuilder.BuildAll(assets);
                ProjectBootstrap.ConfigureBuildScenes(); // scenes exist now: refresh the list with real GUIDs

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[Vent] Rebuild complete.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }

                throw;
            }
        }
    }
}
