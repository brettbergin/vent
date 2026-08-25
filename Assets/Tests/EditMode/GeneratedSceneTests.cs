using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Vent.Core.Utility;

namespace Vent.Tests.EditMode
{
    /// <summary>
    /// Guards the generated scenes against wiring that compiles and passes logic tests but renders
    /// nothing. The original bug this covers: UIDocuments saved with a null PanelSettings, so the
    /// menu/HUD were invisible even though every state transition worked.
    /// </summary>
    public sealed class GeneratedSceneTests
    {
        [Test]
        public void EveryUIDocumentInBootIsFullyWired()
        {
            string path = $"{Vent.Editor.Paths.BootScene}";
            Assert.IsTrue(System.IO.File.Exists(path), $"Boot scene missing at {path}; run Vent/Rebuild Everything.");

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            try
            {
                var documents = new List<UIDocument>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    documents.AddRange(root.GetComponentsInChildren<UIDocument>(includeInactive: true));
                }

                Assert.AreEqual(4, documents.Count, "Boot should host HUD, MainMenu, Pause and GameOver documents.");
                foreach (UIDocument doc in documents)
                {
                    Assert.IsNotNull(doc.panelSettings, $"{doc.name}: PanelSettings must be assigned or the document renders nothing.");
                    Assert.IsNotNull(doc.visualTreeAsset, $"{doc.name}: source UXML must be assigned.");
                    Assert.IsNotNull(doc.panelSettings.themeStyleSheet, $"{doc.name}: PanelSettings needs a theme style sheet.");
                }
            }
            finally
            {
                EditorSceneManager.OpenScene($"{Vent.Editor.Paths.Scenes}/{SceneNames.Boot}.unity", OpenSceneMode.Single);
            }
        }
    }
}
