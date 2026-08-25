using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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

        [Test]
        public void BuildingSceneHasGlobalPostProcessingWithShadowedLights()
        {
            string path = Vent.Editor.Paths.BuildingScene;
            Assert.IsTrue(System.IO.File.Exists(path), $"Building scene missing at {path}; run Vent/Rebuild Everything.");

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            try
            {
                Volume global = null;
                var lights = new List<Light>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Volume v in root.GetComponentsInChildren<Volume>(includeInactive: true))
                    {
                        if (v.isGlobal) global = v;
                    }

                    lights.AddRange(root.GetComponentsInChildren<Light>(includeInactive: true));
                }

                Assert.IsNotNull(global, "Building needs a global post-processing Volume.");
                Assert.IsNotNull(global.sharedProfile, "Global Volume must reference the generated profile asset.");
                Assert.IsTrue(global.sharedProfile.Has<Tonemapping>(), "Profile should tonemap (HDR grading is on in the URP asset).");
                Assert.IsTrue(global.sharedProfile.Has<Bloom>(), "Profile should bloom so the emissive light panels glow.");

                var roomLights = lights.FindAll(l => l.type == LightType.Point && l.name.StartsWith("Light_"));
                Assert.IsNotEmpty(roomLights, "Generated rooms should each carry a point light.");
                Assert.IsTrue(roomLights.TrueForAll(l => l.shadows == LightShadows.None), "Room point lights must not cast: six atlas slices each would overflow the shadow atlas and make window lights blink.");
                var windowLights = lights.FindAll(l => l.type == LightType.Spot && l.name.StartsWith("WindowLight_"));
                Assert.IsTrue(windowLights.TrueForAll(l => l.shadows != LightShadows.None), "Window lights are the shadow casters.");
                Assert.IsTrue(windowLights.TrueForAll(l => l.GetUniversalAdditionalLightData().additionalLightsShadowResolutionTier == UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow),
                    "window lights use the low shadow tier");
                Assert.LessOrEqual(windowLights.Count * 256 * 256, 2048 * 2048, "all window-light shadows fit the atlas in one frame");
            }
            finally
            {
                EditorSceneManager.OpenScene($"{Vent.Editor.Paths.Scenes}/{SceneNames.Boot}.unity", OpenSceneMode.Single);
            }
        }

        [Test]
        public void UrpAssetKeepsGpuResidentDrawerOff()
        {
            // With the drawer on, the macOS player rendered nothing but the camera clear colour: the
            // instancing variants it needs were stripped from the build while the editor (which keeps
            // every variant) looked fine. Keep it off until that stripping path is fixed and verified in
            // a player, not just the editor.
            var asset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            Assert.IsNotNull(asset, "Default render pipeline should be the URP asset.");
            Assert.AreEqual(GPUResidentDrawerMode.Disabled, asset.gpuResidentDrawerMode);
            Assert.AreEqual(ColorGradingMode.HighDynamicRange, asset.colorGradingMode);
            // Window lights must stay shadowed wherever the player stands: a light crossing the shadow
            // distance flips between shadowed and leaking through walls, which reads as blinking.
            Assert.GreaterOrEqual(asset.shadowDistance, 55f, "shadow distance must cover the whole building");
        }

        [Test]
        public void BuildingHasWindowsWithGlassAndLightOutsideEachOne()
        {
            Scene scene = EditorSceneManager.OpenScene(Vent.Editor.Paths.BuildingScene, OpenSceneMode.Single);
            try
            {
                int panes = 0, windowLights = 0;
                Light sun = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name.StartsWith("Glass") && t.GetComponent<Collider>() != null && t.gameObject.layer == LayerMask.NameToLayer("Environment")) panes++;
                    }

                    foreach (Light l in root.GetComponentsInChildren<Light>(true))
                    {
                        if (l.type == LightType.Spot && l.name.StartsWith("WindowLight_")) windowLights++;
                        if (l.type == LightType.Directional) sun = l;
                    }
                }

                Assert.GreaterOrEqual(panes, 20, "every outer wall segment carries glazed, collidable windows");
                Assert.AreEqual(panes, windowLights, "one spot light outside each window");
                Assert.IsNotNull(sun, "an exterior sun drives the skybox");
                Assert.AreEqual(LightShadows.None, sun.shadows, "the sun must not cast into the building; window lights do that");
                // URP reads rendering layers from the additional light data; Light.renderingLayerMask alone is ignored.
                Assert.AreEqual(1u << 1, (uint)sun.GetUniversalAdditionalLightData().renderingLayers, "the sun lights the exterior rendering layer only");
                Assert.IsNotNull(RenderSettings.skybox, "dusk skybox visible through the glass");
            }
            finally
            {
                EditorSceneManager.OpenScene($"{Vent.Editor.Paths.Scenes}/{SceneNames.Boot}.unity", OpenSceneMode.Single);
            }
        }
        [Test]
        public void FarBuildingsStayOutsideTheBuilding()
        {
            Scene scene = EditorSceneManager.OpenScene(Vent.Editor.Paths.BuildingScene, OpenSceneMode.Single);
            try
            {
                // The building's footprint is the union of its floor slabs, plus the pavement apron.
                var footprint = new Bounds();
                bool any = false;
                var blocks = new List<Renderer>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r.name == "Floor")
                        {
                            if (!any) { footprint = r.bounds; any = true; } else footprint.Encapsulate(r.bounds);
                        }
                        else if (r.name.StartsWith("Building") && r.transform.parent != null && r.transform.parent.name == "Exterior")
                        {
                            blocks.Add(r);
                        }
                    }
                }

                Assert.IsTrue(any, "floor slabs found");
                Assert.GreaterOrEqual(blocks.Count, 10, "a skyline outside the windows");
                footprint.Expand(new Vector3(8f, 100f, 8f)); // the apron, and any height
                foreach (Renderer block in blocks)
                {
                    Assert.IsFalse(footprint.Intersects(block.bounds), $"{block.name} stands inside the building or on its pavement");
                }
            }
            finally
            {
                EditorSceneManager.OpenScene($"{Vent.Editor.Paths.Scenes}/{SceneNames.Boot}.unity", OpenSceneMode.Single);
            }
        }
    }
}
