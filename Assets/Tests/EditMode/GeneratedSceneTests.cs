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
using Vent.Gameplay.World;

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
                        // The base grade is the lowest-priority global volume; the outdoor overlay sits above it.
                        if (v.isGlobal && (global == null || v.priority < global.priority)) global = v;
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
            Assert.GreaterOrEqual(asset.shadowDistance, 85f, "shadow distance must cover the whole building and reach across a city block");
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
                // The sun casts real shadows (there is a street to stand in) but still lights only the
                // exterior rendering layer; every layer casts, so the roof keeps sunlight out of the rooms.
                Assert.AreEqual(LightShadows.Soft, sun.shadows, "the sun shadows the district; window lights do the interiors");
                // URP reads rendering layers from the additional light data; Light.renderingLayerMask alone is ignored.
                UniversalAdditionalLightData sunData = sun.GetUniversalAdditionalLightData();
                Assert.AreEqual(1u << 1, (uint)sunData.renderingLayers, "the sun lights the exterior rendering layer only");
                Assert.IsTrue(sunData.customShadowLayers, "shadow layers are decoupled from light layers");
                Assert.AreEqual(uint.MaxValue, (uint)sunData.shadowRenderingLayers, "everything casts the sun's shadow, or it leaks through ceilings onto sunlit objects");
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

        [Test]
        public void BuildingHasOfficeItemsToFindAndNoneOfThemIsStatic()
        {
            Scene scene = EditorSceneManager.OpenScene(Vent.Editor.Paths.BuildingScene, OpenSceneMode.Single);
            try
            {
                OfficeItemDirector director = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    director ??= root.GetComponentInChildren<OfficeItemDirector>(includeInactive: true);
                }

                Assert.IsNotNull(director, "the building has an office item director");
                Assert.GreaterOrEqual(director.Maps.Count, 6, "enough map spots to move between runs");
                Assert.GreaterOrEqual(director.Mirrors.Count, 6, "enough mirror spots");
                Assert.IsNotNull(director.MapTexture, "the floor plan was drawn at regen");
                Assert.IsTrue(director.MapTexture.width >= 512 && director.MapTexture.height >= 256);
                Assert.Greater(director.MapWorldRect.width, director.MapWorldRect.height * 0.5f, "the map covers the building's footprint");

                var all = new List<OfficeItemPickup>();
                all.AddRange(director.Maps);
                all.AddRange(director.Mirrors);
                foreach (OfficeItemPickup item in all)
                {
                    Assert.IsFalse(item.gameObject.activeSelf, $"{item.name} is hidden in the saved scene; BeginRun shows one of each");
                    Assert.AreEqual((StaticEditorFlags)0, GameObjectUtility.GetStaticEditorFlags(item.gameObject), $"{item.name} must stay non-static");
                    Assert.IsNotNull(item.GetComponentInChildren<Collider>(true), $"{item.name} needs a collider for the interactor's ray");
                    Assert.AreEqual(Layers.EnvironmentIndex, item.gameObject.layer, $"{item.name} must be on Environment: that is what Layers.InteractMask looks at");
                }

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Vent.Editor.Paths.Textures}/T_BuildingMap.png");
                Assert.IsNotNull(texture, "the floor plan is a texture asset");
                Assert.AreSame(texture, director.MapTexture);
            }
            finally
            {
                EditorSceneManager.OpenScene($"{Vent.Editor.Paths.Scenes}/{SceneNames.Boot}.unity", OpenSceneMode.Single);
            }
        }

        [Test]
        public void BuildingHasAKeyHuntAndNoneOfItIsStatic()
        {
            string path = Vent.Editor.Paths.BuildingScene;
            Assert.IsTrue(System.IO.File.Exists(path), $"Building scene missing at {path}; run Vent/Rebuild Everything.");

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            try
            {
                var directors = new List<KeyHuntDirector>();
                var notes = new List<QuestNote>();
                var drawers = new List<DeskDrawer>();
                var panels = new List<PatchPanel>();
                var cables = new List<PatchCablePickup>();
                bool serverRoom = false;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    directors.AddRange(root.GetComponentsInChildren<KeyHuntDirector>(includeInactive: true));
                    notes.AddRange(root.GetComponentsInChildren<QuestNote>(includeInactive: true));
                    drawers.AddRange(root.GetComponentsInChildren<DeskDrawer>(includeInactive: true));
                    panels.AddRange(root.GetComponentsInChildren<PatchPanel>(includeInactive: true));
                    cables.AddRange(root.GetComponentsInChildren<PatchCablePickup>(includeInactive: true));
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(includeInactive: true))
                    {
                        serverRoom |= t.name.StartsWith("Room_") && t.name.EndsWith("ServerRoom");
                    }
                }

                Assert.AreEqual(1, directors.Count, "one key hunt director");
                Assert.AreEqual(1, notes.Count, "exactly one whiteboard carries the hint");
                Assert.IsTrue(serverRoom, "the room plan guarantees somewhere to patch the servers");
                Assert.GreaterOrEqual(drawers.Count, 8, "enough desks that finding the right one is a search");
                Assert.GreaterOrEqual(panels.Count, 4, "a patch panel on every rack");
                Assert.GreaterOrEqual(cables.Count, 6, "more candidate cable spots than a run will use");

                // The regression guard that matters. A BatchingStatic renderer is welded into a
                // combined mesh and does not move, so a drawer that picked up static flags would
                // still open in code and never budge on screen; anything marked ContributeGI here
                // would also be baked into a lightmap it then slides out of.
                var movers = new List<Component>();
                movers.AddRange(drawers);
                movers.AddRange(panels);
                movers.AddRange(cables);
                foreach (Component mover in movers)
                {
                    Assert.AreEqual((StaticEditorFlags)0, GameObjectUtility.GetStaticEditorFlags(mover.gameObject),
                        $"{mover.GetType().Name} on {mover.name} must stay non-static; build it after Generate() returns");
                    Assert.IsNotNull(mover.GetComponentInChildren<Collider>(true),
                        $"{mover.GetType().Name} on {mover.name} needs a collider or the interactor's ray cannot find it");
                    Assert.AreEqual(Layers.EnvironmentIndex, mover.gameObject.layer,
                        $"{mover.name} must be on Environment: that is what Layers.InteractMask looks at");
                }

                // Issue #5: an open drawer is a box, not a floating front; it opens toward the chair,
                // the side the monitor faces; the key lies on its bottom and only exists once found.
                foreach (DeskDrawer drawer in drawers)
                {
                    Transform leaf = drawer.transform;
                    foreach (string part in new[] { "Front", "Bottom", "SideL", "SideR", "Back", "Pad", "Key" })
                    {
                        Assert.IsNotNull(leaf.Find(part), $"{drawer.name} on {leaf.parent.parent.name} has a {part}");
                    }

                    Transform desk = leaf.parent.parent; // Drawer → DrawerAnchor → desk
                    Transform screen = desk.Find("Screen"), monitor = desk.Find("Monitor");
                    Assert.IsNotNull(screen);
                    Assert.IsNotNull(monitor);
                    Assert.Less(desk.InverseTransformDirection(leaf.forward).z, -0.9f, $"{desk.name}: the drawer slides out toward the chair (-Z)");
                    Assert.Less(screen.localPosition.z, monitor.localPosition.z, $"{desk.name}: the screen is on the chair side of the monitor");
                    Transform key = leaf.Find("Key"), bottom = leaf.Find("Bottom");
                    Assert.Greater(key.localPosition.y, bottom.localPosition.y, "the key lies on the drawer bottom, not through it");
                    Assert.IsFalse(key.gameObject.activeSelf, "the key does not exist until the hunt puts it there");
                }
            }
            finally
            {
                EditorSceneManager.OpenScene($"{Vent.Editor.Paths.Scenes}/{SceneNames.Boot}.unity", OpenSceneMode.Single);
            }
        }
    }
}
