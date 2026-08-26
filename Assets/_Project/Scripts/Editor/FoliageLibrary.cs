using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Vent.Core.Utility;
using Cell = Vent.Editor.FoliageTextureFactory.Cell;

namespace Vent.Editor
{
    /// <summary>
    /// Builds every plant in the game as a mesh of curved leaf cards cut from the foliage atlas:
    /// office plants (monstera, fiddle-leaf fig, parlour palm, fern, snake plant, trailing pothos),
    /// trees (broadleaf, birch, conifer, and crown-lifted street trees), shrubs, clipped hedges, lawn
    /// tiles, weeds, leaf litter and ivy for walls. Each card carries the wind weights and baked
    /// occlusion the <c>Vent/Foliage</c> shader reads from vertex colour, and its shading normal is
    /// bent toward the plant's outside so a canopy of flat cards still shades like a volume.
    ///
    /// Meshes are deterministic per (kind, variant) and saved under <see cref="Paths.Meshes"/>, so
    /// a hundred street trees share six meshes and static batching folds them together. Foliage
    /// renderers are named <see cref="RendererName"/>: the generators keep them out of the lightmap
    /// (they are probe-lit) and on the exterior rendering layer where the sun reaches them.
    /// </summary>
    public static class FoliageLibrary
    {
        /// <summary>Name of every leaf-card renderer; the static-flag passes key off it.</summary>
        public const string RendererName = "Foliage";

        public enum Plant { Monstera, FiddleFig, Palm, Fern, SnakePlant }

        public enum TreeKind { Broadleaf, Birch, Conifer, Street }

        private static readonly Dictionary<string, Mesh> Cache = new();

        // ------------------------------------------------------------------ indoor

        /// <summary>
        /// A plant in a pot: the pot is the collider (it is the part you bump into), the soil a mulch
        /// disc, and the plant grows from the soil surface. <paramref name="scale"/> scales the plant,
        /// not the pot. Origin at the floor under the pot.
        /// </summary>
        public static GameObject PottedPlant(Transform parent, string name, GameAssets a, System.Random rng, Plant plant, float scale, float potRadius, float potHeight, Material potMaterial, int variant)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            Pot(root.transform, a, potRadius, potHeight, potMaterial);
            string key = $"Plant_{plant}_{Quantize(scale)}_{variant}";
            Mesh mesh = GetMesh(key, mb =>
            {
                var r = new System.Random(key.GetHashCode());
                switch (plant)
                {
                    case Plant.Monstera: Monstera(mb, r, scale); break;
                    case Plant.FiddleFig: FiddleFig(mb, r, scale); break;
                    case Plant.Palm: Palm(mb, r, scale); break;
                    case Plant.Fern: Fern(mb, r, scale); break;
                    case Plant.SnakePlant: SnakePlant(mb, r, scale); break;
                }
            });
            Foliage(root.transform, mesh, a.FoliageIndoor, new Vector3(0f, potHeight - 0.025f, 0f), rng.Next(360));
            return root;
        }

        /// <summary>
        /// A pothos in a small pot with vines that spill over the front edge (+Z) and hang
        /// <paramref name="drop"/> metres: sits on top of shelves and cabinets. Origin at the pot base.
        /// <paramref name="clearance"/> is how far in +Z the vines must reach before they may fall — the
        /// distance from the pot to the front face of the cabinet, or they hang down inside it.
        /// </summary>
        public static GameObject Pothos(Transform parent, GameAssets a, System.Random rng, float drop, float clearance, int variant)
        {
            var root = new GameObject("Pothos");
            root.transform.SetParent(parent, false);
            const float potR = 0.09f, potH = 0.12f;
            Pot(root.transform, a, potR, potH, rng.NextDouble() < 0.5 ? a.Ceramic : a.Terracotta, collider: false);
            string key = $"Plant_Pothos_{Quantize(drop)}_{Quantize(clearance)}_{variant}";
            Mesh mesh = GetMesh(key, mb => PothosVines(mb, new System.Random(key.GetHashCode()), drop, potR, clearance));
            Foliage(root.transform, mesh, a.FoliageIndoor, new Vector3(0f, potH - 0.02f, 0f), 0f);
            return root;
        }

        // ------------------------------------------------------------------ outdoor

        /// <summary>A tree: a bark trunk with a capsule collider and a canopy of leaf cards. <paramref name="crownRadius"/> is the canopy's reach.</summary>
        public static GameObject Tree(Transform parent, GameAssets a, System.Random rng, TreeKind kind, int variant, out float crownRadius)
        {
            var root = new GameObject($"Tree_{kind}");
            root.transform.SetParent(parent, false);
            string key = $"Tree_{kind}_{variant}";
            var r = new System.Random(key.GetHashCode());
            TreeSpec spec = TreeSpec.For(kind, r);
            crownRadius = spec.CrownRadius;

            Mesh trunk = GetMesh(key + "_Trunk", mb => Trunk(mb, r, spec));
            var trunkGo = new GameObject("Trunk");
            trunkGo.transform.SetParent(root.transform, false);
            trunkGo.AddComponent<MeshFilter>().sharedMesh = trunk;
            var trunkRenderer = trunkGo.AddComponent<MeshRenderer>();
            trunkRenderer.sharedMaterial = kind == TreeKind.Birch ? a.Birch : a.Bark;
            trunkRenderer.shadowCastingMode = ShadowCastingMode.On;
            var capsule = trunkGo.AddComponent<CapsuleCollider>();
            capsule.radius = spec.TrunkRadius * 1.15f;
            capsule.height = spec.TrunkHeight + 1f;
            capsule.center = new Vector3(0f, spec.TrunkHeight / 2f, 0f);

            Mesh canopy = GetMesh(key + "_Crown", mb =>
            {
                var cr = new System.Random(key.GetHashCode() + 7);
                Canopy(mb, cr, spec);
                if (kind == TreeKind.Street)
                {
                    mb.ClampMinY(4.45f); // a pruned crown: nothing hangs over the road below a car's roof line
                }
            });
            Foliage(root.transform, canopy, a.FoliageCanopy, Vector3.zero, rng.Next(360));
            return root;
        }

        /// <summary>A round shrub; <paramref name="flowering"/> gives it blossoms. Collidable: it is cover.</summary>
        public static GameObject Shrub(Transform parent, GameAssets a, System.Random rng, float radius, bool flowering, int variant)
        {
            var root = new GameObject(flowering ? "FloweringShrub" : "Shrub");
            root.transform.SetParent(parent, false);
            string key = $"Shrub_{(flowering ? "Bloom" : "Leaf")}_{Quantize(radius)}_{variant}";
            Mesh mesh = GetMesh(key, mb => ShrubCards(mb, new System.Random(key.GetHashCode()), radius, flowering ? Cell.Bloom : Cell.Cluster));
            Foliage(root.transform, mesh, a.FoliageCanopy, Vector3.zero, rng.Next(360));
            var collider = root.AddComponent<CapsuleCollider>();
            collider.radius = radius * 0.75f;
            collider.height = radius * 1.7f;
            collider.center = new Vector3(0f, radius * 0.85f, 0f);
            return root;
        }

        /// <summary>A clipped box hedge, <paramref name="length"/> along X. Solid: a box collider.</summary>
        public static GameObject Hedge(Transform parent, GameAssets a, System.Random rng, float length, float height, float depth, int variant)
        {
            var root = new GameObject("Hedge");
            root.transform.SetParent(parent, false);
            string key = $"Hedge_{Quantize(length)}x{Quantize(height)}x{Quantize(depth)}_{variant}";
            Mesh mesh = GetMesh(key, mb => HedgeCards(mb, new System.Random(key.GetHashCode()), length, height, depth));
            Foliage(root.transform, mesh, a.FoliageCanopy, Vector3.zero, 0f);
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(length, height, depth);
            box.center = new Vector3(0f, height / 2f, 0f);
            return root;
        }

        /// <summary>
        /// Grass tufts over <paramref name="area"/> (local XZ of <paramref name="parent"/>), skipping wherever
        /// <paramref name="keepOut"/> says (paths, beds), in 5 m chunks so the far side of a lawn culls. No
        /// collider: you walk through grass. <paramref name="key"/> names the chunk meshes, so use the lot's name.
        /// </summary>
        public static GameObject Lawn(Transform parent, GameAssets a, string key, Rect area, Func<Vector2, bool> keepOut, float density)
        {
            var root = new GameObject("Lawn");
            root.transform.SetParent(parent, false);
            const float chunk = 5f;
            int nx = Mathf.Max(1, Mathf.CeilToInt(area.width / chunk)), nz = Mathf.Max(1, Mathf.CeilToInt(area.height / chunk));
            float spacing = 0.5f / Mathf.Max(0.2f, density);
            for (int cz = 0; cz < nz; cz++)
            {
                for (int cx = 0; cx < nx; cx++)
                {
                    var cell = new Rect(area.xMin + cx * chunk, area.yMin + cz * chunk, Mathf.Min(chunk, area.xMax - (area.xMin + cx * chunk)), Mathf.Min(chunk, area.yMax - (area.yMin + cz * chunk)));
                    var centre = new Vector3(cell.center.x, 0f, cell.center.y);

                    // A chunk with nothing carved out of it is just grass, and grass is grass: it can share
                    // one of a few interior meshes instead of baking its own ~330 KB asset. Only chunks a
                    // path or bed actually cuts into need a bespoke one. Lawns dominated the mesh folder
                    // before this. The probe grid is independent of the tuft RNG and finer than any path.
                    bool carved = cell.width < chunk - 0.01f || cell.height < chunk - 0.01f;
                    if (!carved && keepOut != null)
                    {
                        for (int py = 0; py <= 12 && !carved; py++)
                        {
                            for (int px = 0; px <= 12 && !carved; px++)
                            {
                                carved = keepOut(new Vector2(cell.xMin + px / 12f * cell.width, cell.yMin + py / 12f * cell.height));
                            }
                        }
                    }

                    string chunkKey = carved
                        ? $"{key}_{cx}_{cz}"
                        : $"Lawn_Fill_{Quantize(spacing)}_{Mathf.Abs((cx * 73 + cz * 31) % 6)}";
                    float yaw = carved ? 0f : Mathf.Abs((cx * 17 + cz * 43) % 4) * 90f;
                    Rect build = carved ? cell : new Rect(-chunk / 2f, -chunk / 2f, chunk, chunk);
                    Vector3 buildCentre = carved ? centre : Vector3.zero;
                    Mesh mesh = GetMesh(chunkKey, mb =>
                    {
                        var rng = new System.Random(chunkKey.GetHashCode());
                        int n = Mathf.Max(1, Mathf.RoundToInt(build.width / spacing)), m = Mathf.Max(1, Mathf.RoundToInt(build.height / spacing));
                        for (int iz = 0; iz < m; iz++)
                        {
                            for (int ix = 0; ix < n; ix++)
                            {
                                if (rng.NextDouble() < 0.1) continue; // worn patches
                                var p = new Vector2(build.xMin + (ix + Rand(rng, 0.15f, 0.85f)) / n * build.width, build.yMin + (iz + Rand(rng, 0.15f, 0.85f)) / m * build.height);
                                if (carved && keepOut != null && keepOut(p)) continue;
                                Tuft(mb, rng, new Vector3(p.x, 0f, p.y) - buildCentre, Rand(rng, 0.16f, 0.32f), Rand(rng, 0.3f, 0.5f), Cell.Grass);
                            }
                        }
                    });
                    if (mesh.vertexCount == 0)
                    {
                        continue;
                    }

                    Foliage(root.transform, mesh, a.Foliage, centre, yaw, castShadows: false);
                }
            }

            return root;
        }

        /// <summary>A clump of weeds within <paramref name="radius"/>: the grass that comes up through cracks.</summary>
        public static GameObject Weeds(Transform parent, GameAssets a, System.Random rng, float radius, int variant)
        {
            var root = new GameObject("Weeds");
            root.transform.SetParent(parent, false);
            string key = $"Weeds_{Quantize(radius)}_{variant}";
            Mesh mesh = GetMesh(key, mb => WeedCards(mb, new System.Random(key.GetHashCode()), radius));
            Foliage(root.transform, mesh, a.Foliage, Vector3.zero, rng.Next(360), castShadows: false);
            return root;
        }

        /// <summary>Fallen leaves lying on the ground within <paramref name="radius"/>.</summary>
        public static GameObject Litter(Transform parent, GameAssets a, System.Random rng, float radius, int variant)
        {
            var root = new GameObject("LeafLitter");
            root.transform.SetParent(parent, false);
            string key = $"Litter_{Quantize(radius)}_{variant}";
            Mesh mesh = GetMesh(key, mb => LitterCards(mb, new System.Random(key.GetHashCode()), radius));
            Foliage(root.transform, mesh, a.Foliage, Vector3.zero, rng.Next(360), castShadows: false);
            return root;
        }

        public const float IvyTileWidth = 2f;

        /// <summary>
        /// A 2 m wide patch of ivy climbing <paramref name="height"/> metres up a wall. Origin at the foot
        /// of the wall, +X along it, +Z out of it (the leaves sit a few centimetres proud of the face).
        /// The top and sides are ragged, so tiles overlap into one growth.
        /// </summary>
        public static GameObject IvyTile(Transform parent, GameAssets a, System.Random rng, float height, int variant)
        {
            var root = new GameObject("Ivy");
            root.transform.SetParent(parent, false);
            string key = $"Ivy_{Quantize(height)}_{variant}";
            Mesh mesh = GetMesh(key, mb => IvyCards(mb, new System.Random(key.GetHashCode()), IvyTileWidth, height));
            Foliage(root.transform, mesh, a.Foliage, Vector3.zero, 0f, castShadows: false);
            return root;
        }

        // ------------------------------------------------------------------ assembly helpers

        private static void Foliage(Transform parent, Mesh mesh, Material material, Vector3 localPosition, float yaw, bool castShadows = true)
        {
            var go = new GameObject(RendererName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            go.layer = Layers.EnvironmentIndex;
        }

        /// <summary>A tapered pot with a rolled rim, a mulch disc inside; the pot is the collider.</summary>
        private static void Pot(Transform parent, GameAssets a, float radius, float height, Material material, bool collider = true)
        {
            string key = $"Pot_{Quantize(radius)}x{Quantize(height)}";
            Mesh mesh = GetMesh(key, mb =>
            {
                mb.Lathe(new List<(float r, float y)>
                {
                    (0f, 0.005f), (radius * 0.72f, 0.005f), (radius * 0.74f, 0.02f), (radius * 0.97f, height - 0.06f),
                    (radius * 1.04f, height - 0.05f), (radius * 1.04f, height), (radius * 0.94f, height), (radius * 0.9f, height - 0.03f),
                }, sides: 24, metreUvs: false);
            });
            var go = new GameObject("Pot");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            go.layer = Layers.EnvironmentIndex;
            if (collider)
            {
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.radius = radius;
                capsule.height = height + radius;
                capsule.center = new Vector3(0f, height / 2f, 0f);
            }

            GameObject soil = PrefabFactory.Primitive(PrimitiveType.Cylinder, "Soil", parent, new Vector3(0f, height - 0.03f, 0f), new Vector3(radius * 1.84f, 0.006f, radius * 1.84f), a.Mulch, collider: false);
            soil.layer = Layers.EnvironmentIndex;
        }

        private static Mesh GetMesh(string key, Action<MeshBuilder> build)
        {
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
            {
                return cached;
            }

            string path = $"{Paths.Meshes}/{key}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool fresh = mesh == null;
            if (fresh)
            {
                mesh = new Mesh { name = key };
            }

            var mb = new MeshBuilder();
            build(mb);
            mb.Fill(mesh);
            if (fresh)
            {
                ProjectBootstrap.EnsureFolder(Paths.Meshes);
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }

            Cache[key] = mesh;
            return mesh;
        }

        private static float Quantize(float v) => Mathf.Round(v * 4f) / 4f;

        private static float Rand(System.Random rng, float min, float max) => (float)(min + rng.NextDouble() * (max - min));

        private static Vector3 Yaw(float degrees) => Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;

        /// <summary>Wind/occlusion vertex colour: r = height weight, g = phase, b = flutter, a = occlusion.</summary>
        private static Color Wind(float height, float phase, float flutter, float occlusion) => new(height, phase, flutter, occlusion);

        // ------------------------------------------------------------------ office plants (origin: soil surface)

        private static void Monstera(MeshBuilder mb, System.Random rng, float scale)
        {
            int stems = 5 + rng.Next(4);
            float tallest = 0.85f * scale;
            for (int i = 0; i < stems; i++)
            {
                float phase = (float)rng.NextDouble();
                Vector3 outward = Yaw(i * 360f / stems + Rand(rng, -25f, 25f));
                float reach = Rand(rng, 0.12f, 0.30f) * scale, height = Rand(rng, 0.40f, 0.85f) * scale;
                Vector3 p0 = new Vector3(Rand(rng, -0.03f, 0.03f), 0f, Rand(rng, -0.03f, 0.03f));
                Vector3 p2 = outward * reach + Vector3.up * height;
                Vector3 p1 = Vector3.up * (height * 0.6f) + outward * (reach * 0.1f);
                mb.BezierTube(p0, p1, p2, 0.014f * scale, 0.009f * scale, 5, Cell.Stem, Wind(0f, phase, 0.05f, 0.7f), Wind(height / tallest, phase, 0.15f, 0.95f));
                Vector3 axis = (outward + Vector3.up * Rand(rng, -0.15f, 0.35f)).normalized;
                Vector3 side = Quaternion.AngleAxis(Rand(rng, -15f, 15f), axis) * Vector3.Cross(Vector3.up, axis).normalized;
                float length = Rand(rng, 0.32f, 0.50f) * scale;
                mb.Card(p2, axis, side, length, length * 0.9f, Cell.Broad, Rand(rng, 15f, 35f), Wind(height / tallest * 0.7f, phase, 0.4f, 0.9f), Wind(height / tallest, phase, 1f, 1f), 3, Vector3.up, 0.35f);
            }
        }

        private static void FiddleFig(MeshBuilder mb, System.Random rng, float scale)
        {
            float height = Rand(rng, 1.15f, 1.6f) * scale;
            Vector3 lean = new Vector3(Rand(rng, -0.08f, 0.08f), 0f, Rand(rng, -0.08f, 0.08f)) * scale;
            Vector3 top = Vector3.up * height + lean;
            Vector3 mid = Vector3.up * (height * 0.5f) + lean * 0.3f;
            mb.BezierTube(Vector3.zero, mid, top, 0.036f * scale, 0.018f * scale, 6, Cell.Bark, Wind(0f, 0.5f, 0f, 0.7f), Wind(1f, 0.5f, 0.05f, 0.9f));

            void Leaf(Vector3 at, Vector3 direction, float h)
            {
                float phase = (float)rng.NextDouble();
                Vector3 axis = (direction + Vector3.up * Rand(rng, -0.35f, 0.25f)).normalized;
                Vector3 side = Quaternion.AngleAxis(Rand(rng, -20f, 20f), axis) * Vector3.Cross(Vector3.up, axis).normalized;
                float length = Rand(rng, 0.26f, 0.38f) * scale;
                mb.Card(at, axis, side, length, length * 0.72f, Cell.Fig, Rand(rng, 10f, 28f), Wind(h * 0.85f, phase, 0.3f, 0.85f), Wind(h, phase, 0.8f, 1f), 3, Vector3.up, 0.3f);
            }

            int branches = 5 + rng.Next(4);
            for (int i = 0; i < branches; i++)
            {
                float t = Rand(rng, 0.45f, 0.95f);
                Vector3 at = Vector3.Lerp(Vector3.Lerp(Vector3.zero, mid, t), Vector3.Lerp(mid, top, t), t);
                Vector3 outward = Yaw(i * 360f / branches + Rand(rng, -30f, 30f));
                Vector3 direction = (outward + Vector3.up * Rand(rng, 0.35f, 0.9f)).normalized;
                float length = Rand(rng, 0.22f, 0.42f) * scale;
                Vector3 end = at + direction * length;
                mb.BezierTube(at, at + direction * (length * 0.5f), end, 0.012f * scale, 0.006f * scale, 3, Cell.Bark, Wind(t, 0.5f, 0.05f, 0.8f), Wind(t + 0.1f, 0.5f, 0.1f, 0.95f));
                int leaves = 2 + rng.Next(2);
                for (int k = 0; k < leaves; k++)
                {
                    float u = 0.45f + k * 0.55f / leaves;
                    Leaf(at + direction * (length * u), Quaternion.AngleAxis(Rand(rng, -70f, 70f), Vector3.up) * outward, t + 0.1f * u);
                }

                Leaf(end, direction, t + 0.15f);
            }

            for (int k = 0; k < 4; k++)
            {
                Leaf(top - Vector3.up * (0.02f * k), Yaw(k * 90f + Rand(rng, -30f, 30f)), 1f);
            }
        }

        private static void Palm(MeshBuilder mb, System.Random rng, float scale)
        {
            int fronds = 10 + rng.Next(5);
            for (int i = 0; i < fronds; i++)
            {
                float phase = (float)rng.NextDouble();
                Vector3 outward = Yaw(i * 360f / fronds + Rand(rng, -20f, 20f));
                float elevation = Rand(rng, 35f, 80f);
                Vector3 direction = (outward * Mathf.Cos(elevation * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(elevation * Mathf.Deg2Rad)).normalized;
                Vector3 start = new Vector3(Rand(rng, -0.04f, 0.04f), 0f, Rand(rng, -0.04f, 0.04f));
                float stem = Rand(rng, 0.25f, 0.42f) * scale;
                Vector3 end = start + direction * stem;
                mb.BezierTube(start, Vector3.Lerp(start, end, 0.5f), end, 0.014f * scale, 0.009f * scale, 3, Cell.Stem, Wind(0f, phase, 0f, 0.6f), Wind(0.35f, phase, 0.1f, 0.85f));
                Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
                float length = Rand(rng, 0.85f, 1.2f) * scale;
                mb.Card(end, direction, side, length, length * 0.42f, Cell.Palm, Rand(rng, 55f, 85f), Wind(0.4f, phase, 0.5f, 0.75f), Wind(1f, phase, 1f, 1f), 5, Vector3.up, 0.25f);
            }
        }

        private static void Fern(MeshBuilder mb, System.Random rng, float scale)
        {
            int fronds = 12 + rng.Next(7);
            for (int i = 0; i < fronds; i++)
            {
                float phase = (float)rng.NextDouble();
                Vector3 outward = Yaw(i * 360f / fronds + Rand(rng, -15f, 15f));
                float elevation = 68f - 45f * (i % 3) / 2f + Rand(rng, -8f, 8f);
                Vector3 direction = (outward * Mathf.Cos(elevation * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(elevation * Mathf.Deg2Rad)).normalized;
                Vector3 start = new Vector3(Rand(rng, -0.04f, 0.04f), 0f, Rand(rng, -0.04f, 0.04f));
                Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
                float length = Rand(rng, 0.38f, 0.62f) * scale;
                mb.Card(start, direction, side, length, length * 0.6f, Cell.Frond, Rand(rng, 35f, 70f), Wind(0.1f, phase, 0.3f, 0.55f), Wind(1f, phase, 1f, 1f), 4, Vector3.up, 0.3f);
            }
        }

        private static void SnakePlant(MeshBuilder mb, System.Random rng, float scale)
        {
            int blades = 9 + rng.Next(6);
            for (int i = 0; i < blades; i++)
            {
                float phase = (float)rng.NextDouble();
                Vector3 outward = Yaw(i * 360f / blades + Rand(rng, -30f, 30f));
                Vector3 start = outward * Rand(rng, 0f, 0.07f) * scale;
                float tilt = Rand(rng, 2f, 14f);
                Vector3 direction = (Vector3.up * Mathf.Cos(tilt * Mathf.Deg2Rad) + outward * Mathf.Sin(tilt * Mathf.Deg2Rad)).normalized;
                Vector3 side = Quaternion.AngleAxis(Rand(rng, -25f, 25f), direction) * Vector3.Cross(Vector3.up, outward).normalized;
                float length = Rand(rng, 0.45f, 0.95f) * scale;
                mb.Card(start, direction, side, length, length * 0.3f, Cell.Sword, Rand(rng, 3f, 12f), Wind(0f, phase, 0.05f, 0.6f), Wind(1f, phase, 0.25f, 1f), 3, outward, 0.3f);
            }
        }

        private static void PothosVines(MeshBuilder mb, System.Random rng, float drop, float potRadius, float clearance)
        {
            int vines = 3 + rng.Next(3);
            for (int i = 0; i < vines; i++)
            {
                // Every vine goes over the front edge. Letting one spill "any way it likes" sent it backwards
                // through the shelf it sits on, so its leaves hung among the books.
                bool hanging = i < vines - 1;
                float yaw = Rand(rng, -52f, 52f);
                Vector3 outward = Yaw(yaw);
                Vector3 start = new Vector3(Rand(rng, -0.04f, 0.04f), 0f, Rand(rng, -0.04f, 0.04f));
                float fall = hanging ? drop * Rand(rng, 0.55f, 1f) : Rand(rng, 0.08f, 0.16f);
                // Reach far enough along +Z to be past the front face before the vine turns down.
                float reach = Mathf.Clamp(Mathf.Max(potRadius * 2.4f, (clearance + 0.06f) / Mathf.Max(outward.z, 0.4f)), 0f, 0.5f);
                Vector3 p1 = start + outward * (reach * 0.62f) + Vector3.up * 0.08f;
                Vector3 p2 = start + outward * (reach * 0.92f) - Vector3.up * (fall * 0.35f);
                Vector3 p3 = start + outward * (reach + Rand(rng, 0f, 0.08f)) - Vector3.up * fall;
                int segments = 4 + Mathf.RoundToInt(fall / 0.08f);
                Vector3 prev = start;
                float phase = (float)rng.NextDouble();
                for (int k = 1; k <= segments; k++)
                {
                    float t = (float)k / segments;
                    Vector3 p = Bezier3(start, p1, p2, p3, t);
                    Vector3 tangent = (p - prev).normalized;
                    mb.Tube(prev, p, 0.006f, 0.005f, 5, Cell.Stem, Wind(0.2f + 0.6f * (float)(k - 1) / segments, phase, 0.3f, 0.85f), Wind(0.2f + 0.6f * t, phase, 0.4f, 0.9f));
                    Vector3 perpendicular = Vector3.Cross(tangent, Vector3.up).normalized;
                    if (perpendicular.sqrMagnitude < 0.01f) perpendicular = Vector3.Cross(tangent, Vector3.right).normalized;
                    float sideSign = k % 2 == 0 ? 1f : -1f;
                    Vector3 axis = (perpendicular * sideSign * 0.7f + tangent * 0.4f - Vector3.up * 0.3f).normalized;
                    Vector3 side = Quaternion.AngleAxis(Rand(rng, -20f, 20f), axis) * Vector3.Cross(axis, tangent).normalized;
                    float length = Rand(rng, 0.08f, 0.14f);
                    float leafPhase = (float)rng.NextDouble();
                    mb.Card(p, axis, side, length, length * 0.9f, Cell.Heart, Rand(rng, 10f, 25f), Wind(0.25f + 0.6f * t, leafPhase, 0.6f, 0.85f), Wind(0.3f + 0.7f * t, leafPhase, 1f, 1f), 2, Vector3.up, 0.25f);
                    prev = p;
                }
            }

            // A few leaves standing up out of the pot itself.
            for (int i = 0; i < 5; i++)
            {
                Vector3 outward = Yaw(Rand(rng, 0f, 360f));
                Vector3 axis = (outward + Vector3.up * Rand(rng, 0.4f, 1.2f)).normalized;
                float length = Rand(rng, 0.09f, 0.13f);
                float phase = (float)rng.NextDouble();
                mb.Card(outward * 0.03f, axis, Vector3.Cross(Vector3.up, axis).normalized, length, length * 0.9f, Cell.Heart, 20f, Wind(0.1f, phase, 0.3f, 0.7f), Wind(0.3f, phase, 0.8f, 1f), 2, Vector3.up, 0.3f);
            }
        }

        // ------------------------------------------------------------------ trees

        private struct TreeSpec
        {
            public float TrunkHeight, TrunkRadius, CrownRadius, CrownHeight, CrownCentreY, CardMin, CardMax, Lean;
            public int Cards, Branches;
            public Cell Leaf;
            public bool Conifer;

            public static TreeSpec For(TreeKind kind, System.Random rng)
            {
                switch (kind)
                {
                    case TreeKind.Birch:
                    {
                        float r = Rand(rng, 1.5f, 2.1f);
                        float h = Rand(rng, 4.4f, 6f);
                        return new TreeSpec { TrunkHeight = h, TrunkRadius = Rand(rng, 0.11f, 0.15f), CrownRadius = r, CrownHeight = r * 1.35f, CrownCentreY = h + r * 0.7f, CardMin = 0.70f, CardMax = 1.00f, Cards = 95 + rng.Next(35), Branches = 4 + rng.Next(3), Leaf = Cell.Cluster, Lean = Rand(rng, 0.05f, 0.25f) };
                    }
                    case TreeKind.Conifer:
                    {
                        float h = Rand(rng, 6f, 9f);
                        return new TreeSpec { TrunkHeight = h * 0.92f, TrunkRadius = Rand(rng, 0.15f, 0.21f), CrownRadius = Rand(rng, 1.6f, 2.2f), CrownHeight = h, CrownCentreY = h / 2f, Conifer = true, Leaf = Cell.Needles, Lean = Rand(rng, 0f, 0.08f) };
                    }
                    case TreeKind.Street:
                    {
                        float r = Rand(rng, 2.0f, 2.4f);
                        return new TreeSpec { TrunkHeight = 5.0f, TrunkRadius = Rand(rng, 0.15f, 0.2f), CrownRadius = r, CrownHeight = r * 0.85f, CrownCentreY = 6.3f, CardMin = 0.84f, CardMax = 1.20f, Cards = 100 + rng.Next(35), Branches = 5, Leaf = Cell.Cluster, Lean = Rand(rng, 0f, 0.1f) };
                    }
                    default:
                    {
                        float r = Rand(rng, 2.1f, 2.9f);
                        float h = Rand(rng, 3.4f, 4.8f);
                        return new TreeSpec { TrunkHeight = h, TrunkRadius = Rand(rng, 0.17f, 0.25f), CrownRadius = r, CrownHeight = r * 0.9f, CrownCentreY = h + r * 0.5f, CardMin = 0.90f, CardMax = 1.35f, Cards = 110 + rng.Next(50), Branches = 4 + rng.Next(3), Leaf = Cell.Cluster, Lean = Rand(rng, 0.05f, 0.3f) };
                    }
                }
            }
        }

        private static void Trunk(MeshBuilder mb, System.Random rng, TreeSpec spec)
        {
            float r = spec.TrunkRadius, h = spec.TrunkHeight;
            float top = spec.Conifer ? h : h + spec.CrownHeight * 0.45f;
            mb.Lathe(new List<(float r, float y)>
            {
                (0f, -0.05f), (r * 1.5f, -0.05f), (r * 1.25f, 0.15f), (r * 1.02f, 0.6f), (r, h * 0.35f), (r * 0.82f, h * 0.7f), (r * 0.62f, h), (r * 0.35f, top), (0f, top + 0.05f),
            }, sides: 12, metreUvs: true, lean: new Vector2(spec.Lean, spec.Lean * 0.4f), noise: rng);
        }

        private static void Canopy(MeshBuilder mb, System.Random rng, TreeSpec spec)
        {
            if (spec.Conifer)
            {
                ConiferCanopy(mb, rng, spec);
                return;
            }

            Vector3 centre = new Vector3(0f, spec.CrownCentreY, 0f);
            float rx = spec.CrownRadius, ry = spec.CrownHeight;
            // Branches from the top of the trunk into the crown.
            for (int i = 0; i < spec.Branches; i++)
            {
                Vector3 outward = Yaw(i * 360f / spec.Branches + Rand(rng, -25f, 25f));
                float elevation = Rand(rng, 25f, 60f);
                Vector3 direction = (outward * Mathf.Cos(elevation * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(elevation * Mathf.Deg2Rad)).normalized;
                Vector3 start = new Vector3(0f, Rand(rng, spec.TrunkHeight * 0.8f, spec.TrunkHeight + 0.3f), 0f) + outward * (spec.TrunkRadius * 0.5f);
                float length = rx * Rand(rng, 0.6f, 0.95f);
                Vector3 end = start + direction * length;
                Color c0 = Wind(start.y / (centre.y + ry), 0.5f, 0f, 0.6f), c1 = Wind(end.y / (centre.y + ry), 0.5f, 0.05f, 0.8f);
                mb.BezierTube(start, start + direction * (length * 0.5f) + Vector3.up * 0.2f, end, spec.TrunkRadius * 0.4f, spec.TrunkRadius * 0.12f, 4, Cell.Bark, c0, c1);
            }

            float top = centre.y + ry;
            for (int i = 0; i < spec.Cards; i++)
            {
                // A point in the ellipsoid, biased to the shell so the silhouette is full and the middle is not wasted.
                Vector3 dir = new Vector3(Rand(rng, -1f, 1f), Rand(rng, -1f, 1f), Rand(rng, -1f, 1f)).normalized;
                float depth = Mathf.Pow((float)rng.NextDouble(), 0.4f); // 0 = centre, 1 = shell
                float length = Rand(rng, spec.CardMin, spec.CardMax);
                // Place the card's centre, not its corner: a card pinned to the shell hangs half its length
                // out past the silhouette, and one lone card out in the sky reads as a frond of some other
                // plant rather than as part of the crown.
                float reach = length * 0.5f;
                float shellX = Mathf.Max(rx * 0.35f, rx - reach), shellY = Mathf.Max(ry * 0.35f, ry - reach);
                Vector3 offset = new Vector3(dir.x * shellX, dir.y * shellY, dir.z * shellX) * depth;
                Vector3 at = centre + offset;
                Vector3 outward = (new Vector3(dir.x, dir.y * 0.6f + 0.25f, dir.z)).normalized;
                // Only a light tilt off the crown shell. At 0.45 a good share of cards ended up edge-on to
                // any viewer outside the tree, and a metre-wide card seen edge-on is a blade, not a leaf.
                Vector3 normal = Vector3.Slerp(outward, new Vector3(Rand(rng, -1f, 1f), Rand(rng, -1f, 1f), Rand(rng, -1f, 1f)).normalized, 0.22f).normalized;
                Vector3 axis = Vector3.Cross(normal, new Vector3(Rand(rng, -1f, 1f), Rand(rng, -1f, 1f), Rand(rng, -1f, 1f))).normalized;
                Vector3 side = Vector3.Cross(axis, normal).normalized;
                float phase = (float)rng.NextDouble();
                float occlusion = Mathf.Lerp(0.6f, 1f, depth) * Mathf.Lerp(0.88f, 1f, (at.y - (centre.y - ry)) / (2f * ry));
                Vector3 origin = at - axis * (length / 2f);
                mb.Card(origin, axis, side, length, length, spec.Leaf, Rand(rng, 15f, 35f),
                    Wind(origin.y / top, phase, 1f, occlusion), Wind((origin.y + length * 0.5f) / top, phase, 1f, occlusion), 2, outward, 0.65f);
            }
        }

        private static void ConiferCanopy(MeshBuilder mb, System.Random rng, TreeSpec spec)
        {
            float h = spec.CrownHeight, baseR = spec.CrownRadius;
            for (float y = 1.1f; y < h - 0.5f; y += 0.55f)
            {
                float ring = Mathf.Lerp(baseR, 0.15f, y / h) * Rand(rng, 0.9f, 1.1f);
                int cards = Mathf.Max(4, Mathf.CeilToInt(2f * Mathf.PI * ring / 0.55f));
                float start = Rand(rng, 0f, 360f);
                for (int i = 0; i < cards; i++)
                {
                    Vector3 outward = Yaw(start + i * 360f / cards + Rand(rng, -8f, 8f));
                    float elevation = Rand(rng, -28f, -12f);
                    Vector3 axis = (outward * Mathf.Cos(elevation * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(elevation * Mathf.Deg2Rad)).normalized;
                    Vector3 side = Vector3.Cross(Vector3.up, outward).normalized;
                    Vector3 origin = new Vector3(0f, y + Rand(rng, -0.1f, 0.1f), 0f) + outward * (spec.TrunkRadius * 0.6f);
                    float length = ring + Rand(rng, 0.3f, 0.5f);
                    float phase = (float)rng.NextDouble();
                    Vector3 shading = (outward + Vector3.up * 0.8f).normalized;
                    mb.Card(origin, axis, side, length, length * 0.8f, Cell.Needles, Rand(rng, 8f, 18f), Wind(y / h * 0.6f, phase, 0.3f, 0.55f), Wind(y / h, phase, 0.7f, 1f), 3, shading, 0.55f);
                }
            }

            // The leader: three cards straight up from the top.
            for (int i = 0; i < 3; i++)
            {
                Vector3 side = Yaw(i * 60f);
                float phase = (float)rng.NextDouble();
                mb.Card(new Vector3(0f, h - 1.1f, 0f), Vector3.up, side, 1.4f, 0.9f, Cell.Needles, 4f, Wind(0.85f, phase, 0.3f, 0.8f), Wind(1f, phase, 0.6f, 1f), 2, Vector3.up, 0.3f);
            }
        }

        // ------------------------------------------------------------------ shrubs, hedges, ground cover

        private static void ShrubCards(MeshBuilder mb, System.Random rng, float radius, Cell cell)
        {
            Vector3 centre = new Vector3(0f, radius * 0.85f, 0f);
            int cards = 16 + Mathf.RoundToInt(radius * 24f);
            float top = radius * 1.8f;
            for (int i = 0; i < cards; i++)
            {
                Vector3 dir = new Vector3(Rand(rng, -1f, 1f), Rand(rng, -0.6f, 1f), Rand(rng, -1f, 1f)).normalized;
                float depth = Mathf.Pow((float)rng.NextDouble(), 0.35f);
                Vector3 at = centre + new Vector3(dir.x * radius, dir.y * radius * 0.9f, dir.z * radius) * depth;
                at.y = Mathf.Max(at.y, 0.12f);
                Vector3 outward = (dir + Vector3.up * 0.3f).normalized;
                Vector3 normal = Vector3.Slerp(outward, new Vector3(Rand(rng, -1f, 1f), Rand(rng, -1f, 1f), Rand(rng, -1f, 1f)).normalized, 0.4f).normalized;
                Vector3 axis = Vector3.Cross(normal, new Vector3(Rand(rng, -1f, 1f), Rand(rng, -1f, 1f), Rand(rng, -1f, 1f))).normalized;
                Vector3 side = Vector3.Cross(axis, normal).normalized;
                float length = Rand(rng, 0.55f, 0.8f) * (radius / 0.7f);
                float phase = (float)rng.NextDouble();
                float occlusion = Mathf.Lerp(0.62f, 1f, depth);
                Vector3 origin = at - axis * (length / 2f);
                mb.Card(origin, axis, side, length, length, cell, Rand(rng, 10f, 30f), Wind(at.y / top * 0.5f, phase, 0.6f, occlusion), Wind(at.y / top * 0.6f, phase, 0.7f, occlusion), 2, outward, 0.6f);
            }
        }

        private static void HedgeCards(MeshBuilder mb, System.Random rng, float length, float height, float depth)
        {
            const float spacing = 0.28f;
            void Face(Vector3 origin, Vector3 right, Vector3 up, float w, float h, Vector3 normal, bool top)
            {
                int nx = Mathf.Max(1, Mathf.RoundToInt(w / spacing)), ny = Mathf.Max(1, Mathf.RoundToInt(h / spacing));
                for (int iy = 0; iy < ny; iy++)
                {
                    for (int ix = 0; ix < nx; ix++)
                    {
                        Vector3 at = origin + right * ((ix + 0.5f) / nx * w + Rand(rng, -0.06f, 0.06f)) + up * ((iy + 0.5f) / ny * h + Rand(rng, -0.06f, 0.06f));
                        Vector3 n = Vector3.Slerp(normal, new Vector3(Rand(rng, -1f, 1f), Rand(rng, -1f, 1f), Rand(rng, -1f, 1f)).normalized, 0.3f).normalized;
                        Vector3 axis = Vector3.Cross(n, new Vector3(Rand(rng, -1f, 1f), Rand(rng, -1f, 1f), Rand(rng, -1f, 1f))).normalized;
                        Vector3 side = Vector3.Cross(axis, n).normalized;
                        float size = Rand(rng, 0.5f, 0.65f);
                        float phase = (float)rng.NextDouble();
                        float hw = top ? 0.5f : at.y / height * 0.5f;
                        mb.Card(at - axis * (size / 2f) + normal * 0.04f, axis, side, size, size, Cell.Boxwood, Rand(rng, 5f, 20f), Wind(hw, phase, 0.4f, 0.85f), Wind(hw, phase, 0.5f, 0.9f), 2, normal, 0.7f);
                    }
                }
            }

            float hl = length / 2f, hd = depth / 2f;
            Face(new Vector3(-hl, 0f, hd), Vector3.right, Vector3.up, length, height, Vector3.forward, false);
            Face(new Vector3(hl, 0f, -hd), Vector3.left, Vector3.up, length, height, Vector3.back, false);
            Face(new Vector3(-hl, 0f, -hd), Vector3.forward, Vector3.up, depth, height, Vector3.left, false);
            Face(new Vector3(hl, 0f, hd), Vector3.back, Vector3.up, depth, height, Vector3.right, false);
            Face(new Vector3(-hl, height, -hd), Vector3.right, Vector3.forward, length, depth, Vector3.up, true);
        }

        private static void Tufts(MeshBuilder mb, System.Random rng, float tile, float density)
        {
            float spacing = 0.5f / Mathf.Max(0.2f, density);
            int n = Mathf.Max(1, Mathf.RoundToInt(tile / spacing));
            for (int iz = 0; iz < n; iz++)
            {
                for (int ix = 0; ix < n; ix++)
                {
                    if (rng.NextDouble() < 0.12) continue; // bare patches
                    Vector3 at = new Vector3((ix + Rand(rng, 0.15f, 0.85f)) / n * tile - tile / 2f, 0f, (iz + Rand(rng, 0.15f, 0.85f)) / n * tile - tile / 2f);
                    Tuft(mb, rng, at, Rand(rng, 0.22f, 0.42f), Rand(rng, 0.3f, 0.5f), Cell.Grass);
                }
            }
        }

        private static void Tuft(MeshBuilder mb, System.Random rng, Vector3 at, float height, float width, Cell cell)
        {
            float start = Rand(rng, 0f, 180f);
            float phase = (float)rng.NextDouble();
            for (int k = 0; k < 3; k++)
            {
                Vector3 side = Yaw(start + k * 60f + Rand(rng, -10f, 10f));
                Vector3 axis = (Vector3.up + Vector3.Cross(side, Vector3.up) * Rand(rng, -0.15f, 0.15f)).normalized;
                mb.Card(at, axis, side, height, width, cell, Rand(rng, 5f, 18f), Wind(0f, phase, 0.2f, 0.55f), Wind(1f, phase, 1f, 1f), 2, Vector3.up, 0.8f);
            }
        }

        private static void WeedCards(MeshBuilder mb, System.Random rng, float radius)
        {
            int clumps = 3 + Mathf.RoundToInt(radius * 4f);
            for (int i = 0; i < clumps; i++)
            {
                Vector3 at = Yaw(Rand(rng, 0f, 360f)) * (radius * Mathf.Sqrt((float)rng.NextDouble()));
                float size = Rand(rng, 0.3f, 0.5f);
                float phase = (float)rng.NextDouble();
                for (int k = 0; k < 2; k++)
                {
                    Vector3 side = Yaw(k * 90f + Rand(rng, -15f, 15f));
                    mb.Card(at, Vector3.up, side, size, size * 0.9f, Cell.Weeds, Rand(rng, 5f, 15f), Wind(0f, phase, 0.2f, 0.6f), Wind(0.6f, phase, 0.8f, 1f), 2, Vector3.up, 0.7f);
                }

                if (rng.NextDouble() < 0.6)
                {
                    Tuft(mb, rng, at + Yaw(Rand(rng, 0f, 360f)) * 0.2f, Rand(rng, 0.18f, 0.3f), Rand(rng, 0.25f, 0.35f), Cell.Grass);
                }
            }
        }

        private static void LitterCards(MeshBuilder mb, System.Random rng, float radius)
        {
            int cards = 3 + Mathf.RoundToInt(radius * 2f);
            for (int i = 0; i < cards; i++)
            {
                Vector3 at = Yaw(Rand(rng, 0f, 360f)) * (radius * Mathf.Sqrt((float)rng.NextDouble()));
                at.y = 0.012f + i * 0.003f;
                Vector3 axis = Yaw(Rand(rng, 0f, 360f));
                Vector3 side = Vector3.Cross(Vector3.up, axis);
                float size = Rand(rng, 0.7f, 1.0f);
                mb.Card(at - axis * (size / 2f), axis, side, size, size, Cell.Litter, 0f, Wind(0f, 0.5f, 0f, 1f), Wind(0f, 0.5f, 0f, 1f), 1, Vector3.up, 0f);
            }
        }

        private static void IvyCards(MeshBuilder mb, System.Random rng, float width, float height)
        {
            const float spacing = 0.3f;
            int nx = Mathf.RoundToInt(width / spacing), ny = Mathf.RoundToInt(height / spacing) + 1;
            var noise = new TextureFactory.Noise(rng.Next());
            for (int iy = 0; iy < ny; iy++)
            {
                for (int ix = 0; ix < nx; ix++)
                {
                    float x = (ix + Rand(rng, 0.2f, 0.8f)) / nx * width - width / 2f;
                    float y = (iy + Rand(rng, 0.1f, 0.9f)) / ny * height;
                    // Ragged top and thinning sides: overlapping tiles read as one continuous growth.
                    float crest = height * (0.6f + 0.4f * noise.Fbm((x / width + 0.5f), 0.3f, 3, 1, 2));
                    if (y > crest && rng.NextDouble() < 0.8) continue;
                    // Dense at the foot, open near the top: ivy carries less leaf the further it has climbed.
                    if (rng.NextDouble() < 0.45f * (y / Mathf.Max(height, 0.01f))) continue;
                    float edge = 1f - Mathf.Abs(x) / (width / 2f);
                    if (rng.NextDouble() > Mathf.Clamp01(edge * 3f + 0.2f)) continue;
                    Vector3 at = new Vector3(x, y, Rand(rng, 0.03f, 0.09f));
                    Vector3 normal = (Vector3.forward + new Vector3(Rand(rng, -0.4f, 0.4f), Rand(rng, -0.3f, 0.3f), 0f)).normalized;
                    Vector3 axis = (Vector3.down + Vector3.right * Rand(rng, -0.6f, 0.6f)).normalized;
                    axis = (axis - normal * Vector3.Dot(axis, normal)).normalized;
                    Vector3 side = Vector3.Cross(axis, normal).normalized;
                    float size = Rand(rng, 0.5f, 0.7f);
                    float phase = (float)rng.NextDouble();
                    mb.Card(at - axis * (size * 0.35f), axis, side, size, size, Cell.Ivy, Rand(rng, 5f, 15f), Wind(0.12f * y / height, phase, 0.5f, 0.85f), Wind(0.12f * y / height, phase, 0.6f, 0.9f), 2, Vector3.forward, 0.5f);
                }
            }

            // One or two runners climbing higher than the mass.
            int runners = 1 + rng.Next(2);
            for (int r = 0; r < runners; r++)
            {
                float x = Rand(rng, -width * 0.35f, width * 0.35f);
                float top = height * Rand(rng, 1.3f, 1.8f);
                for (float y = height * 0.8f; y < top; y += 0.22f)
                {
                    Vector3 at = new Vector3(x + Rand(rng, -0.08f, 0.08f), y, 0.05f);
                    Vector3 axis = (Vector3.down + Vector3.right * Rand(rng, -0.5f, 0.5f)).normalized;
                    Vector3 side = Vector3.Cross(axis, Vector3.forward).normalized;
                    float size = Rand(rng, 0.3f, 0.42f);
                    float phase = (float)rng.NextDouble();
                    mb.Card(at - axis * (size * 0.3f), axis, side, size, size, Cell.Ivy, 8f, Wind(0.15f, phase, 0.6f, 0.9f), Wind(0.15f, phase, 0.7f, 0.95f), 1, Vector3.forward, 0.5f);
                }
            }
        }

        private static Vector3 Bezier3(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        // ------------------------------------------------------------------ mesh builder

        /// <summary>Accumulates cards, tubes and lathes into one mesh with the atlas UVs and the foliage vertex colours.</summary>
        public sealed class MeshBuilder
        {
            private readonly List<Vector3> vertices = new();
            private readonly List<Vector3> normals = new();
            private readonly List<Vector4> tangents = new();
            private readonly List<Vector2> uvs = new();
            private readonly List<Color> colors = new();
            private readonly List<int> triangles = new();
            // The first vertex of each card, tube or lathe, so ClampMinY can move them whole.
            private readonly List<int> pieceStarts = new();

            public int VertexCount => vertices.Count;

            /// <summary>
            /// A leaf card: base at <paramref name="origin"/>, growing along <paramref name="axis"/> for
            /// <paramref name="length"/>, <paramref name="width"/> across <paramref name="side"/>, bending by
            /// <paramref name="droopDegrees"/> toward its underside over <paramref name="segments"/>. The shading
            /// normal is bent toward <paramref name="shadingNormal"/> by <paramref name="shadingBlend"/>.
            /// </summary>
            public void Card(Vector3 origin, Vector3 axis, Vector3 side, float length, float width, Cell cell, float droopDegrees, Color baseColor, Color tipColor, int segments, Vector3 shadingNormal, float shadingBlend)
            {
                axis.Normalize();
                side = (side - axis * Vector3.Dot(side, axis)).normalized;
                if (side.sqrMagnitude < 0.5f)
                {
                    side = Vector3.Cross(axis, Vector3.up).normalized;
                }

                Rect uv = FoliageTextureFactory.CellUv(cell);
                segments = Mathf.Max(1, segments);
                Vector3 pos = origin, dir = axis;
                int first = vertices.Count;
                pieceStarts.Add(first);
                for (int k = 0; k <= segments; k++)
                {
                    float s = (float)k / segments;
                    if (k > 0)
                    {
                        dir = Quaternion.AngleAxis(droopDegrees / segments, side) * dir;
                        pos += dir * (length / segments);
                    }

                    Vector3 geometric = Vector3.Cross(dir, side).normalized;
                    Vector3 n = shadingBlend > 0f ? Vector3.Lerp(geometric, shadingNormal.normalized, shadingBlend).normalized : geometric;
                    if (n.sqrMagnitude < 0.5f) n = geometric;
                    float w = Vector3.Dot(Vector3.Cross(n, side), dir) >= 0f ? 1f : -1f;
                    Color c = Color.Lerp(baseColor, tipColor, s);
                    var tangent = new Vector4(side.x, side.y, side.z, w);
                    vertices.Add(pos - side * (width / 2f));
                    vertices.Add(pos + side * (width / 2f));
                    normals.Add(n);
                    normals.Add(n);
                    tangents.Add(tangent);
                    tangents.Add(tangent);
                    uvs.Add(new Vector2(uv.xMin, uv.yMin + uv.height * s));
                    uvs.Add(new Vector2(uv.xMax, uv.yMin + uv.height * s));
                    colors.Add(c);
                    colors.Add(c);
                    if (k > 0)
                    {
                        int a = first + (k - 1) * 2;
                        Quad(a, a + 1, a + 3, a + 2, geometric);
                    }
                }
            }

            /// <summary>A straight tapered tube; UVs span the cell across the girth and along the length.</summary>
            public void Tube(Vector3 from, Vector3 to, float r0, float r1, int sides, Cell cell, Color c0, Color c1)
            {
                pieceStarts.Add(vertices.Count);
                Ring(from, (to - from).normalized, r0, sides, cell, 0f, c0, out int a);
                Ring(to, (to - from).normalized, r1, sides, cell, 1f, c1, out int b);
                Join(a, b, sides);
            }

            /// <summary>A tube along a quadratic curve.</summary>
            public void BezierTube(Vector3 p0, Vector3 p1, Vector3 p2, float r0, float r1, int segments, Cell cell, Color c0, Color c1)
            {
                pieceStarts.Add(vertices.Count);
                Vector3 At(float t) => (1f - t) * (1f - t) * p0 + 2f * (1f - t) * t * p1 + t * t * p2;
                const int sides = 6;
                int prev = -1;
                for (int k = 0; k <= segments; k++)
                {
                    float t = (float)k / segments;
                    Vector3 tangent = (At(Mathf.Min(1f, t + 0.01f)) - At(Mathf.Max(0f, t - 0.01f))).normalized;
                    Ring(At(t), tangent, Mathf.Lerp(r0, r1, t), sides, cell, t, Color.Lerp(c0, c1, t), out int ring);
                    if (prev >= 0)
                    {
                        Join(prev, ring, sides);
                    }

                    prev = ring;
                }
            }

            // A branch is a few centimetres around, but wrapping its girth across a whole 512 px atlas cell
            // minifies that cell so hard the sampler drops to a mip where the neighbouring cell has bled in.
            // Bark sits next to Palm in the atlas, which painted a pale frond along every branch of every
            // crown. Sampling a narrow band near the middle of the cell keeps the mip in safe territory.
            private const float GirthBand = 0.08f;

            private void Ring(Vector3 centre, Vector3 axis, float radius, int sides, Cell cell, float v, Color color, out int first)
            {
                Rect uv = FoliageTextureFactory.CellUv(cell);
                float uStart = uv.xMin + uv.width * (0.5f - GirthBand / 2f);
                Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
                Vector3 u = Vector3.Cross(axis, reference).normalized;
                Vector3 w = Vector3.Cross(axis, u).normalized;
                first = vertices.Count;
                for (int i = 0; i <= sides; i++)
                {
                    float ang = i * Mathf.PI * 2f / sides;
                    Vector3 radial = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                    vertices.Add(centre + radial * radius);
                    normals.Add(radial);
                    Vector3 t = Vector3.Cross(axis, radial).normalized;
                    tangents.Add(new Vector4(t.x, t.y, t.z, 1f));
                    uvs.Add(new Vector2(uStart + uv.width * GirthBand * i / sides, uv.yMin + uv.height * v));
                    colors.Add(color);
                }
            }

            private void Join(int a, int b, int sides)
            {
                for (int i = 0; i < sides; i++)
                {
                    Quad(a + i, a + i + 1, b + i + 1, b + i, normals[a + i]);
                }
            }

            /// <summary>
            /// A surface of revolution around Y from a (radius, y) profile. <paramref name="metreUvs"/> puts
            /// UVs in metres (u around the girth, v up) for tiling bark; otherwise 0..1. <paramref name="lean"/>
            /// bends the top over by that many metres per metre of height squared; <paramref name="noise"/> roughens the radius.
            /// </summary>
            public void Lathe(List<(float r, float y)> profile, int sides, bool metreUvs, Vector2 lean = default, System.Random noise = null)
            {
                pieceStarts.Add(vertices.Count);
                float top = profile[^1].y;
                float circumference = 0f;
                foreach ((float r, float y) in profile) circumference = Mathf.Max(circumference, 2f * Mathf.PI * r);
                var rings = new List<int>();
                for (int p = 0; p < profile.Count; p++)
                {
                    (float r, float y) = profile[p];
                    int first = vertices.Count;
                    rings.Add(first);
                    float h = Mathf.Clamp01(y / Mathf.Max(top, 0.001f));
                    Vector3 offset = new Vector3(lean.x, 0f, lean.y) * (h * h * top);
                    // Slope of the profile, for the normal.
                    (float rPrev, float yPrev) = profile[Mathf.Max(0, p - 1)];
                    (float rNext, float yNext) = profile[Mathf.Min(profile.Count - 1, p + 1)];
                    Vector2 slope = new Vector2(rNext - rPrev, yNext - yPrev).normalized;
                    for (int i = 0; i <= sides; i++)
                    {
                        float ang = i * Mathf.PI * 2f / sides;
                        float bump = noise != null && r > 0.02f ? 1f + (float)(noise.NextDouble() - 0.5) * 0.12f : 1f;
                        Vector3 radial = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                        vertices.Add(offset + radial * (r * bump) + Vector3.up * y);
                        Vector3 n = (radial * slope.y - Vector3.up * slope.x).normalized;
                        if (r < 1e-4f) n = slope.y >= 0f ? Vector3.up : Vector3.down;
                        normals.Add(n);
                        Vector3 t = Vector3.Cross(Vector3.up, radial).normalized;
                        tangents.Add(new Vector4(t.x, t.y, t.z, 1f));
                        uvs.Add(metreUvs ? new Vector2(circumference * i / sides, y) : new Vector2((float)i / sides, h));
                        colors.Add(Color.white);
                    }
                }

                for (int p = 0; p + 1 < rings.Count; p++)
                {
                    int a = rings[p], b = rings[p + 1];
                    for (int i = 0; i < sides; i++)
                    {
                        Quad(a + i, a + i + 1, b + i + 1, b + i, normals[a + i] + normals[b + i]);
                    }
                }
            }

            /// <summary>Nothing below <paramref name="minY"/>: a pruned crown.</summary>
            /// <summary>
            /// Lifts whole cards and tubes until nothing sits below <paramref name="minY"/>. Snapping single
            /// vertices to the plane shears the card that straddles it and smears its atlas cell into a
            /// pale blade, which is what a pruned street crown used to look like from the pavement.
            /// </summary>
            public void ClampMinY(float minY)
            {
                for (int p = 0; p < pieceStarts.Count; p++)
                {
                    int start = pieceStarts[p];
                    int end = p + 1 < pieceStarts.Count ? pieceStarts[p + 1] : vertices.Count;
                    float lowest = float.MaxValue;
                    for (int i = start; i < end; i++)
                    {
                        lowest = Mathf.Min(lowest, vertices[i].y);
                    }

                    if (lowest >= minY) continue;
                    float lift = minY - lowest;
                    for (int i = start; i < end; i++)
                    {
                        vertices[i] = new Vector3(vertices[i].x, vertices[i].y + lift, vertices[i].z);
                    }
                }
            }

            /// <summary>Two triangles for a..d, wound so the front face agrees with <paramref name="normalHint"/>.</summary>
            private void Quad(int a, int b, int c, int d, Vector3 normalHint)
            {
                Vector3 n = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                if (Vector3.Dot(n, normalHint) < 0f)
                {
                    (b, d) = (d, b);
                }

                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
            }

            public void Fill(Mesh mesh)
            {
                mesh.Clear();
                mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetTangents(tangents);
                mesh.SetUVs(0, uvs);
                mesh.SetColors(colors);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                // Wind moves leaves past their rest bounds; pad so a swaying edge is never culled early.
                Bounds b = mesh.bounds;
                b.Expand(new Vector3(0.5f, 0.15f, 0.5f));
                mesh.bounds = b;
            }
        }
    }
}
