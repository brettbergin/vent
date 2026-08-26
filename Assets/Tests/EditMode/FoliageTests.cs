using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vent.Core.Utility;
using Vent.Editor;

namespace Vent.Tests.EditMode
{
    /// <summary>
    /// Guards the generated foliage: the shader compiles and the atlas imports as cutout art, every
    /// leaf renderer is probe-lit Environment (never lightmapped, never on the default layer where
    /// bullets ignore it), and trees stand on colliders so zombies path around them.
    /// </summary>
    public sealed class FoliageTests
    {
        private static Scene Open() => EditorSceneManager.OpenScene(Paths.BuildingScene, OpenSceneMode.Single);
        private static void Close() => EditorSceneManager.OpenScene(Paths.BootScene, OpenSceneMode.Single);

        [Test]
        public void FoliageShaderCompilesAndTheAtlasIsCutoutArt()
        {
            Shader shader = Shader.Find("Vent/Foliage");
            Assert.IsNotNull(shader, "Vent/Foliage shader missing");
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "Vent/Foliage has compile errors");
            foreach (string name in new[] { "M_Foliage", "M_FoliageCanopy", "M_FoliageIndoor" })
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>($"{Paths.Materials}/{name}.mat");
                Assert.IsNotNull(material, $"{name} missing; run Vent/Rebuild Everything");
                Assert.AreEqual(shader, material.shader, $"{name} must use Vent/Foliage");
                Assert.IsNotNull(material.GetTexture("_BaseMap"), $"{name} has no leaf atlas");
                Assert.IsNotNull(material.GetTexture("_BumpMap"), $"{name} has no normal map");
            }

            // Ground cover must never fade at grazing angles: leaf litter lies flat, so a player looking
            // along the pavement sees it edge-on and a fade would erase it out from under them.
            var canopy = AssetDatabase.LoadAssetAtPath<Material>($"{Paths.Materials}/M_FoliageCanopy.mat");
            var ground = AssetDatabase.LoadAssetAtPath<Material>($"{Paths.Materials}/M_Foliage.mat");
            var indoor = AssetDatabase.LoadAssetAtPath<Material>($"{Paths.Materials}/M_FoliageIndoor.mat");
            Assert.Greater(canopy.GetFloat("_GrazingFade"), 0f, "crowns fade edge-on cards or they read as blades");
            Assert.AreEqual(0f, ground.GetFloat("_GrazingFade"), "flat ground cover must not fade");
            Assert.AreEqual(0f, indoor.GetFloat("_GrazingFade"), "a single big indoor leaf must read from any angle");

            var importer = (TextureImporter)AssetImporter.GetAtPath($"{Paths.Textures}/T_Foliage.png");
            Assert.IsNotNull(importer, "leaf atlas not generated");
            Assert.IsTrue(importer.alphaIsTransparency, "the atlas alpha is the cutout");
            Assert.IsTrue(importer.mipMapsPreserveCoverage, "mips must keep the alpha-tested coverage or distant canopies thin to twigs");
            Assert.IsTrue(importer.mipmapEnabled);
        }

        [Test]
        public void EveryLeafRendererIsProbeLitEnvironmentUsingTheFoliageShader()
        {
            Scene scene = Open();
            try
            {
                Shader shader = Shader.Find("Vent/Foliage");
                int environment = LayerMask.NameToLayer(Layers.Environment);
                int indoor = 0, ivy = 0, lawn = 0, streetTrees = 0, total = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r.name != FoliageLibrary.RendererName)
                        {
                            continue;
                        }

                        total++;
                        Assert.AreEqual(shader, r.sharedMaterial.shader, $"{Path(r.transform)} is not a Vent/Foliage renderer");
                        Assert.AreEqual(environment, r.gameObject.layer, $"{Path(r.transform)} must be Environment so bullets clip leaves");
                        StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                        Assert.IsFalse((flags & StaticEditorFlags.ContributeGI) != 0, $"{Path(r.transform)} must not be lightmapped (probe-lit)");
                        Assert.IsTrue((flags & StaticEditorFlags.BatchingStatic) != 0, $"{Path(r.transform)} should static-batch");
                        // Props/Room_x_y_Type/PottedPlant/Plant/Foliage: walk up for the room; the rest sit one or two levels up.
                        Transform t = r.transform;
                        for (Transform p = t.parent; p != null; p = p.parent)
                        {
                            if (p.name.StartsWith("Room_")) { indoor++; break; }
                        }

                        if (t.parent != null && t.parent.name == "Ivy") ivy++;
                        if (t.parent != null && t.parent.name == "Lawn") lawn++;
                        if (t.parent != null && t.parent.parent != null && t.parent.parent.name == "StreetTree") streetTrees++;
                    }
                }

                Assert.GreaterOrEqual(total, 200, "a district's worth of foliage");
                Assert.GreaterOrEqual(indoor, 12, "plants in the offices");
                Assert.GreaterOrEqual(ivy, 6, "ivy on the walls");
                Assert.GreaterOrEqual(lawn, 4, "a lawn in the park");
                Assert.GreaterOrEqual(streetTrees, 30, "street trees along the avenues");
            }
            finally
            {
                Close();
            }
        }

        [Test]
        public void TreesAndHedgesAreSolidEnvironment()
        {
            Scene scene = Open();
            try
            {
                int environment = LayerMask.NameToLayer(Layers.Environment);
                int trees = 0, hedges = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name.StartsWith("Tree_"))
                        {
                            trees++;
                            var capsule = t.GetComponentInChildren<CapsuleCollider>();
                            Assert.IsNotNull(capsule, $"{Path(t)} has no trunk collider");
                            Assert.AreEqual(environment, capsule.gameObject.layer, $"{Path(t)} trunk must be Environment");
                        }
                        else if (t.name == "Hedge" && t.GetComponentInChildren<MeshRenderer>() != null && t.GetComponentInParent<Transform>() != null && (t.parent == null || t.parent.name != "Hedge"))
                        {
                            // StreetPropLibrary wraps the library's hedge (which carries the collider) in a root of the same name.
                            hedges++;
                            var box = t.GetComponentInChildren<BoxCollider>();
                            Assert.IsNotNull(box, $"{Path(t)} is cover: it needs a collider");
                            Assert.AreEqual(environment, box.gameObject.layer);
                        }
                    }
                }

                Assert.GreaterOrEqual(trees, 40, "trees in the district");
                Assert.GreaterOrEqual(hedges, 10, "hedges in the district");
            }
            finally
            {
                Close();
            }
        }

        [Test]
        public void PruningACrownMovesWholeCardsInsteadOfShearingThem()
        {
            // A street crown is pruned to a clear height. Clamping loose vertices to that plane flattened
            // whichever card straddled it and stretched its atlas cell into a pale blade across the canopy.
            var mb = new FoliageLibrary.MeshBuilder();
            mb.Card(Vector3.zero, Vector3.up, Vector3.right, 1f, 1f, FoliageTextureFactory.Cell.Cluster, 0f,
                Color.white, Color.white, 1, Vector3.forward, 0f);

            var before = new Mesh();
            mb.Fill(before);
            Vector3[] rest = before.vertices;

            mb.ClampMinY(0.5f);
            var after = new Mesh();
            mb.Fill(after);
            Vector3[] pruned = after.vertices;

            Assert.AreEqual(rest.Length, pruned.Length, "pruning must not add or drop vertices");
            foreach (Vector3 v in pruned)
            {
                Assert.GreaterOrEqual(v.y, 0.5f - 1e-4f, "nothing may hang below the pruned line");
            }

            // The card kept its shape: every vertex moved by the same lift.
            Vector3 lift = pruned[0] - rest[0];
            Assert.AreEqual(0.5f, lift.y, 1e-4f, "the card should rise just enough to clear the line");
            for (int i = 1; i < rest.Length; i++)
            {
                Assert.That((pruned[i] - rest[i] - lift).magnitude, Is.LessThan(1e-4f),
                    $"vertex {i} moved on its own: the card was sheared, not lifted");
            }

            Object.DestroyImmediate(before);
            Object.DestroyImmediate(after);
        }

        private static string Path(Transform t)
        {
            var parts = new List<string>();
            for (Transform p = t; p != null; p = p.parent) parts.Insert(0, p.name);
            return string.Join("/", parts);
        }
    }
}
