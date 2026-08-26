using UnityEngine;
using UnityEngine.AI;
using Vent.Core.Utility;
using Vent.Enemies.Spawning;

namespace Vent.Editor
{
    /// <summary>
    /// Street furniture and outdoor spawn points for the district, built from primitives the way
    /// <see cref="PropLibrary"/> builds office furniture. Every piece's origin is on the ground at
    /// its footprint centre and faces +Z. Big masses get colliders (they are cover and they carve
    /// the NavMesh); greebles do not. Everything is Environment, except the manhole covers, which
    /// are Vent-layer bullet targets like the indoor grates.
    /// </summary>
    public static class StreetPropLibrary
    {
        public enum Kind
        {
            StreetLamp, LotLamp, Dumpster, Planter, Bollard, Hydrant, BusShelter, Bench, TrashCan, NewsBox,
            Tree, TrafficCone, PumpIsland, Trailer, PortaLoo, MaterialStack, Fountain, JerseyBarrier,
            /// <summary>A concrete planter of flowering shrubs.</summary>
            FlowerPlanter,
            /// <summary>Clipped box hedges, 3 m and 1.5 m runs along X. Solid.</summary>
            Hedge, HedgeShort,
            /// <summary>A round bush, plain or in flower. Solid.</summary>
            Shrub, FloweringShrub,
            /// <summary>A pavement tree: crown pruned clear of the road, roots in a grated pit.</summary>
            StreetTree,
            /// <summary>A tall conifer.</summary>
            Conifer,
            /// <summary>Weeds and grass through a crack: no collider.</summary>
            Weeds,
        }

        private const uint ExteriorRenderingLayer = 1u << 1;

        /// <summary>Footprint in metres (x = width across the front, y = depth).</summary>
        public static Vector2 Footprint(Kind kind) => kind switch
        {
            Kind.StreetLamp => new Vector2(0.4f, 0.4f),
            Kind.LotLamp => new Vector2(0.5f, 0.5f),
            Kind.Dumpster => new Vector2(1.8f, 1.2f),
            Kind.Planter => new Vector2(1.6f, 0.8f),
            Kind.Bollard => new Vector2(0.25f, 0.25f),
            Kind.Hydrant => new Vector2(0.4f, 0.4f),
            Kind.BusShelter => new Vector2(4f, 1.6f),
            Kind.Bench => new Vector2(1.8f, 0.6f),
            Kind.TrashCan => new Vector2(0.6f, 0.6f),
            Kind.NewsBox => new Vector2(0.5f, 0.5f),
            Kind.Tree => new Vector2(3.5f, 3.5f),
            Kind.TrafficCone => new Vector2(0.4f, 0.4f),
            Kind.PumpIsland => new Vector2(4.5f, 1.4f),
            Kind.Trailer => new Vector2(2.6f, 12f),
            Kind.PortaLoo => new Vector2(1.2f, 1.2f),
            Kind.MaterialStack => new Vector2(2.4f, 1.4f),
            Kind.Fountain => new Vector2(6f, 6f),
            Kind.JerseyBarrier => new Vector2(3f, 0.6f),
            Kind.FlowerPlanter => new Vector2(1.6f, 0.8f),
            Kind.Hedge => new Vector2(3f, 0.6f),
            Kind.HedgeShort => new Vector2(1.5f, 0.6f),
            Kind.Shrub => new Vector2(1.6f, 1.6f),
            Kind.FloweringShrub => new Vector2(1.3f, 1.3f),
            Kind.StreetTree => new Vector2(1.4f, 1.4f),
            Kind.Conifer => new Vector2(4f, 4f),
            Kind.Weeds => new Vector2(1f, 1f),
            _ => Vector2.one,
        };

        /// <summary>Build a piece under <paramref name="parent"/>. <paramref name="lit"/> only matters for lamps: whether they carry a real light.</summary>
        public static GameObject Build(Kind kind, GameAssets a, System.Random rng, Transform parent, bool lit = false)
        {
            var root = new GameObject(kind.ToString());
            root.transform.SetParent(parent, false);
            Transform t = root.transform;
            switch (kind)
            {
                case Kind.StreetLamp: StreetLamp(t, a, lit, arm: 2f, height: 5f); break;
                case Kind.LotLamp: StreetLamp(t, a, lit, arm: 0f, height: 6f); break;
                case Kind.Dumpster: Dumpster(t, a); break;
                case Kind.Planter: Planter(t, a, rng); break;
                case Kind.Bollard: Bollard(t, a); break;
                case Kind.Hydrant: Hydrant(t, a); break;
                case Kind.BusShelter: BusShelter(t, a); break;
                case Kind.Bench: Bench(t, a); break;
                case Kind.TrashCan: TrashCan(t, a); break;
                case Kind.NewsBox: NewsBox(t, a, rng); break;
                case Kind.Tree: Tree(t, a, rng); break;
                case Kind.TrafficCone: TrafficCone(t, a); break;
                case Kind.PumpIsland: PumpIsland(t, a); break;
                case Kind.Trailer: Trailer(t, a); break;
                case Kind.PortaLoo: PortaLoo(t, a); break;
                case Kind.MaterialStack: MaterialStack(t, a, rng); break;
                case Kind.Fountain: Fountain(t, a); break;
                case Kind.JerseyBarrier: JerseyBarrier(t, a); break;
                case Kind.FlowerPlanter: FlowerPlanter(t, a, rng); break;
                case Kind.Hedge: FoliageLibrary.Hedge(t, a, rng, 3f, 0.75f, 0.55f, rng.Next(3)); break;
                case Kind.HedgeShort: FoliageLibrary.Hedge(t, a, rng, 1.5f, 0.75f, 0.55f, rng.Next(3)); break;
                case Kind.Shrub: FoliageLibrary.Shrub(t, a, rng, Rand(rng, 0.6f, 0.85f), flowering: false, rng.Next(4)); break;
                case Kind.FloweringShrub: FoliageLibrary.Shrub(t, a, rng, Rand(rng, 0.5f, 0.7f), flowering: true, rng.Next(4)); break;
                case Kind.StreetTree: StreetTree(t, a, rng); break;
                case Kind.Conifer: Conifer(t, a, rng); break;
                case Kind.Weeds: FoliageLibrary.Weeds(t, a, rng, Rand(rng, 0.4f, 0.7f), rng.Next(4)); break;
            }

            Layers.SetRecursively(root, Layers.EnvironmentIndex);
            MarkExterior(root);
            return root;
        }

        /// <summary>
        /// A manhole cover on a sidewalk or lot: the outdoor equivalent of an AC vent. The zombie
        /// starts 1.6 m underground and rises through the cover to the floor point beside it. The
        /// cover is a Vent-layer collider (shootable, ignored by cars and zombies). Faces up so the
        /// spawner's line-of-sight probe sits just above the lid.
        /// </summary>
        public static AirVent BuildManhole(GameAssets a, Transform parent, Vector3 surfacePosition, Vector3 alongSurface)
        {
            return BuildSpawn(a, parent, "Manhole", surfacePosition, alongSurface, root =>
            {
                GameObject cover = PrefabFactory.Primitive(PrimitiveType.Cylinder, "Cover", root, Vector3.zero, new Vector3(0.8f, 0.015f, 0.8f), a.MetalDark, collider: true);
                cover.transform.rotation = Quaternion.identity;
                cover.transform.position = surfacePosition + Vector3.up * 0.015f;
                GameObject rim = PrefabFactory.Primitive(PrimitiveType.Cylinder, "Rim", root, Vector3.zero, new Vector3(0.9f, 0.01f, 0.9f), a.MetalGrey, collider: false);
                rim.transform.rotation = Quaternion.identity;
                rim.transform.position = surfacePosition + Vector3.up * 0.008f;
                return cover.transform;
            });
        }

        /// <summary>A storm drain grate in the gutter: same spawn mechanics as a manhole, rectangular.</summary>
        public static AirVent BuildStormDrain(GameAssets a, Transform parent, Vector3 surfacePosition, Vector3 alongSurface)
        {
            return BuildSpawn(a, parent, "StormDrain", surfacePosition, alongSurface, root =>
            {
                GameObject grate = PrefabFactory.Primitive(PrimitiveType.Cube, "Grate", root, Vector3.zero, new Vector3(0.9f, 0.04f, 0.45f), a.MetalDark, collider: true);
                grate.transform.rotation = Quaternion.LookRotation(alongSurface);
                grate.transform.position = surfacePosition + Vector3.up * 0.02f;
                for (int i = 0; i < 5; i++)
                {
                    GameObject slot = PrefabFactory.Primitive(PrimitiveType.Cube, $"Slot{i}", grate.transform, new Vector3(-0.4f + i * 0.2f, 0.51f, 0f), new Vector3(0.08f, 0.02f, 0.8f), a.Trim, collider: false);
                    slot.transform.localRotation = Quaternion.identity;
                }

                return grate.transform;
            });
        }

        private static AirVent BuildSpawn(GameAssets a, Transform parent, string name, Vector3 surfacePosition, Vector3 alongSurface, System.Func<Transform, Transform> visual)
        {
            var rootGo = new GameObject(name);
            Transform root = rootGo.transform;
            root.SetParent(parent, false);
            alongSurface.y = 0f;
            alongSurface = alongSurface.sqrMagnitude > 0.001f ? alongSurface.normalized : Vector3.forward;
            // Forward = up: AirVent.Facing is where the spawner probes for line of sight, and the
            // only clear air around a lid is above it. The floor point goes along the surface.
            root.SetPositionAndRotation(surfacePosition, Quaternion.LookRotation(Vector3.up, alongSurface));

            var grate = new GameObject("SpawnPoint");
            grate.transform.SetParent(root, false);
            grate.transform.position = surfacePosition + Vector3.down * 1.6f;

            var floor = new GameObject("FloorPoint");
            floor.transform.SetParent(root, false);
            floor.transform.position = surfacePosition + alongSurface * 1.0f;

            Transform lid = visual(root);
            var vent = rootGo.AddComponent<AirVent>();
            vent.Configure(grate.transform, floor.transform, a.Vents, lid);
            Layers.SetRecursively(rootGo, Layers.VentIndex);
            MarkExterior(rootGo);
            return vent;
        }

        // ------------------------------------------------------------------ pieces

        private static void StreetLamp(Transform t, GameAssets a, bool lit, float arm, float height)
        {
            Cyl(t, "Base", new Vector3(0f, 0.08f, 0f), 0.22f, 0.08f, a.MetalDark);
            Cyl(t, "Pole", new Vector3(0f, height / 2f, 0f), 0.08f, height / 2f, a.MetalDark, collider: false);
            Vector3 headAt;
            if (arm > 0f)
            {
                Box(t, "Arm", new Vector3(0f, height - 0.1f, arm / 2f), new Vector3(0.1f, 0.1f, arm), a.MetalDark, collider: false);
                headAt = new Vector3(0f, height - 0.2f, arm);
            }
            else
            {
                headAt = new Vector3(0f, height - 0.1f, 0f);
            }

            Box(t, "Head", headAt, new Vector3(0.5f, 0.2f, 0.32f), a.MetalDark, collider: false);
            Box(t, "Lens", headAt + Vector3.down * 0.11f, new Vector3(0.42f, 0.02f, 0.26f), a.LampHead, collider: false);
            if (!lit)
            {
                return;
            }

            var lightGo = new GameObject("LampLight");
            lightGo.transform.SetParent(t, false);
            lightGo.transform.localPosition = headAt + Vector3.down * 0.3f;
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 18f;
            light.intensity = 4f;
            light.color = new Color(1f, 0.76f, 0.48f);
            // No shadows and never baked: a hundred lamps would swamp the shadow atlas and the
            // lightmapper alike; sodium light is soft anyway.
            light.shadows = LightShadows.None;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            lightGo.layer = Layers.PlayerIndex; // see PrefabFactory: lights live on a layer both cameras cull in
        }

        private static void Dumpster(Transform t, GameAssets a)
        {
            Box(t, "Body", new Vector3(0f, 0.7f, 0f), new Vector3(1.8f, 1.2f, 1.2f), a.PropAlt);
            Box(t, "Lid", new Vector3(0f, 1.34f, -0.05f), new Vector3(1.84f, 0.08f, 1.3f), a.MetalDark, collider: false);
            Box(t, "Rail", new Vector3(0f, 0.55f, 0.61f), new Vector3(1.6f, 0.06f, 0.04f), a.MetalGrey, collider: false);
            for (int i = 0; i < 2; i++)
            {
                Cyl(t, $"Wheel{i}", new Vector3(-0.7f + i * 1.4f, 0.06f, 0.5f), 0.06f, 0.05f, a.Plastic, collider: false);
            }
        }

        private static void Planter(Transform t, GameAssets a, System.Random rng)
        {
            // Concrete box, mulch, two clipped box shrubs.
            Box(t, "Box", new Vector3(0f, 0.3f, 0f), new Vector3(1.6f, 0.6f, 0.8f), a.Concrete);
            Box(t, "Soil", new Vector3(0f, 0.58f, 0f), new Vector3(1.5f, 0.04f, 0.7f), a.Mulch, collider: false);
            for (int i = 0; i < 2; i++)
            {
                GameObject shrub = FoliageLibrary.Shrub(t, a, rng, Rand(rng, 0.3f, 0.38f), flowering: false, rng.Next(4));
                shrub.transform.localPosition = new Vector3(-0.38f + i * 0.76f, 0.58f, Rand(rng, -0.08f, 0.08f));
                Object.DestroyImmediate(shrub.GetComponent<Collider>()); // the box is the collider
            }
        }

        private static void FlowerPlanter(Transform t, GameAssets a, System.Random rng)
        {
            Box(t, "Box", new Vector3(0f, 0.3f, 0f), new Vector3(1.6f, 0.6f, 0.8f), a.Concrete);
            Box(t, "Soil", new Vector3(0f, 0.58f, 0f), new Vector3(1.5f, 0.04f, 0.7f), a.Mulch, collider: false);
            for (int i = 0; i < 3; i++)
            {
                GameObject shrub = FoliageLibrary.Shrub(t, a, rng, Rand(rng, 0.26f, 0.34f), flowering: true, rng.Next(4));
                shrub.transform.localPosition = new Vector3(-0.5f + i * 0.5f, 0.58f, Rand(rng, -0.1f, 0.1f));
                Object.DestroyImmediate(shrub.GetComponent<Collider>());
            }
        }

        private static void Bollard(Transform t, GameAssets a)
        {
            Cyl(t, "Post", new Vector3(0f, 0.45f, 0f), 0.09f, 0.45f, a.MetalDark);
            Cyl(t, "Cap", new Vector3(0f, 0.92f, 0f), 0.1f, 0.02f, a.PaintYellow, collider: false);
        }

        private static void Hydrant(Transform t, GameAssets a)
        {
            Cyl(t, "Body", new Vector3(0f, 0.4f, 0f), 0.14f, 0.4f, a.VendingRed);
            Sphere(t, "Cap", new Vector3(0f, 0.86f, 0f), 0.15f, a.VendingRed, collider: false);
            Cyl(t, "NozzleL", new Vector3(-0.2f, 0.45f, 0f), 0.06f, 0.1f, a.VendingRed, collider: false).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Cyl(t, "NozzleR", new Vector3(0.2f, 0.45f, 0f), 0.06f, 0.1f, a.VendingRed, collider: false).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }

        private static void BusShelter(Transform t, GameAssets a)
        {
            Box(t, "Back", new Vector3(0f, 1.3f, -0.75f), new Vector3(4f, 2.4f, 0.04f), a.Glass);
            Box(t, "SideL", new Vector3(-1.98f, 1.3f, -0.1f), new Vector3(0.04f, 2.4f, 1.3f), a.Glass);
            Box(t, "SideR", new Vector3(1.98f, 1.3f, -0.1f), new Vector3(0.04f, 2.4f, 1.3f), a.Glass);
            Box(t, "Roof", new Vector3(0f, 2.55f, 0f), new Vector3(4.2f, 0.1f, 1.8f), a.MetalDark, collider: false);
            Box(t, "Seat", new Vector3(0f, 0.45f, -0.45f), new Vector3(3f, 0.06f, 0.4f), a.MetalGrey);
            Box(t, "SeatFrame", new Vector3(0f, 0.22f, -0.45f), new Vector3(2.8f, 0.4f, 0.06f), a.MetalDark, collider: false);
            Box(t, "Ad", new Vector3(-1.95f, 1.4f, -0.1f), new Vector3(0.02f, 1.6f, 1.0f), a.PosterB, collider: false);
        }

        private static void Bench(Transform t, GameAssets a)
        {
            Box(t, "Seat", new Vector3(0f, 0.44f, 0f), new Vector3(1.8f, 0.06f, 0.45f), a.Wood);
            Box(t, "Back", new Vector3(0f, 0.72f, -0.22f), new Vector3(1.8f, 0.4f, 0.05f), a.Wood, collider: false);
            for (int i = 0; i < 2; i++)
            {
                float x = -0.75f + i * 1.5f;
                Box(t, $"Leg{i}", new Vector3(x, 0.22f, 0f), new Vector3(0.06f, 0.44f, 0.45f), a.MetalDark, collider: false);
            }
        }

        private static void TrashCan(Transform t, GameAssets a)
        {
            Cyl(t, "Can", new Vector3(0f, 0.5f, 0f), 0.3f, 0.5f, a.MetalDark);
            Cyl(t, "Rim", new Vector3(0f, 1.0f, 0f), 0.32f, 0.03f, a.MetalGrey, collider: false);
            Cyl(t, "Liner", new Vector3(0f, 0.98f, 0f), 0.26f, 0.02f, a.Plastic, collider: false);
        }

        private static void NewsBox(Transform t, GameAssets a, System.Random rng)
        {
            Material face = rng.NextDouble() < 0.5 ? a.PosterA : a.PosterC;
            Box(t, "Body", new Vector3(0f, 0.6f, 0f), new Vector3(0.5f, 1.2f, 0.5f), a.MetalDark);
            Box(t, "Window", new Vector3(0f, 0.85f, 0.251f), new Vector3(0.4f, 0.4f, 0.01f), a.Glass, collider: false);
            Box(t, "Panel", new Vector3(0f, 0.4f, 0.251f), new Vector3(0.44f, 0.36f, 0.01f), face, collider: false);
        }

        /// <summary>A park or lot tree: mostly broadleaf, sometimes a birch, in a mulch bed with last year's leaves around it.</summary>
        private static void Tree(Transform t, GameAssets a, System.Random rng)
        {
            FoliageLibrary.TreeKind kind = rng.NextDouble() < 0.7 ? FoliageLibrary.TreeKind.Broadleaf : FoliageLibrary.TreeKind.Birch;
            FoliageLibrary.Tree(t, a, rng, kind, rng.Next(5), out float crown);
            Cyl(t, "Bed", new Vector3(0f, 0.01f, 0f), 0.9f, 0.01f, a.Mulch, collider: false);
            FoliageLibrary.Litter(t, a, rng, crown * 0.7f, rng.Next(4));
        }

        private static void StreetTree(Transform t, GameAssets a, System.Random rng)
        {
            FoliageLibrary.Tree(t, a, rng, FoliageLibrary.TreeKind.Street, rng.Next(6), out _);
            Box(t, "Grate", new Vector3(0f, 0.008f, 0f), new Vector3(1.3f, 0.016f, 1.3f), a.MetalDark, collider: false);
            Cyl(t, "Bed", new Vector3(0f, 0.012f, 0f), 0.42f, 0.008f, a.Mulch, collider: false);
            if (rng.NextDouble() < 0.6)
            {
                FoliageLibrary.Litter(t, a, rng, 1.2f, rng.Next(4));
            }
        }

        private static void Conifer(Transform t, GameAssets a, System.Random rng)
        {
            FoliageLibrary.Tree(t, a, rng, FoliageLibrary.TreeKind.Conifer, rng.Next(4), out _);
            Cyl(t, "Bed", new Vector3(0f, 0.01f, 0f), 0.8f, 0.01f, a.Mulch, collider: false);
        }

        private static void TrafficCone(Transform t, GameAssets a)
        {
            Box(t, "Base", new Vector3(0f, 0.02f, 0f), new Vector3(0.4f, 0.04f, 0.4f), a.MetalDark, collider: false);
            Cyl(t, "Cone", new Vector3(0f, 0.38f, 0f), 0.12f, 0.36f, a.NeonAmber, collider: false);
            Cyl(t, "Band", new Vector3(0f, 0.45f, 0f), 0.125f, 0.05f, a.PaintWhite, collider: false);
        }

        private static void PumpIsland(Transform t, GameAssets a)
        {
            Box(t, "Kerb", new Vector3(0f, 0.08f, 0f), new Vector3(4.5f, 0.16f, 1.4f), a.Concrete);
            for (int i = 0; i < 2; i++)
            {
                float x = -1.2f + i * 2.4f;
                Box(t, $"Pump{i}", new Vector3(x, 0.96f, 0f), new Vector3(0.6f, 1.6f, 0.5f), a.MetalGrey);
                Box(t, $"Screen{i}", new Vector3(x, 1.35f, 0.26f), new Vector3(0.4f, 0.3f, 0.01f), a.Screen, collider: false);
                Box(t, $"Nozzle{i}", new Vector3(x + 0.35f, 1.0f, 0f), new Vector3(0.08f, 0.3f, 0.12f), a.MetalDark, collider: false);
            }

            Cyl(t, "Post", new Vector3(0f, 1.1f, 0f), 0.06f, 0.95f, a.PaintYellow, collider: false);
        }

        private static void Trailer(Transform t, GameAssets a)
        {
            Box(t, "Body", new Vector3(0f, 2.4f, 0f), new Vector3(2.6f, 2.8f, 12f), a.MetalPanel);
            Box(t, "Chassis", new Vector3(0f, 0.9f, 0f), new Vector3(2.2f, 0.2f, 11.5f), a.MetalDark, collider: false);
            Box(t, "Legs", new Vector3(0f, 0.5f, 4.5f), new Vector3(1.8f, 1.0f, 0.15f), a.MetalDark, collider: false);
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -1.05f : 1.05f;
                float z = i < 2 ? -3.2f : -4.6f;
                Cyl(t, $"Wheel{i}", new Vector3(x, 0.5f, z), 0.5f, 0.2f, a.Tyre, collider: false).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
        }

        private static void PortaLoo(Transform t, GameAssets a)
        {
            Box(t, "Cabin", new Vector3(0f, 1.15f, 0f), new Vector3(1.2f, 2.3f, 1.2f), a.NeonBlue);
            Box(t, "Roof", new Vector3(0f, 2.34f, 0f), new Vector3(1.26f, 0.08f, 1.26f), a.PaintWhite, collider: false);
            Box(t, "Door", new Vector3(0f, 1.1f, 0.605f), new Vector3(0.7f, 2.0f, 0.02f), a.Plastic, collider: false);
        }

        private static void MaterialStack(Transform t, GameAssets a, System.Random rng)
        {
            Box(t, "Pallet", new Vector3(0f, 0.07f, 0f), new Vector3(2.4f, 0.14f, 1.4f), a.Wood, collider: false);
            int layers = 2 + rng.Next(3);
            Material m = rng.NextDouble() < 0.5 ? a.Concrete : a.Brick;
            for (int i = 0; i < layers; i++)
            {
                Box(t, $"Layer{i}", new Vector3(0f, 0.14f + 0.2f + i * 0.4f, 0f), new Vector3(2.2f - i * 0.1f, 0.4f, 1.2f), m, collider: i == 0);
            }
        }

        private static void Fountain(Transform t, GameAssets a)
        {
            Cyl(t, "Basin", new Vector3(0f, 0.3f, 0f), 3f, 0.3f, a.Concrete);
            Cyl(t, "Water", new Vector3(0f, 0.5f, 0f), 2.7f, 0.02f, a.Glass, collider: false);
            Cyl(t, "Column", new Vector3(0f, 1.0f, 0f), 0.4f, 0.5f, a.Concrete, collider: false);
            Cyl(t, "Bowl", new Vector3(0f, 1.6f, 0f), 1.2f, 0.12f, a.Concrete, collider: false);
            Cyl(t, "Spout", new Vector3(0f, 2.2f, 0f), 0.15f, 0.5f, a.Concrete, collider: false);
        }

        private static void JerseyBarrier(Transform t, GameAssets a)
        {
            Box(t, "Base", new Vector3(0f, 0.25f, 0f), new Vector3(3f, 0.5f, 0.6f), a.Concrete);
            Box(t, "Top", new Vector3(0f, 0.75f, 0f), new Vector3(3f, 0.5f, 0.3f), a.Concrete, collider: false);
            Box(t, "Stripe", new Vector3(0f, 0.6f, 0.16f), new Vector3(2.6f, 0.15f, 0.01f), a.NeonAmber, collider: false);
        }

        // ------------------------------------------------------------------ primitives

        private static float Rand(System.Random rng, float min, float max) => (float)(min + rng.NextDouble() * (max - min));

        /// <summary>Everything outdoors is lit by the sun (rendering layer 1) as well as the default layer lights.</summary>
        public static void MarkExterior(GameObject root)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                r.renderingLayerMask = ExteriorRenderingLayer | 1u;
            }
        }

        private static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material material, bool collider = true)
            => PrefabFactory.Primitive(PrimitiveType.Cube, name, parent, localPos, size, material, collider);

        private static GameObject Cyl(Transform parent, string name, Vector3 localPos, float radius, float halfHeight, Material material, bool collider = true)
            => PrefabFactory.Primitive(PrimitiveType.Cylinder, name, parent, localPos, new Vector3(radius * 2f, halfHeight, radius * 2f), material, collider);

        private static GameObject Sphere(Transform parent, string name, Vector3 localPos, float radius, Material material, bool collider = true)
            => PrefabFactory.Primitive(PrimitiveType.Sphere, name, parent, localPos, Vector3.one * radius * 2f, material, collider);
    }
}
