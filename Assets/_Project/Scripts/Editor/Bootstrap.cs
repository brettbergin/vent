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
