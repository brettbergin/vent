using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Vent.Core.Utility;
using Vent.Enemies.Spawning;

namespace Vent.Editor
{
    /// <summary>Tunable knobs for the commercial district around the office building. Metres throughout.</summary>
    public sealed class DistrictLayout
    {
        public int Seed = 90210;
        /// <summary>Lots per side; the office building sits in the centre lot.</summary>
        public int Columns = 5, Rows = 5;
        public float OuterLotSize = 40f;
        /// <summary>The centre lot holds the 60×45 building plus its apron, front lot and rear yard.</summary>
        public float CentreLotWidth = 104f, CentreLotDepth = 60f;
        public float StreetWidth = 14f, SidewalkWidth = 3f, KerbHeight = 0.15f, SlabThickness = 0.3f;
        public float LaneDashLength = 3f, LaneDashGap = 6f;
        public float LampSpacing = 35f;
        /// <summary>Lamps farther than this from the origin are unlit props: the light budget is finite, the skyline is not.</summary>
        public float LitRadius = 120f;
        public float BarrierHeight = 1.2f, FenceHeight = 1.6f;
        public float BayWidth = 2.7f, BayDepth = 5f;
        public float ProbeSpacing = 15f;
        public bool Vents = true;
    }

    /// <summary>
    /// The commercial district: a 5×5 grid of city blocks around the office building, with streets,
    /// sidewalks, parking lots, storefronts, towers, warehouses, a supermarket, a gas station, a
    /// park, a construction site, street lamps, a perimeter barrier and outdoor spawn points.
    /// Deterministic for a seed. Every walkable or drivable surface is an Environment collider so
    /// it bakes into the NavMesh and the player's containment accepts it; nothing here contributes
    /// to global illumination (the sun lights it directly) so the lightmap bake stays short.
    ///
    /// Naming matters: the scene tests look for <c>Floor</c>, <c>Glass*</c>, <c>Light_*</c>,
    /// <c>WindowLight_*</c> and <c>Building*</c> to describe the office; the district uses none of them.
    /// </summary>
    public static class DistrictGenerator
    {
        /// <summary>Where a car can be parked: on the ground, nose along <see cref="Yaw"/>.</summary>
        public readonly struct ParkingSpot
        {
            public readonly Vector3 Position;
            public readonly float Yaw;
            public readonly string Lot;

            public ParkingSpot(Vector3 position, float yaw, string lot)
            {
                Position = position;
                Yaw = yaw;
                Lot = lot;
            }
        }

        public enum BlockKind { Supermarket, GasStation, OfficeTower, RetailStrip, Warehouse, Apartments, Park, Plaza, ConstructionSite, Diner }

        public sealed class Result
        {
            public GameObject Root;
            /// <summary>The whole district including the barrier.</summary>
            public Bounds Bounds;
            /// <summary>Inside the ring road's outer kerb.</summary>
            public Bounds DrivableBounds;
            /// <summary>Sorted nearest-first to the front door.</summary>
            public readonly List<ParkingSpot> ParkingSpots = new();
            /// <summary>Inactive until the front door opens (see FrontDoor).</summary>
            public GameObject ExteriorVents;
            public readonly List<Vector3> LampPositions = new();
            /// <summary>World bounds of every building mass, for probe placement and tests.</summary>
            public readonly List<Bounds> Masses = new();
        }

        /// <summary>An axis-aligned rectangle on the ground.</summary>
        public readonly struct Area
        {
            public readonly float X0, X1, Z0, Z1;

            public Area(float x0, float x1, float z0, float z1)
            {
                X0 = x0;
                X1 = x1;
                Z0 = z0;
                Z1 = z1;
            }

            public float Width => X1 - X0;
            public float Depth => Z1 - Z0;
            public Vector3 Center => new((X0 + X1) / 2f, 0f, (Z0 + Z1) / 2f);
            public Area Expand(float m) => new(X0 - m, X1 + m, Z0 - m, Z1 + m);
            public bool Contains(float x, float z) => x >= X0 && x <= X1 && z >= Z0 && z <= Z1;
        }

        private const uint ExteriorRenderingLayer = 1u << 1;

        private static readonly StaticEditorFlags StaticFlags =
            StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;

        /// <summary>Half extents of the district (to the barrier); pure arithmetic so the building can push its skyline out first.</summary>
        public static Vector2 HalfExtents(DistrictLayout layout)
        {
            float corridor = layout.StreetWidth + 2f * layout.SidewalkWidth;
            float halfX = HalfSpan(layout.Columns, layout.OuterLotSize, layout.CentreLotWidth, corridor) + corridor + 1f;
            float halfZ = HalfSpan(layout.Rows, layout.OuterLotSize, layout.CentreLotDepth, corridor) + corridor + 1f;
            return new Vector2(halfX, halfZ);
        }

        private static float HalfSpan(int count, float outer, float centre, float corridor)
        {
            return ((count - 1) * outer + centre + (count - 1) * corridor) / 2f;
        }

        public static Result Generate(GameAssets a, DistrictLayout layout, BuildingGenerator.Result building, Transform parent = null)
        {
            var rng = new System.Random(layout.Seed);
            var result = new Result { Root = new GameObject("District") };
            if (parent != null)
            {
                result.Root.transform.SetParent(parent, false);
            }

            Transform root = result.Root.transform;
            var ctx = new Context
            {
                A = a,
                Rng = rng,
                Layout = layout,
                Result = result,
                Roads = Child(root, "Roads"),
                Sidewalks = Child(root, "Sidewalks"),
                Lots = Child(root, "Lots"),
                Blocks = Child(root, "Blocks"),
                Markings = Child(root, "Markings"),
                Props = Child(root, "Props"),
                Lamps = Child(root, "Lamps"),
                Barrier = Child(root, "Barrier"),
                Spots = Child(root, "ParkingSpots"),
                Vents = Child(root, "ExteriorVents"),
                Probes = Child(root, "Probes"),
            };

            float corridor = layout.StreetWidth + 2f * layout.SidewalkWidth;
            float sw = layout.SidewalkWidth;
            List<(float min, float max)> xs = Intervals(layout.Columns, layout.OuterLotSize, layout.CentreLotWidth, corridor);
            List<(float min, float max)> zs = Intervals(layout.Rows, layout.OuterLotSize, layout.CentreLotDepth, corridor);
            float lotsHalfX = xs[^1].max, lotsHalfZ = zs[^1].max;
            float ringInnerX = lotsHalfX + sw, ringInnerZ = lotsHalfZ + sw;           // blocks' outer edge / ring road's inner kerb
            float ringOuterX = ringInnerX + layout.StreetWidth, ringOuterZ = ringInnerZ + layout.StreetWidth;
            float edgeX = ringOuterX + sw, edgeZ = ringOuterZ + sw;                    // outer sidewalk's outer edge
            Vector2 half = HalfExtents(layout);
            result.Bounds = new Bounds(new Vector3(0f, 15f, 0f), new Vector3(half.x * 2f, 60f, half.y * 2f));
            result.DrivableBounds = new Bounds(new Vector3(0f, 0f, 0f), new Vector3(ringOuterX * 2f, 10f, ringOuterZ * 2f));
            ctx.RoadTop = -layout.KerbHeight;

            // ---- Blocks (lot + sidewalk ring) at y = 0, roads between them at y = -kerb --------------
            int cc = layout.Columns / 2, cr = layout.Rows / 2;
            var blocks = new Area[layout.Columns, layout.Rows];
            for (int ci = 0; ci < layout.Columns; ci++)
            {
                for (int ri = 0; ri < layout.Rows; ri++)
                {
                    var lot = new Area(xs[ci].min, xs[ci].max, zs[ri].min, zs[ri].max);
                    blocks[ci, ri] = lot.Expand(sw);
                    BuildSidewalkRing(ctx, $"{ci}_{ri}", lot, sw);
                }
            }

            // Outer sidewalk ring around the ring road.
            Slab(ctx.Sidewalks, "Sidewalk_Ring_W", new Area(-edgeX, -ringOuterX, -edgeZ, edgeZ), 0f, layout.SlabThickness, a.Concrete);
            Slab(ctx.Sidewalks, "Sidewalk_Ring_E", new Area(ringOuterX, edgeX, -edgeZ, edgeZ), 0f, layout.SlabThickness, a.Concrete);
            Slab(ctx.Sidewalks, "Sidewalk_Ring_S", new Area(-ringOuterX, ringOuterX, -edgeZ, -ringOuterZ), 0f, layout.SlabThickness, a.Concrete);
            Slab(ctx.Sidewalks, "Sidewalk_Ring_N", new Area(-ringOuterX, ringOuterX, ringOuterZ, edgeZ), 0f, layout.SlabThickness, a.Concrete);

            // Interior north-south avenues run the full inner height; east-west streets are segmented
            // between them so no two road slabs overlap (coplanar slabs z-fight).
            var nsRoads = new List<Area>();
            var ewRoads = new List<Area>();
            for (int ci = 0; ci < layout.Columns - 1; ci++)
            {
                var road = new Area(blocks[ci, 0].X1, blocks[ci + 1, 0].X0, -ringInnerZ, ringInnerZ);
                nsRoads.Add(road);
                Slab(ctx.Roads, $"Road_NS_{ci + 1}", road, ctx.RoadTop, layout.SlabThickness, a.Asphalt);
            }

            for (int ri = 0; ri < layout.Rows - 1; ri++)
            {
                for (int ci = 0; ci < layout.Columns; ci++)
                {
                    var road = new Area(blocks[ci, ri].X0, blocks[ci, ri].X1, blocks[0, ri].Z1, blocks[0, ri + 1].Z0);
                    ewRoads.Add(road);
                    Slab(ctx.Roads, $"Road_EW_{ri + 1}_{ci}", road, ctx.RoadTop, layout.SlabThickness, a.Asphalt);
                }
            }

            var ringW = new Area(-ringOuterX, -ringInnerX, -ringInnerZ, ringInnerZ);
            var ringE = new Area(ringInnerX, ringOuterX, -ringInnerZ, ringInnerZ);
            var ringS = new Area(-ringOuterX, ringOuterX, -ringOuterZ, -ringInnerZ);
            var ringN = new Area(-ringOuterX, ringOuterX, ringInnerZ, ringOuterZ);
            Slab(ctx.Roads, "Road_Ring_W", ringW, ctx.RoadTop, layout.SlabThickness, a.Asphalt);
            Slab(ctx.Roads, "Road_Ring_E", ringE, ctx.RoadTop, layout.SlabThickness, a.Asphalt);
            Slab(ctx.Roads, "Road_Ring_S", ringS, ctx.RoadTop, layout.SlabThickness, a.Asphalt);
            Slab(ctx.Roads, "Road_Ring_N", ringN, ctx.RoadTop, layout.SlabThickness, a.Asphalt);

            // ---- Markings -------------------------------------------------------------------------
            var ewGaps = new List<(float z0, float z1)>();
            for (int ri = 0; ri < layout.Rows - 1; ri++)
            {
                ewGaps.Add((blocks[0, ri].Z1, blocks[0, ri + 1].Z0));
            }

            foreach (Area road in nsRoads)
            {
                PaintRoad(ctx, road, alongZ: true, skip: ewGaps);
            }

            foreach (Area road in ewRoads)
            {
                PaintRoad(ctx, road, alongZ: false, skip: null);
            }

            PaintRoad(ctx, ringW, alongZ: true, skip: null);
            PaintRoad(ctx, ringE, alongZ: true, skip: null);
            PaintRoad(ctx, ringS, alongZ: false, skip: null);
            PaintRoad(ctx, ringN, alongZ: false, skip: null);

            foreach (Area ns in nsRoads)
            {
                foreach ((float z0, float z1) in ewGaps)
                {
                    PaintCrosswalks(ctx, new Area(ns.X0, ns.X1, z0, z1));
                }
            }

            // ---- The office block and the twenty-four others --------------------------------------
            BuildOfficeLot(ctx, new Area(xs[cc].min, xs[cc].max, zs[cr].min, zs[cr].max), building);

            List<BlockKind> deck = Deck(rng);
            int dealt = 0;
            for (int ri = 0; ri < layout.Rows; ri++)
            {
                for (int ci = 0; ci < layout.Columns; ci++)
                {
                    if (ci == cc && ri == cr)
                    {
                        continue;
                    }

                    var lot = new Area(xs[ci].min, xs[ci].max, zs[ri].min, zs[ri].max);
                    Vector3 facing = FacingToCentre(ci - cc, ri - cr);
                    string lotName = $"{ci}_{ri}";
                    if (ci == cc + 1 && ri == cr)
                    {
                        BuildBlock(ctx, BlockKind.Supermarket, lot, facing, lotName);
                    }
                    else if (ci == cc - 1 && ri == cr)
                    {
                        BuildBlock(ctx, BlockKind.RetailStrip, lot, facing, lotName);
                    }
                    else if (ci == cc && ri == cr + 1)
                    {
                        // The long lot north of the office is two businesses side by side.
                        BuildBlock(ctx, BlockKind.GasStation, new Area(lot.X0, lot.Center.x, lot.Z0, lot.Z1), facing, lotName + "W");
                        BuildBlock(ctx, BlockKind.Diner, new Area(lot.Center.x, lot.X1, lot.Z0, lot.Z1), facing, lotName + "E");
                    }
                    else if (ci == cc && ri == cr - 1)
                    {
                        BuildBlock(ctx, BlockKind.Park, new Area(lot.X0, lot.Center.x, lot.Z0, lot.Z1), facing, lotName + "W");
                        BuildBlock(ctx, BlockKind.ConstructionSite, new Area(lot.Center.x, lot.X1, lot.Z0, lot.Z1), facing, lotName + "E");
                    }
                    else
                    {
                        BuildBlock(ctx, deck[dealt++ % deck.Count], lot, facing, lotName);
                    }
                }
            }

            // ---- Street lamps, barrier, spawn points, probes ----------------------------------------
            foreach (Area road in nsRoads)
            {
                PlaceLampsAlong(ctx, road, alongZ: true);
            }

            for (int ri = 0; ri < layout.Rows - 1; ri++)
            {
                PlaceLampsAlong(ctx, new Area(-ringInnerX, ringInnerX, blocks[0, ri].Z1, blocks[0, ri + 1].Z0), alongZ: false);
            }

            PlaceLampsAlong(ctx, ringW, alongZ: true, innerOnly: true);
            PlaceLampsAlong(ctx, ringE, alongZ: true, innerOnly: true);
            PlaceLampsAlong(ctx, ringS, alongZ: false, innerOnly: true);
            PlaceLampsAlong(ctx, ringN, alongZ: false, innerOnly: true);

            // ---- Street trees on the sidewalks, between the lamps ---------------------------------
            var nsGaps = new List<(float z0, float z1)>();
            foreach (Area road in nsRoads)
            {
                nsGaps.Add((road.X0, road.X1));
                PlaceTreesAlong(ctx, road, alongZ: true, skip: ewGaps);
            }

            for (int ri = 0; ri < layout.Rows - 1; ri++)
            {
                PlaceTreesAlong(ctx, new Area(-ringInnerX, ringInnerX, blocks[0, ri].Z1, blocks[0, ri + 1].Z0), alongZ: false, skip: nsGaps);
            }

            PlaceTreesAlong(ctx, ringW, alongZ: true, skip: ewGaps, innerOnly: true);
            PlaceTreesAlong(ctx, ringE, alongZ: true, skip: ewGaps, innerOnly: true);
            PlaceTreesAlong(ctx, ringS, alongZ: false, skip: nsGaps, innerOnly: true);
            PlaceTreesAlong(ctx, ringN, alongZ: false, skip: nsGaps, innerOnly: true);

            BuildBarrier(ctx, edgeX + 0.5f, edgeZ + 0.5f);
            WeedsAlongBarrier(ctx, edgeX, edgeZ);

            if (layout.Vents)
            {
                BuildVents(ctx, blocks, xs, zs, nsRoads);
            }

            result.ExteriorVents = ctx.Vents.gameObject;
            ctx.Vents.gameObject.SetActive(false);

            foreach (Renderer r in ctx.Blocks.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "Mass")
                {
                    result.Masses.Add(r.bounds);
                }
            }

            BuildProbes(ctx, building, edgeX, edgeZ);

            // Parking spots nearest the front door first: the first one is the hero car.
            Vector3 door = building.FrontDoorPosition;
            result.ParkingSpots.Sort((p, q) => Vector3.Distance(p.Position, door).CompareTo(Vector3.Distance(q.Position, door)));

            // Static for batching and occlusion, but not for GI: the sun lights the district directly and
            // baking 3,000 more renderers would turn a one-minute bake into an hour.
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root || t.IsChildOf(ctx.Vents) || t.GetComponent<Light>() != null || t.GetComponent<LightProbeGroup>() != null || t.GetComponent<ReflectionProbe>() != null)
                {
                    continue;
                }

                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticFlags);
            }

            return result;
        }

        // ------------------------------------------------------------------ grid

        private sealed class Context
        {
            public GameAssets A;
            public System.Random Rng;
            public DistrictLayout Layout;
            public Result Result;
            public Transform Roads, Sidewalks, Lots, Blocks, Markings, Props, Lamps, Barrier, Spots, Vents, Probes;
            public float RoadTop;
            /// <summary>Street-tree pits, so manholes are not dug through them.</summary>
            public readonly List<Vector3> TreePositions = new();
        }

        /// <summary>Lot intervals along one axis: the centre lot is the odd one out, corridors between.</summary>
        private static List<(float min, float max)> Intervals(int count, float outer, float centre, float corridor)
        {
            float half = HalfSpan(count, outer, centre, corridor);
            var list = new List<(float, float)>();
            float x = -half;
            for (int i = 0; i < count; i++)
            {
                float size = i == count / 2 ? centre : outer;
                list.Add((x, x + size));
                x += size + corridor;
            }

            return list;
        }

        private static Vector3 FacingToCentre(int dx, int dz)
        {
            if (dx == 0 && dz == 0)
            {
                return Vector3.right;
            }

            return Mathf.Abs(dx) >= Mathf.Abs(dz) ? (dx > 0 ? Vector3.left : Vector3.right) : (dz > 0 ? Vector3.back : Vector3.forward);
        }

        private static List<BlockKind> Deck(System.Random rng)
        {
            var deck = new List<BlockKind>();
            void Add(BlockKind kind, int n) { for (int i = 0; i < n; i++) deck.Add(kind); }
            Add(BlockKind.OfficeTower, 5);
            Add(BlockKind.Apartments, 4);
            Add(BlockKind.RetailStrip, 3);
            Add(BlockKind.Warehouse, 3);
            Add(BlockKind.Park, 1);
            Add(BlockKind.Plaza, 1);
            Add(BlockKind.ConstructionSite, 1);
            Add(BlockKind.Diner, 1);
            Add(BlockKind.GasStation, 1);
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }

            return deck;
        }

        private static void BuildSidewalkRing(Context c, string name, Area lot, float sw)
        {
            Material m = c.A.Concrete;
            float th = c.Layout.SlabThickness;
            Slab(c.Sidewalks, $"Sidewalk_{name}_W", new Area(lot.X0 - sw, lot.X0, lot.Z0 - sw, lot.Z1 + sw), 0f, th, m);
            Slab(c.Sidewalks, $"Sidewalk_{name}_E", new Area(lot.X1, lot.X1 + sw, lot.Z0 - sw, lot.Z1 + sw), 0f, th, m);
            Slab(c.Sidewalks, $"Sidewalk_{name}_S", new Area(lot.X0, lot.X1, lot.Z0 - sw, lot.Z0), 0f, th, m);
            Slab(c.Sidewalks, $"Sidewalk_{name}_N", new Area(lot.X0, lot.X1, lot.Z1, lot.Z1 + sw), 0f, th, m);
        }

        // ------------------------------------------------------------------ roads

        /// <summary>Yellow centre dashes, white edge lines. Dashes skip the intersections in <paramref name="skip"/>.</summary>
        private static void PaintRoad(Context c, Area road, bool alongZ, List<(float z0, float z1)> skip)
        {
            float dash = c.Layout.LaneDashLength, gap = c.Layout.LaneDashGap;
            float y = c.RoadTop + 0.006f;
            Vector3 centre = road.Center;
            float length = alongZ ? road.Depth : road.Width;
            float start = alongZ ? road.Z0 : road.X0;
            int i = 0;
            for (float p = start + gap / 2f; p + dash <= start + length; p += dash + gap)
            {
                float mid = p + dash / 2f;
                if (skip != null && IsInside(mid, skip))
                {
                    continue;
                }

                Vector3 at = alongZ ? new Vector3(centre.x, y, mid) : new Vector3(mid, y, centre.z);
                Vector3 size = alongZ ? new Vector3(0.12f, 0.01f, dash) : new Vector3(dash, 0.01f, 0.12f);
                Decal(c.Markings, $"Dash_{i++}", at, size, c.A.PaintYellow);
            }

            float inset = 0.35f;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 at = alongZ
                    ? new Vector3(centre.x + side * (road.Width / 2f - inset), y, centre.z)
                    : new Vector3(centre.x, y, centre.z + side * (road.Depth / 2f - inset));
                Vector3 size = alongZ ? new Vector3(0.12f, 0.01f, length) : new Vector3(length, 0.01f, 0.12f);
                Decal(c.Markings, "EdgeLine", at, size, c.A.PaintWhite);
            }
        }

        private static bool IsInside(float v, List<(float z0, float z1)> ranges, float margin = 1.5f)
        {
            foreach ((float z0, float z1) in ranges)
            {
                if (v >= z0 - margin && v <= z1 + margin)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Zebra stripes on the four approaches to an intersection.</summary>
        private static void PaintCrosswalks(Context c, Area cross)
        {
            float y = c.RoadTop + 0.007f;
            const int stripes = 5;
            const float stripeW = 0.6f, stripeL = 2.4f;
            float pitch = cross.Width / (stripes + 1);
            for (int i = 0; i < stripes; i++)
            {
                float x = cross.X0 + pitch * (i + 1);
                float z = cross.Z0 + pitch * (i + 1);
                Decal(c.Markings, "Zebra", new Vector3(x, y, cross.Z0 - 1.8f), new Vector3(stripeW, 0.01f, stripeL), c.A.PaintWhite);
                Decal(c.Markings, "Zebra", new Vector3(x, y, cross.Z1 + 1.8f), new Vector3(stripeW, 0.01f, stripeL), c.A.PaintWhite);
                Decal(c.Markings, "Zebra", new Vector3(cross.X0 - 1.8f, y, z), new Vector3(stripeL, 0.01f, stripeW), c.A.PaintWhite);
                Decal(c.Markings, "Zebra", new Vector3(cross.X1 + 1.8f, y, z), new Vector3(stripeL, 0.01f, stripeW), c.A.PaintWhite);
            }
        }

        // ------------------------------------------------------------------ the office block

        /// <summary>
        /// The centre lot around the existing building: a concrete apron, a paver walkway from the front
        /// door to the avenue, the front parking lot (the hero car lives here), a rear yard with a
        /// loading dock, and kerb-side bays on the avenue outside.
        /// </summary>
        private static void BuildOfficeLot(Context c, Area lot, BuildingGenerator.Result building)
        {
            GameAssets a = c.A;
            float th = c.Layout.SlabThickness;
            Bounds fp = building.Footprint;
            float bx = fp.extents.x - 1f, bz = fp.extents.z - 1f; // the walls' outer faces
            float ax = bx + 4f, az = bz + 4f;                       // the apron's outer edge
            Transform lots = c.Lots;

            // Apron: four strips around the building (never under it: the floors are already at y = 0).
            Slab(lots, "Apron_W", new Area(-ax, -bx, -az, az), 0f, th, a.Concrete);
            Slab(lots, "Apron_E", new Area(bx, ax, -az, az), 0f, th, a.Concrete);
            Slab(lots, "Apron_S", new Area(-bx, bx, -az, -bz), 0f, th, a.Concrete);
            Slab(lots, "Apron_N", new Area(-bx, bx, bz, az), 0f, th, a.Concrete);
            // Fill between the apron and the lot's north/south edges.
            Slab(lots, "Lot_Office_N", new Area(-ax, ax, az, lot.Z1), 0f, th, a.Asphalt);
            Slab(lots, "Lot_Office_S", new Area(-ax, ax, lot.Z0, -az), 0f, th, a.Asphalt);

            // Front lot (east, where the door is): asphalt either side of a paver walkway along z = 0.
            float walk = 3f;
            Slab(lots, "Lot_Front_N", new Area(ax, lot.X1, walk, lot.Z1), 0f, th, a.Asphalt);
            Slab(lots, "Lot_Front_S", new Area(ax, lot.X1, lot.Z0, -walk), 0f, th, a.Asphalt);
            Slab(lots, "Walkway", new Area(ax, lot.X1, -walk, walk), 0f, th, a.Pavers);
            Slab(lots, "Lot_Rear", new Area(lot.X0, -ax, lot.Z0, lot.Z1), 0f, th, a.Asphalt);

            // Eight bays facing the building, four each side of the walkway; the nearest is the hero car's.
            float bayX = ax + c.Layout.BayDepth / 2f + 0.5f;
            for (int k = 0; k < 4; k++)
            {
                float z = walk + 1.85f + k * c.Layout.BayWidth;
                Bay(c, new Vector3(bayX, 0f, z), Vector3.left, "Office");
                Bay(c, new Vector3(bayX, 0f, -z), Vector3.left, "Office");
            }

            // Rear yard: a loading dock against the building, two staff bays, dumpsters.
            Piece(c.Props, "LoadingDock", new Vector3(-bx - 1.5f, 0.6f, 0f), new Vector3(3f, 1.2f, 10f), a.Concrete);
            Piece(c.Props, "DockBumperN", new Vector3(-bx - 3.05f, 0.9f, 3f), new Vector3(0.1f, 0.3f, 0.8f), a.Plastic, collider: false);
            Piece(c.Props, "DockBumperS", new Vector3(-bx - 3.05f, 0.9f, -3f), new Vector3(0.1f, 0.3f, 0.8f), a.Plastic, collider: false);
            Bay(c, new Vector3(-ax - c.Layout.BayDepth / 2f - 0.5f, 0f, 12f), Vector3.right, "Rear");
            Bay(c, new Vector3(-ax - c.Layout.BayDepth / 2f - 0.5f, 0f, -12f), Vector3.right, "Rear");
            Prop(c, StreetPropLibrary.Kind.Dumpster, new Vector3(-ax - 3f, 0f, 20f), 90f);
            Prop(c, StreetPropLibrary.Kind.Dumpster, new Vector3(-ax - 3f, 0f, 22f), 90f);
            Prop(c, StreetPropLibrary.Kind.TrashCan, new Vector3(-bx - 1f, 0f, 8f), 0f);

            // Front: flower planters at the door, a bench, box hedges lining the walkway out to the avenue.
            Prop(c, StreetPropLibrary.Kind.FlowerPlanter, new Vector3(ax + 2f, 0f, walk + 0.6f), 0f);
            Prop(c, StreetPropLibrary.Kind.FlowerPlanter, new Vector3(ax + 2f, 0f, -walk - 0.6f), 0f);
            Prop(c, StreetPropLibrary.Kind.Bench, new Vector3(ax + 5f, 0f, walk + 0.5f), 180f);
            Prop(c, StreetPropLibrary.Kind.TrashCan, new Vector3(ax + 3.4f, 0f, walk + 0.5f), 0f);
            for (float x = ax + 8f; x + 1.5f <= lot.X1 - 3f; x += 4.5f)
            {
                Prop(c, StreetPropLibrary.Kind.Hedge, new Vector3(x, 0f, walk + 0.6f), 0f);
                Prop(c, StreetPropLibrary.Kind.Hedge, new Vector3(x, 0f, -walk - 0.6f), 0f);
            }

            // A shrub in a bed at each corner of the building, on the apron.
            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sz in new[] { -1f, 1f })
                {
                    var at = new Vector3(sx * (bx + 1.4f), 0f, sz * (bz + 1.4f));
                    Bed(c, at, 1.0f);
                    Prop(c, StreetPropLibrary.Kind.Shrub, at, 0f);
                }
            }
            for (int i = 0; i < 3; i++)
            {
                Prop(c, StreetPropLibrary.Kind.Bollard, new Vector3(lot.X1 - 0.6f, 0f, -2f + i * 2f), 0f);
            }

            Lamp(c, StreetPropLibrary.Kind.LotLamp, new Vector3(ax + 9f, 0f, lot.Z1 - 6f), Vector3.forward, lit: true);
            Lamp(c, StreetPropLibrary.Kind.LotLamp, new Vector3(ax + 9f, 0f, lot.Z0 + 6f), Vector3.forward, lit: true);
            Lamp(c, StreetPropLibrary.Kind.LotLamp, new Vector3(-ax - 9f, 0f, 0f), Vector3.forward, lit: true);
            Piece(c.Props, "FlagPole", new Vector3(ax + 1.2f, 4f, az - 2f), new Vector3(0.12f, 8f, 0.12f), a.MetalGrey);
            Piece(c.Props, "Flag", new Vector3(ax + 1.9f, 7.4f, az - 2f), new Vector3(1.4f, 0.9f, 0.02f), a.PosterB, collider: false);

            // Kerb-side bays on the avenue right outside (on the road surface), parallel to the kerb.
            float kerbX = lot.X1 + c.Layout.SidewalkWidth;
            foreach (float z in new[] { 9f, 15f, -9f, -15f })
            {
                Bay(c, new Vector3(kerbX + 1.25f, c.RoadTop, z), z > 0f ? Vector3.forward : Vector3.back, "Street", width: 2.5f, depth: 6f);
            }
        }

        // ------------------------------------------------------------------ blocks

        /// <summary>A lot's local frame: origin at the lot centre, +Z toward the front street, +X to the right.</summary>
        private sealed class Lot
        {
            public Context C;
            public Transform Root;
            public Area Area;
            public Vector3 Facing;
            public float HalfRight, HalfForward;
            public string Name;

            public Vector3 World(Vector3 local) => Root.TransformPoint(local);

            public GameObject Piece(string name, Vector3 local, Vector3 size, Material m, bool collider = true)
                => DistrictGenerator.Piece(Root, name, local, size, m, collider);

            public GameObject Prop(StreetPropLibrary.Kind kind, Vector3 local, float yaw)
            {
                GameObject go = StreetPropLibrary.Build(kind, C.A, C.Rng, Root);
                go.transform.localPosition = local;
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                return go;
            }

            public void Lamp(Vector3 local, float yaw)
            {
                Vector3 world = World(local);
                bool lit = world.magnitude <= C.Layout.LitRadius;
                GameObject go = StreetPropLibrary.Build(StreetPropLibrary.Kind.LotLamp, C.A, C.Rng, Root, lit);
                go.transform.localPosition = local;
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                C.Result.LampPositions.Add(world);
            }

            public void Bay(Vector3 local, Vector3 localFacing)
            {
                DistrictGenerator.Bay(C, World(local), Root.TransformDirection(localFacing), Name);
            }

            public float Rand(float min, float max) => (float)(min + C.Rng.NextDouble() * (max - min));

            /// <summary>Box hedges from <paramref name="from"/> to <paramref name="to"/>: 3 m runs with 1.5 m gaps, none within 2 m of an obstacle.</summary>
            public void HedgeRun(Vector3 from, Vector3 to, List<Vector3> obstacles)
            {
                Vector3 dir = (to - from).normalized;
                float total = Vector3.Distance(from, to);
                float yaw = -Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg; // the hedge's length runs along its local X
                for (float d = 1.5f; d + 1.5f <= total; d += 4.5f)
                {
                    Vector3 at = from + dir * d;
                    if (!Clear(at, obstacles, 2.2f))
                    {
                        continue;
                    }

                    Prop(StreetPropLibrary.Kind.Hedge, at, yaw);
                }
            }

            /// <summary>Ivy up a wall of this lot's building: foot at <paramref name="foot"/>, running along <paramref name="along"/> for <paramref name="length"/>, face toward <paramref name="outward"/>.</summary>
            public void Ivy(Vector3 foot, Vector3 along, Vector3 outward, float length, int patches, float maxHeight)
            {
                for (int p = 0; p < patches; p++)
                {
                    float height = Rand(maxHeight * 0.62f, maxHeight);
                    int tiles = 1 + C.Rng.Next(3);
                    float span = tiles * FoliageLibrary.IvyTileWidth * 0.85f;
                    if (span > length - 1f)
                    {
                        tiles = 1;
                        span = FoliageLibrary.IvyTileWidth;
                    }

                    float start = Rand(-length / 2f + 0.5f, length / 2f - 0.5f - span);
                    for (int i = 0; i < tiles; i++)
                    {
                        GameObject ivy = FoliageLibrary.IvyTile(Root, C.A, C.Rng, height * Rand(0.85f, 1.15f), C.Rng.Next(6));
                        ivy.transform.localPosition = foot + along * (start + (i + 0.5f) * FoliageLibrary.IvyTileWidth * 0.85f) + outward * 0.02f;
                        ivy.transform.localRotation = Quaternion.LookRotation(outward, Vector3.up);
                        StreetPropLibrary.MarkExterior(ivy);
                    }
                }
            }

            public GameObject Bed(Vector3 local, float radius) => DistrictGenerator.Bed(C, World(local), radius, Root);
        }

        private static bool Clear(Vector3 at, List<Vector3> obstacles, float radius)
        {
            foreach (Vector3 o in obstacles)
            {
                if (Vector3.Distance(new Vector3(at.x, 0f, at.z), new Vector3(o.x, 0f, o.z)) < radius)
                {
                    return false;
                }
            }

            return true;
        }

        private static void BuildBlock(Context c, BlockKind kind, Area area, Vector3 facing, string name)
        {
            Material ground = kind switch
            {
                BlockKind.Park => c.A.Grass,
                BlockKind.Plaza => c.A.Pavers,
                BlockKind.ConstructionSite => c.A.Dirt,
                _ => c.A.Asphalt,
            };
            Slab(c.Lots, $"Lot_{name}", area, 0f, c.Layout.SlabThickness, ground);

            var rootGo = new GameObject($"{kind}_{name}");
            rootGo.transform.SetParent(c.Blocks, false);
            rootGo.transform.SetPositionAndRotation(area.Center, Quaternion.LookRotation(facing));
            bool alongX = Mathf.Abs(facing.x) > 0.5f;
            var lot = new Lot
            {
                C = c,
                Root = rootGo.transform,
                Area = area,
                Facing = facing,
                HalfRight = (alongX ? area.Depth : area.Width) / 2f,
                HalfForward = (alongX ? area.Width : area.Depth) / 2f,
                Name = name,
            };

            switch (kind)
            {
                case BlockKind.Supermarket: Supermarket(lot); break;
                case BlockKind.GasStation: GasStation(lot); break;
                case BlockKind.OfficeTower: OfficeTower(lot); break;
                case BlockKind.RetailStrip: RetailStrip(lot); break;
                case BlockKind.Warehouse: Warehouse(lot); break;
                case BlockKind.Apartments: Apartments(lot); break;
                case BlockKind.Park: Park(lot); break;
                case BlockKind.Plaza: Plaza(lot); break;
                case BlockKind.ConstructionSite: ConstructionSite(lot); break;
                case BlockKind.Diner: Diner(lot); break;
            }
        }

        private static void Supermarket(Lot l)
        {
            GameAssets a = l.C.A;
            float hr = l.HalfRight, hf = l.HalfForward;
            float w = hr * 2f - 8f, d = 22f, h = 7f;
            float back = -hf + 2f;
            float zc = back + d / 2f, front = back + d;
            l.Piece("Mass", new Vector3(0f, h / 2f, zc), new Vector3(w, h, d), a.Stucco);
            l.Piece("RoofCap", new Vector3(0f, h + 0.25f, zc), new Vector3(w + 0.6f, 0.5f, d + 0.6f), a.MetalDark, collider: false);
            l.Piece("Storefront", new Vector3(0f, 1.9f, front + 0.04f), new Vector3(w - 4f, 3.2f, 0.06f), a.DarkGlass, collider: false);
            l.Piece("Canopy", new Vector3(0f, 4.2f, front + 1.5f), new Vector3(w - 2f, 0.3f, 3f), a.MetalDark, collider: false);
            l.Piece("SignBoard", new Vector3(0f, h - 1.2f, front + 0.12f), new Vector3(w * 0.6f, 1.6f, 0.2f), a.PaintWhite, collider: false);
            l.Piece("SignNeon", new Vector3(0f, h - 1.2f, front + 0.24f), new Vector3(w * 0.55f, 1.0f, 0.06f), a.NeonRed, collider: false);
            for (int i = 0; i < 2; i++)
            {
                l.Piece($"RoofUnit{i}", new Vector3(-w / 4f + i * w / 2f, h + 1.2f, zc - 3f), new Vector3(3f, 1.8f, 2.5f), a.MetalPanel, collider: false);
            }

            // One row of bays against the storefront, an aisle in front of them, the street beyond.
            int bays = 6;
            for (int i = 0; i < bays; i++)
            {
                float x = (i - (bays - 1) / 2f) * l.C.Layout.BayWidth;
                l.Bay(new Vector3(x, 0f, front + 1.5f + l.C.Layout.BayDepth / 2f), Vector3.back);
            }

            for (int i = 0; i < 5; i++)
            {
                l.Prop(StreetPropLibrary.Kind.Bollard, new Vector3(-w / 2f + 4f + i * (w - 8f) / 4f, 0f, front + 0.9f), 0f);
            }

            l.Prop(StreetPropLibrary.Kind.TrashCan, new Vector3(w / 2f - 3f, 0f, front + 0.8f), 0f);
            l.Prop(StreetPropLibrary.Kind.FlowerPlanter, new Vector3(-w / 2f + 1.5f, 0f, front + 0.8f), 0f);
            l.Prop(StreetPropLibrary.Kind.Dumpster, new Vector3(w / 2f + 2.5f, 0f, zc), 90f);
            foreach (float sx in new[] { -1f, 1f })
            {
                l.Bed(new Vector3(sx * (hr - 2.2f), 0f, hf - 2.2f), 1.1f);
                l.Prop(StreetPropLibrary.Kind.Shrub, new Vector3(sx * (hr - 2.2f), 0f, hf - 2.2f), 0f);
            }

            l.Prop(StreetPropLibrary.Kind.Weeds, new Vector3(-w / 2f - 1.5f, 0f, zc - d / 2f + 2f), 0f);
            l.Prop(StreetPropLibrary.Kind.Weeds, new Vector3(w / 2f + 1.2f, 0f, zc - d / 2f + 5f), 0f);
            l.Lamp(new Vector3(-hr + 6f, 0f, hf - 6f), 0f);
            l.Lamp(new Vector3(hr - 6f, 0f, hf - 6f), 0f);
        }

        private static void GasStation(Lot l)
        {
            GameAssets a = l.C.A;
            float hr = l.HalfRight, hf = l.HalfForward;
            float canopyZ = hf - 11f;
            foreach (float x in new[] { -7f, 7f })
            {
                foreach (float z in new[] { canopyZ - 4f, canopyZ + 4f })
                {
                    l.Piece("CanopyColumn", new Vector3(x, 2.5f, z), new Vector3(0.4f, 5f, 0.4f), a.MetalGrey);
                }
            }

            l.Piece("Canopy", new Vector3(0f, 5.25f, canopyZ), new Vector3(18f, 0.5f, 10f), a.PaintWhite, collider: false);
            l.Piece("CanopyFascia", new Vector3(0f, 5.25f, canopyZ + 5.05f), new Vector3(18f, 0.7f, 0.1f), a.NeonRed, collider: false);
            l.Piece("CanopyLens", new Vector3(0f, 4.98f, canopyZ), new Vector3(14f, 0.04f, 6f), a.LampHead, collider: false);
            PointLight(l.C, l.World(new Vector3(0f, 4.6f, canopyZ)), 16f, 5f, l.Root);
            l.Prop(StreetPropLibrary.Kind.PumpIsland, new Vector3(-3.5f, 0f, canopyZ), 0f);
            l.Prop(StreetPropLibrary.Kind.PumpIsland, new Vector3(3.5f, 0f, canopyZ), 0f);

            float kz = -hf + 6f;
            l.Piece("Mass", new Vector3(0f, 1.75f, kz), new Vector3(10f, 3.5f, 6f), a.Brick);
            l.Piece("KioskGlass", new Vector3(0f, 1.6f, kz + 3.04f), new Vector3(8f, 2.2f, 0.06f), a.DarkGlass, collider: false);
            l.Piece("KioskRoof", new Vector3(0f, 3.7f, kz), new Vector3(10.6f, 0.4f, 6.6f), a.MetalDark, collider: false);
            l.Piece("KioskSign", new Vector3(0f, 3.1f, kz + 3.1f), new Vector3(5f, 0.6f, 0.08f), a.NeonBlue, collider: false);
            l.Piece("SignPole", new Vector3(hr - 3f, 4.5f, hf - 3f), new Vector3(0.35f, 9f, 0.35f), a.MetalGrey);
            l.Piece("SignPanel", new Vector3(hr - 3f, 9.5f, hf - 3f), new Vector3(3f, 2f, 0.3f), a.NeonAmber, collider: false);
            l.Prop(StreetPropLibrary.Kind.TrashCan, new Vector3(-6f, 0f, kz + 4f), 0f);
            l.Prop(StreetPropLibrary.Kind.Bollard, new Vector3(-9.5f, 0f, canopyZ - 6f), 0f);
            l.Prop(StreetPropLibrary.Kind.Bollard, new Vector3(9.5f, 0f, canopyZ - 6f), 0f);
            l.Bay(new Vector3(hr - 8f, 0f, kz), Vector3.left);
            l.Bay(new Vector3(hr - 8f, 0f, kz + l.C.Layout.BayWidth), Vector3.left);
            for (int i = -1; i <= 1; i++)
            {
                l.Prop(StreetPropLibrary.Kind.Shrub, new Vector3(i * 3.2f, 0f, kz - 4.3f), 0f);
            }

            l.Prop(StreetPropLibrary.Kind.Weeds, new Vector3(hr - 3f, 0f, hf - 4.2f), 0f);
            l.Prop(StreetPropLibrary.Kind.Weeds, new Vector3(-hr + 1.5f, 0f, -hf + 3f), 0f);
            l.Lamp(new Vector3(-hr + 4f, 0f, hf - 4f), 0f);
        }

        private static void OfficeTower(Lot l)
        {
            GameAssets a = l.C.A;
            float hr = l.HalfRight, hf = l.HalfForward;
            int floors = 3 + l.C.Rng.Next(6);
            float floorH = 3.6f, h = floors * floorH;
            float w = hr * 2f - 8f, d = hf * 2f - 10f, zc = -1f, front = zc + d / 2f;
            Material facade = l.C.Rng.Next(3) switch { 0 => a.Stucco, 1 => a.Brick, _ => a.DarkGlass };
            bool glassTower = facade == a.DarkGlass;
            l.Piece("Mass", new Vector3(0f, h / 2f, zc), new Vector3(w, h, d), facade);
            for (int f = 0; f < floors; f++)
            {
                float y = f * floorH + 1.9f;
                Material band = glassTower ? (l.C.Rng.NextDouble() < 0.35 ? a.LitWindow : a.MetalDark) : (l.C.Rng.NextDouble() < 0.35 ? a.LitWindow : a.DarkGlass);
                l.Piece($"WindowBand_{f}_F", new Vector3(0f, y, front + 0.03f), new Vector3(w - 1f, 1.6f, 0.06f), band, collider: false);
                l.Piece($"WindowBand_{f}_B", new Vector3(0f, y, zc - d / 2f - 0.03f), new Vector3(w - 1f, 1.6f, 0.06f), band, collider: false);
                l.Piece($"WindowBand_{f}_L", new Vector3(-w / 2f - 0.03f, y, zc), new Vector3(0.06f, 1.6f, d - 1f), band, collider: false);
                l.Piece($"WindowBand_{f}_R", new Vector3(w / 2f + 0.03f, y, zc), new Vector3(0.06f, 1.6f, d - 1f), band, collider: false);
            }

            l.Piece("Parapet", new Vector3(0f, h + 0.3f, zc), new Vector3(w + 0.4f, 0.6f, d + 0.4f), a.MetalDark, collider: false);
            l.Piece("RoofUnit", new Vector3(w / 4f, h + 1.4f, zc - d / 4f), new Vector3(4f, 2.2f, 3f), a.MetalPanel, collider: false);
            l.Piece("RoofMast", new Vector3(-w / 4f, h + 4f, zc), new Vector3(0.3f, 8f, 0.3f), a.MetalGrey, collider: false);
            l.Piece("Lobby", new Vector3(0f, 1.6f, front + 0.04f), new Vector3(6f, 2.8f, 0.06f), a.DarkGlass, collider: false);
            l.Piece("EntranceCanopy", new Vector3(0f, 3.3f, front + 1.5f), new Vector3(7f, 0.25f, 3f), a.MetalDark, collider: false);
            l.Piece("SignPlate", new Vector3(0f, h - 1.5f, front + 0.1f), new Vector3(w * 0.4f, 0.9f, 0.14f), l.C.Rng.NextDouble() < 0.5 ? a.NeonBlue : a.NeonAmber, collider: false);
            l.Prop(StreetPropLibrary.Kind.FlowerPlanter, new Vector3(-4.5f, 0f, front + 1f), 0f);
            l.Prop(StreetPropLibrary.Kind.FlowerPlanter, new Vector3(4.5f, 0f, front + 1f), 0f);
            l.Prop(StreetPropLibrary.Kind.Bench, new Vector3(-8f, 0f, front + 1.2f), 0f);
            l.Prop(StreetPropLibrary.Kind.TrashCan, new Vector3(7.5f, 0f, front + 1.2f), 0f);
            if (w / 2f > 13f)
            {
                l.Prop(StreetPropLibrary.Kind.Hedge, new Vector3(-11.5f, 0f, front + 1.2f), 0f);
                l.Prop(StreetPropLibrary.Kind.Hedge, new Vector3(11.5f, 0f, front + 1.2f), 0f);
            }

            l.Prop(StreetPropLibrary.Kind.Tree, new Vector3(-hr + 3f, 0f, hf - 3.5f), 0f);
            l.Prop(StreetPropLibrary.Kind.Tree, new Vector3(hr - 3f, 0f, hf - 3.5f), 0f);
            l.Lamp(new Vector3(0f, 0f, hf - 2f), 0f);
        }

        private static void RetailStrip(Lot l)
        {
            GameAssets a = l.C.A;
            float hr = l.HalfRight, hf = l.HalfForward;
            float w = hr * 2f - 6f, d = 10f, h = 5f;
            float front = hf - 3f, zc = front - d / 2f;
            l.Piece("Mass", new Vector3(0f, h / 2f, zc), new Vector3(w, h, d), l.C.Rng.NextDouble() < 0.5 ? a.Brick : a.Stucco);
            l.Piece("Parapet", new Vector3(0f, h + 0.25f, zc), new Vector3(w + 0.3f, 0.5f, d + 0.3f), a.MetalDark, collider: false);
            int shops = Mathf.Max(2, Mathf.RoundToInt(w / 8f));
            float pitch = w / shops;
            Material[] neon = { a.NeonRed, a.NeonBlue, a.NeonAmber };
            for (int i = 0; i < shops; i++)
            {
                float x = (i - (shops - 1) / 2f) * pitch;
                l.Piece($"Storefront{i}", new Vector3(x, 1.8f, front + 0.03f), new Vector3(pitch - 1.2f, 2.6f, 0.06f), a.DarkGlass, collider: false);
                l.Piece($"ShopDoor{i}", new Vector3(x + pitch / 4f, 1.1f, front + 0.05f), new Vector3(1.1f, 2.2f, 0.03f), a.MetalDark, collider: false);
                GameObject awning = l.Piece($"Awning{i}", new Vector3(x, 3.35f, front + 0.8f), new Vector3(pitch - 1.4f, 0.12f, 1.7f), a.Awning, collider: false);
                awning.transform.localRotation = Quaternion.Euler(-18f, 0f, 0f);
                l.Piece($"Sign{i}", new Vector3(x, 4.3f, front + 0.12f), new Vector3(pitch - 2.2f, 0.7f, 0.12f), neon[i % neon.Length], collider: false);
            }

            l.Piece("RoofUnit", new Vector3(-w / 3f, h + 1.1f, zc - 2f), new Vector3(3f, 1.6f, 2.4f), a.MetalPanel, collider: false);
            l.Piece("BackDoor", new Vector3(w / 3f, 1.1f, zc - d / 2f - 0.05f), new Vector3(1.1f, 2.2f, 0.04f), a.MetalDark, collider: false);
            l.Prop(StreetPropLibrary.Kind.Dumpster, new Vector3(w / 3f + 3f, 0f, zc - d / 2f - 1.5f), 0f);
            l.Prop(StreetPropLibrary.Kind.Dumpster, new Vector3(-w / 3f, 0f, zc - d / 2f - 1.5f), 0f);
            l.Prop(StreetPropLibrary.Kind.NewsBox, new Vector3(-w / 2f + 1f, 0f, front + 1.4f), 0f);
            l.Prop(StreetPropLibrary.Kind.Bench, new Vector3(0f, 0f, front + 1.8f), 180f);
            l.Prop(StreetPropLibrary.Kind.TrashCan, new Vector3(2f, 0f, front + 1.8f), 0f);
            l.Prop(StreetPropLibrary.Kind.FlowerPlanter, new Vector3(w / 2f - 1.5f, 0f, front + 1.4f), 0f);
            l.Prop(StreetPropLibrary.Kind.Weeds, new Vector3(-w / 2f + 1f, 0f, zc - d / 2f - 1.2f), 0f);
            l.Prop(StreetPropLibrary.Kind.Weeds, new Vector3(w / 2f + 1.2f, 0f, zc + 1f), 0f);
            l.Ivy(new Vector3(0f, 0f, zc - d / 2f), Vector3.right, Vector3.back, w, 1, 3.6f);
            l.Lamp(new Vector3(-hr + 4f, 0f, -hf + 4f), 0f);
        }

        private static void Warehouse(Lot l)
        {
            GameAssets a = l.C.A;
            float hr = l.HalfRight, hf = l.HalfForward;
            float w = hr * 2f - 8f, d = hf * 2f - 14f, h = 9f;
            float zc = -2f, front = zc + d / 2f;
            l.Piece("Mass", new Vector3(0f, h / 2f, zc), new Vector3(w, h, d), a.MetalPanel);
            l.Piece("RoofCap", new Vector3(0f, h + 0.2f, zc), new Vector3(w + 0.4f, 0.4f, d + 0.4f), a.MetalDark, collider: false);
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 6f;
                l.Piece($"RollerDoor{i}", new Vector3(x, 2.25f, front + 0.03f), new Vector3(4f, 4.5f, 0.06f), a.MetalDark, collider: false);
                l.Piece($"DoorStripe{i}", new Vector3(x, 0.3f, front + 0.07f), new Vector3(4f, 0.2f, 0.02f), a.PaintYellow, collider: false);
            }

            l.Piece("SiteOffice", new Vector3(w / 2f - 4f, 1.75f, front + 2f), new Vector3(8f, 3.5f, 4f), a.Stucco);
            l.Piece("SiteOfficeGlass", new Vector3(w / 2f - 4f, 1.7f, front + 4.04f), new Vector3(6f, 1.4f, 0.06f), a.DarkGlass, collider: false);
            l.Piece("NameSign", new Vector3(0f, h - 1.2f, front + 0.08f), new Vector3(10f, 1.2f, 0.1f), a.PaintWhite, collider: false);
            l.Prop(StreetPropLibrary.Kind.Trailer, new Vector3(-hr + 5f, 0f, front + 2.5f), 0f); // 12 m long: nose stays inside the lot
            l.Prop(StreetPropLibrary.Kind.Dumpster, new Vector3(-w / 2f - 2f, 0f, zc), 90f);
            l.Prop(StreetPropLibrary.Kind.Dumpster, new Vector3(-w / 2f - 2f, 0f, zc + 2.5f), 90f);
            for (int i = 0; i < 4; i++)
            {
                l.Prop(StreetPropLibrary.Kind.Bollard, new Vector3(-6f + i * 4f, 0f, hf - 1f), 0f);
            }

            l.Prop(StreetPropLibrary.Kind.MaterialStack, new Vector3(hr - 4f, 0f, front + 9f), 20f);
            // Nobody weeds a warehouse yard.
            for (int i = 0; i < 6; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * (w / 2f + l.Rand(0.8f, 1.6f));
                l.Prop(StreetPropLibrary.Kind.Weeds, new Vector3(x, 0f, zc + l.Rand(-d / 2f + 1f, d / 2f - 1f)), 0f);
            }

            l.Prop(StreetPropLibrary.Kind.Weeds, new Vector3(l.Rand(-w / 3f, w / 3f), 0f, zc - d / 2f - 1.2f), 0f);
            l.Lamp(new Vector3(0f, 0f, hf - 3f), 0f);
        }

        private static void Apartments(Lot l)
        {
            GameAssets a = l.C.A;
            float hr = l.HalfRight, hf = l.HalfForward;
            float w = hr * 2f - 12f, d = hf * 2f - 14f;
            int floors = 5;
            float floorH = 3f, h = floors * floorH;
            float zc = -1f, front = zc + d / 2f;
            l.Piece("Mass", new Vector3(0f, h / 2f, zc), new Vector3(w, h, d), a.Brick);
            l.Piece("Parapet", new Vector3(0f, h + 0.25f, zc), new Vector3(w + 0.3f, 0.5f, d + 0.3f), a.Concrete, collider: false);
            for (int f = 0; f < floors; f++)
            {
                float y = f * floorH;
                Material band = l.C.Rng.NextDouble() < 0.4 ? a.LitWindow : a.DarkGlass;
                l.Piece($"WindowBand_{f}_B", new Vector3(0f, y + 1.7f, zc - d / 2f - 0.03f), new Vector3(w - 1f, 1.3f, 0.06f), band, collider: false);
                l.Piece($"WindowBand_{f}_L", new Vector3(-w / 2f - 0.03f, y + 1.7f, zc), new Vector3(0.06f, 1.3f, d - 1f), band, collider: false);
                l.Piece($"WindowBand_{f}_R", new Vector3(w / 2f + 0.03f, y + 1.7f, zc), new Vector3(0.06f, 1.3f, d - 1f), band, collider: false);
                if (f == 0)
                {
                    l.Piece("Entrance", new Vector3(0f, 1.2f, front + 0.04f), new Vector3(3f, 2.4f, 0.06f), a.DarkGlass, collider: false);
                    l.Piece("EntranceCanopy", new Vector3(0f, 2.7f, front + 0.8f), new Vector3(4f, 0.2f, 1.6f), a.MetalDark, collider: false);
                    continue;
                }

                for (int b = -1; b <= 1; b++)
                {
                    float x = b * w / 3f;
                    l.Piece($"Balcony_{f}_{b + 1}", new Vector3(x, y + 0.08f, front + 0.6f), new Vector3(2.6f, 0.16f, 1.2f), a.Concrete, collider: false);
                    l.Piece($"Railing_{f}_{b + 1}", new Vector3(x, y + 0.6f, front + 1.18f), new Vector3(2.6f, 0.9f, 0.04f), a.MetalDark, collider: false);
                    l.Piece($"BalconyDoor_{f}_{b + 1}", new Vector3(x, y + 1.2f, front + 0.03f), new Vector3(1.6f, 2.2f, 0.06f), band, collider: false);
                }
            }

            l.Piece("RoofUnit", new Vector3(0f, h + 0.9f, zc - 2f), new Vector3(2.5f, 1.3f, 2f), a.MetalPanel, collider: false);
            l.Prop(StreetPropLibrary.Kind.Tree, new Vector3(-w / 2f - 3f, 0f, front + 1f), 0f);
            l.Prop(StreetPropLibrary.Kind.Tree, new Vector3(w / 2f + 3f, 0f, front + 1f), 0f);
            l.Prop(StreetPropLibrary.Kind.Bench, new Vector3(-3.5f, 0f, front + 2.5f), 180f);
            l.Prop(StreetPropLibrary.Kind.FlowerPlanter, new Vector3(3.5f, 0f, front + 2.5f), 0f);
            // Hedges either side of the entrance, ivy up the blank side and back walls.
            var clutter = new List<Vector3> { new(-3.5f, 0f, front + 2.5f), new(3.5f, 0f, front + 2.5f), new(0f, 0f, front + 1f) };
            l.HedgeRun(new Vector3(-w / 2f + 0.5f, 0f, front + 1.4f), new Vector3(-2.6f, 0f, front + 1.4f), clutter);
            l.HedgeRun(new Vector3(2.6f, 0f, front + 1.4f), new Vector3(w / 2f - 0.5f, 0f, front + 1.4f), clutter);
            l.Ivy(new Vector3(-w / 2f, 0f, zc), Vector3.forward, Vector3.left, d, 2, 5.4f);
            l.Ivy(new Vector3(w / 2f, 0f, zc), Vector3.back, Vector3.right, d, 2, 5.4f);
            l.Ivy(new Vector3(0f, 0f, zc - d / 2f), Vector3.right, Vector3.back, w, 2, 4.4f);
            l.Prop(StreetPropLibrary.Kind.Dumpster, new Vector3(w / 2f + 2f, 0f, zc - d / 2f), 90f);
            l.Lamp(new Vector3(0f, 0f, hf - 2f), 0f);
        }

        private static void Park(Lot l)
        {
            GameAssets a = l.C.A;
            float hr = l.HalfRight, hf = l.HalfForward;
            l.Piece("PathN", new Vector3(0f, 0.01f, 0f), new Vector3(4f, 0.02f, hf * 2f - 2f), a.Pavers, collider: false);
            l.Piece("PathE", new Vector3(0f, 0.012f, 0f), new Vector3(hr * 2f - 2f, 0.02f, 4f), a.Pavers, collider: false);

            // Benches, bin and lamp first: the hedges leave gaps for them.
            var furniture = new List<Vector3> { new(-2.6f, 0f, hf / 2f), new(2.6f, 0f, -hf / 2f), new(hr / 2f, 0f, 2.6f), new(-hr / 2f, 0f, -2.6f), new(2.6f, 0f, 2.6f), new(2.6f, 0f, -2.6f) };
            l.Prop(StreetPropLibrary.Kind.Bench, furniture[0], 90f);
            l.Prop(StreetPropLibrary.Kind.Bench, furniture[1], -90f);
            l.Prop(StreetPropLibrary.Kind.Bench, furniture[2], 180f);
            l.Prop(StreetPropLibrary.Kind.Bench, furniture[3], 0f);
            l.Prop(StreetPropLibrary.Kind.TrashCan, furniture[4], 0f);
            l.Lamp(furniture[5], 0f);

            // Flower beds in the four corners of the crossing.
            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sz in new[] { -1f, 1f })
                {
                    var bed = new Vector3(sx * 4.2f, 0f, sz * 4.2f);
                    l.Bed(bed, 1.5f);
                    for (int i = 0; i < 3; i++)
                    {
                        float ang = i * 120f + l.Rand(0f, 120f);
                        l.Prop(StreetPropLibrary.Kind.FloweringShrub, bed + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * 0.55f, ang);
                    }

                    furniture.Add(bed);
                }
            }

            // Hedges along both sides of both paths, broken at the benches and the beds.
            const float hedgeLine = 2.9f;
            l.HedgeRun(new Vector3(-hedgeLine, 0f, -hf + 2.5f), new Vector3(-hedgeLine, 0f, hf - 2.5f), furniture);
            l.HedgeRun(new Vector3(hedgeLine, 0f, -hf + 2.5f), new Vector3(hedgeLine, 0f, hf - 2.5f), furniture);
            l.HedgeRun(new Vector3(-hr + 2.5f, 0f, -hedgeLine), new Vector3(hr - 2.5f, 0f, -hedgeLine), furniture);
            l.HedgeRun(new Vector3(-hr + 2.5f, 0f, hedgeLine), new Vector3(hr - 2.5f, 0f, hedgeLine), furniture);

            // Trees: mostly broadleaf and birch, a conifer or two, each in its own litter; then a few loose shrubs.
            var trees = new List<Vector3>();
            int count = 9 + l.C.Rng.Next(5);
            for (int i = 0; i < count; i++)
            {
                float x = l.Rand(-hr + 3f, hr - 3f), z = l.Rand(-hf + 3f, hf - 3f);
                var at = new Vector3(x, 0f, z);
                if (Mathf.Abs(x) < 4.2f || Mathf.Abs(z) < 4.2f || !Clear(at, trees, 4.5f) || !Clear(at, furniture, 2.5f))
                {
                    continue; // keep the paths, the beds and the other trees clear
                }

                l.Prop(l.C.Rng.NextDouble() < 0.18 ? StreetPropLibrary.Kind.Conifer : StreetPropLibrary.Kind.Tree, at, l.Rand(0f, 360f));
                trees.Add(at);
            }

            for (int i = 0; i < 5; i++)
            {
                var at = new Vector3(l.Rand(-hr + 2f, hr - 2f), 0f, l.Rand(-hf + 2f, hf - 2f));
                if (Mathf.Abs(at.x) < 4.5f || Mathf.Abs(at.z) < 4.5f || !Clear(at, trees, 2.5f) || !Clear(at, furniture, 2.5f))
                {
                    continue;
                }

                l.Prop(StreetPropLibrary.Kind.Shrub, at, l.Rand(0f, 360f));
                furniture.Add(at);
            }

            // The lawn: everywhere the paths and the beds are not.
            GameObject lawn = FoliageLibrary.Lawn(l.Root, a, $"Lawn_{l.Name}", new Rect(-hr + 0.5f, -hf + 0.5f, hr * 2f - 1f, hf * 2f - 1f),
                p => Mathf.Abs(p.x) < 2.35f || Mathf.Abs(p.y) < 2.35f || (Mathf.Abs(Mathf.Abs(p.x) - 4.2f) < 1.6f && Mathf.Abs(Mathf.Abs(p.y) - 4.2f) < 1.6f), 1.15f);
            StreetPropLibrary.MarkExterior(lawn);
        }

        private static void Plaza(Lot l)
        {
            float hr = l.HalfRight, hf = l.HalfForward;
            l.Prop(StreetPropLibrary.Kind.Fountain, Vector3.zero, 0f);
            for (int i = 0; i < 6; i++)
            {
                float ang = i * Mathf.PI * 2f / 6f;
                l.Prop(i % 2 == 0 ? StreetPropLibrary.Kind.FlowerPlanter : StreetPropLibrary.Kind.Planter, new Vector3(Mathf.Cos(ang) * 8f, 0f, Mathf.Sin(ang) * 8f), -i * 60f);
            }

            l.Prop(StreetPropLibrary.Kind.Conifer, new Vector3(-hr + 3.5f, 0f, 1f), 0f);
            l.Prop(StreetPropLibrary.Kind.Conifer, new Vector3(hr - 3.5f, 0f, 1f), 0f);

            int bollards = Mathf.Max(4, Mathf.RoundToInt(hr * 2f / 4f));
            for (int i = 0; i < bollards; i++)
            {
                l.Prop(StreetPropLibrary.Kind.Bollard, new Vector3(-hr + 2f + i * (hr * 2f - 4f) / (bollards - 1), 0f, hf - 1f), 0f);
            }

            l.Prop(StreetPropLibrary.Kind.Bench, new Vector3(-5f, 0f, 5f), 135f);
            l.Prop(StreetPropLibrary.Kind.Bench, new Vector3(5f, 0f, 5f), -135f);
            l.Prop(StreetPropLibrary.Kind.Bench, new Vector3(-5f, 0f, -5f), 45f);
            l.Prop(StreetPropLibrary.Kind.Bench, new Vector3(5f, 0f, -5f), -45f);
            l.Prop(StreetPropLibrary.Kind.Tree, new Vector3(-hr + 4f, 0f, -hf + 4f), 0f);
            l.Prop(StreetPropLibrary.Kind.Tree, new Vector3(hr - 4f, 0f, -hf + 4f), 0f);
            l.Prop(StreetPropLibrary.Kind.TrashCan, new Vector3(0f, 0f, hf - 3f), 0f);
            l.Lamp(new Vector3(-hr + 5f, 0f, hf - 5f), 0f);
            l.Lamp(new Vector3(hr - 5f, 0f, hf - 5f), 0f);
        }

        private static void ConstructionSite(Lot l)
        {
            GameAssets a = l.C.A;
            float hr = l.HalfRight, hf = l.HalfForward;
            float zc = -2f;
            foreach (float x in new[] { -6f, 0f, 6f })
            {
                foreach (float z in new[] { zc - 4f, zc + 4f })
                {
                    l.Piece("Column", new Vector3(x, 3.5f, z), new Vector3(0.5f, 7f, 0.5f), a.Concrete);
                }
            }

            l.Piece("Mass", new Vector3(0f, 3.65f, zc), new Vector3(14f, 0.3f, 10f), a.Concrete);
            l.Piece("RoofSlab", new Vector3(0f, 7.15f, zc), new Vector3(14f, 0.3f, 10f), a.Concrete);
            l.Piece("Rebar", new Vector3(-6f, 8.2f, zc - 4f), new Vector3(0.08f, 2f, 0.08f), a.MetalDark, collider: false);
            l.Piece("Rebar2", new Vector3(6f, 8.2f, zc + 4f), new Vector3(0.08f, 2f, 0.08f), a.MetalDark, collider: false);
            for (int i = 0; i < 5; i++)
            {
                l.Prop(StreetPropLibrary.Kind.JerseyBarrier, new Vector3(-6.4f + i * 3.2f, 0f, hf - 2.5f), 0f);
            }

            l.Prop(StreetPropLibrary.Kind.MaterialStack, new Vector3(-hr + 4f, 0f, 3f), 10f);
            l.Prop(StreetPropLibrary.Kind.MaterialStack, new Vector3(-hr + 4f, 0f, 6f), -5f);
            l.Prop(StreetPropLibrary.Kind.MaterialStack, new Vector3(hr - 5f, 0f, -hf + 5f), 30f);
            l.Prop(StreetPropLibrary.Kind.PortaLoo, new Vector3(hr - 3f, 0f, hf - 6f), -90f);
            l.Prop(StreetPropLibrary.Kind.PortaLoo, new Vector3(hr - 3f, 0f, hf - 7.5f), -90f);
            l.Prop(StreetPropLibrary.Kind.Dumpster, new Vector3(hr - 4f, 0f, 2f), 0f);
            for (int i = 0; i < 6; i++)
            {
                l.Prop(StreetPropLibrary.Kind.TrafficCone, new Vector3(l.Rand(-hr + 2f, hr - 2f), 0f, l.Rand(hf - 8f, hf - 3f)), 0f);
            }

            for (int i = 0; i < 7; i++)
            {
                var at = new Vector3(l.Rand(-hr + 1.5f, hr - 1.5f), 0f, l.Rand(-hf + 1.5f, hf - 1.5f));
                if (Mathf.Abs(at.x) < 8f && Mathf.Abs(at.z - zc) < 6.5f)
                {
                    continue; // not under the slab
                }

                l.Prop(StreetPropLibrary.Kind.Weeds, at, 0f);
            }

            l.Lamp(new Vector3(-hr + 3f, 0f, hf - 3f), 0f);
        }

        private static void Diner(Lot l)
        {
            GameAssets a = l.C.A;
            float hr = l.HalfRight, hf = l.HalfForward;
            float w = 14f, d = 9f, h = 4f, zc = -2f, front = zc + d / 2f;
            l.Piece("Mass", new Vector3(0f, h / 2f, zc), new Vector3(w, h, d), a.Stucco);
            l.Piece("RoofBand", new Vector3(0f, h + 0.2f, zc), new Vector3(w + 0.5f, 0.4f, d + 0.5f), a.MetalDark, collider: false);
            l.Piece("Windows_F", new Vector3(0f, 1.8f, front + 0.04f), new Vector3(w - 2f, 1.4f, 0.06f), a.LitWindow, collider: false);
            l.Piece("Windows_L", new Vector3(-w / 2f - 0.04f, 1.8f, zc), new Vector3(0.06f, 1.4f, d - 2f), a.LitWindow, collider: false);
            l.Piece("Windows_R", new Vector3(w / 2f + 0.04f, 1.8f, zc), new Vector3(0.06f, 1.4f, d - 2f), a.LitWindow, collider: false);
            l.Piece("Door", new Vector3(w / 2f - 2.5f, 1.05f, front + 0.06f), new Vector3(1.1f, 2.1f, 0.03f), a.MetalDark, collider: false);
            l.Piece("SignPost", new Vector3(0f, h + 1.2f, zc), new Vector3(0.3f, 2.4f, 0.3f), a.MetalGrey, collider: false);
            l.Piece("SignNeon", new Vector3(0f, h + 2.6f, zc), new Vector3(8f, 1.4f, 0.25f), a.NeonRed, collider: false);
            GameObject awning = l.Piece("Awning", new Vector3(0f, 3.0f, front + 0.9f), new Vector3(w - 1f, 0.12f, 1.9f), a.Awning, collider: false);
            awning.transform.localRotation = Quaternion.Euler(-15f, 0f, 0f);
            l.Prop(StreetPropLibrary.Kind.Bench, new Vector3(-4f, 0f, front + 2f), 180f);
            l.Prop(StreetPropLibrary.Kind.Bench, new Vector3(0f, 0f, front + 2f), 180f);
            l.Prop(StreetPropLibrary.Kind.TrashCan, new Vector3(3f, 0f, front + 2f), 0f);
            l.Prop(StreetPropLibrary.Kind.NewsBox, new Vector3(4f, 0f, front + 2f), 0f);
            l.Prop(StreetPropLibrary.Kind.Dumpster, new Vector3(-w / 2f - 2f, 0f, zc - 2f), 90f);
            l.Prop(StreetPropLibrary.Kind.Tree, new Vector3(hr - 3f, 0f, hf - 3f), 0f);
            l.Prop(StreetPropLibrary.Kind.Tree, new Vector3(-hr + 3f, 0f, hf - 3f), 0f);
            l.Prop(StreetPropLibrary.Kind.FlowerPlanter, new Vector3(-6.5f, 0f, front + 2f), 0f);
            l.Prop(StreetPropLibrary.Kind.Weeds, new Vector3(w / 2f + 1.5f, 0f, zc - 3f), 0f);
            l.Ivy(new Vector3(0f, 0f, zc - d / 2f), Vector3.right, Vector3.back, w, 1, 3.1f);
            l.Lamp(new Vector3(hr - 6f, 0f, hf - 7f), 0f);
        }

        // ------------------------------------------------------------------ furniture

        private static void PlaceLampsAlong(Context c, Area road, bool alongZ, bool innerOnly = false)
        {
            float spacing = c.Layout.LampSpacing;
            float length = alongZ ? road.Depth : road.Width;
            float start = alongZ ? road.Z0 : road.X0;
            int count = Mathf.Max(1, Mathf.FloorToInt(length / spacing));
            float pitch = length / count;
            for (int i = 0; i < count; i++)
            {
                float p = start + pitch * (i + 0.5f);
                // Alternate sides so the street is lit from both kerbs without doubling the count.
                int side = i % 2 == 0 ? -1 : 1;
                if (innerOnly)
                {
                    // Ring road: lamps only on the inner kerb (the outer one is the barrier).
                    side = alongZ ? (road.Center.x > 0f ? -1 : 1) : (road.Center.z > 0f ? -1 : 1);
                }

                Vector3 pos, facing;
                if (alongZ)
                {
                    float x = side < 0 ? road.X0 - 0.7f : road.X1 + 0.7f;
                    pos = new Vector3(x, 0f, p);
                    facing = side < 0 ? Vector3.right : Vector3.left;
                }
                else
                {
                    float z = side < 0 ? road.Z0 - 0.7f : road.Z1 + 0.7f;
                    pos = new Vector3(p, 0f, z);
                    facing = side < 0 ? Vector3.forward : Vector3.back;
                }

                Lamp(c, StreetPropLibrary.Kind.StreetLamp, pos, facing, lit: pos.magnitude <= c.Layout.LitRadius);
            }
        }

        /// <summary>
        /// Street trees every twelve metres along a road's sidewalks, clear of the intersections in
        /// <paramref name="skip"/>, of the lamps, and of the office's front walk. Their crowns are pruned
        /// above the road (see FoliageLibrary.TreeKind.Street), so a car never drives into leaves.
        /// </summary>
        private static void PlaceTreesAlong(Context c, Area road, bool alongZ, List<(float z0, float z1)> skip, bool innerOnly = false)
        {
            const float spacing = 12f;
            float length = alongZ ? road.Depth : road.Width;
            float start = alongZ ? road.Z0 : road.X0;
            int count = Mathf.FloorToInt(length / spacing);
            float slack = (length - count * spacing) / 2f;
            for (int i = 0; i < count; i++)
            {
                float p = start + slack + spacing * (i + 0.5f);
                if (IsInside(p, skip, 7f))
                {
                    continue;
                }

                foreach (int side in new[] { -1, 1 })
                {
                    if (innerOnly)
                    {
                        int inner = alongZ ? (road.Center.x > 0f ? -1 : 1) : (road.Center.z > 0f ? -1 : 1);
                        if (side != inner) continue;
                    }

                    Vector3 pos = alongZ
                        ? new Vector3(side < 0 ? road.X0 - 1.1f : road.X1 + 1.1f, 0f, p)
                        : new Vector3(p, 0f, side < 0 ? road.Z0 - 1.1f : road.Z1 + 1.1f);
                    if (alongZ && Mathf.Abs(pos.z) < 7f && Mathf.Abs(road.Center.x) < c.Layout.CentreLotWidth)
                    {
                        continue; // the office's walkway crosses here, and its kerb-side bays are beside it
                    }

                    if (!Clear(pos, c.Result.LampPositions, 2.5f))
                    {
                        continue;
                    }

                    Prop(c, StreetPropLibrary.Kind.StreetTree, pos, (float)(c.Rng.NextDouble() * 360.0));
                    c.TreePositions.Add(pos);
                }
            }
        }

        /// <summary>Weeds along the foot of the barrier: the edge of town nobody sweeps.</summary>
        private static void WeedsAlongBarrier(Context c, float edgeX, float edgeZ)
        {
            for (float x = -edgeX + 8f; x < edgeX - 8f; x += 14f)
            {
                Prop(c, StreetPropLibrary.Kind.Weeds, new Vector3(x + (float)(c.Rng.NextDouble() * 4.0 - 2.0), 0f, -edgeZ + 1.0f), 0f);
                Prop(c, StreetPropLibrary.Kind.Weeds, new Vector3(x + (float)(c.Rng.NextDouble() * 4.0 - 2.0), 0f, edgeZ - 1.0f), 0f);
            }

            for (float z = -edgeZ + 8f; z < edgeZ - 8f; z += 14f)
            {
                Prop(c, StreetPropLibrary.Kind.Weeds, new Vector3(-edgeX + 1.0f, 0f, z + (float)(c.Rng.NextDouble() * 4.0 - 2.0)), 0f);
                Prop(c, StreetPropLibrary.Kind.Weeds, new Vector3(edgeX - 1.0f, 0f, z + (float)(c.Rng.NextDouble() * 4.0 - 2.0)), 0f);
            }
        }

        /// <summary>A round mulch bed on the ground: where a shrub or a tree is planted through the paving.</summary>
        private static GameObject Bed(Context c, Vector3 position, float radius, Transform parent = null)
        {
            GameObject go = PrefabFactory.Primitive(PrimitiveType.Cylinder, "Bed", parent != null ? parent : c.Props, Vector3.zero, new Vector3(radius * 2f, 0.012f, radius * 2f), c.A.Mulch, collider: false);
            go.transform.position = position + Vector3.up * 0.012f;
            go.transform.rotation = Quaternion.identity;
            go.GetComponent<Renderer>().renderingLayerMask = ExteriorRenderingLayer | 1u;
            go.layer = Layers.EnvironmentIndex;
            return go;
        }

        private static void Lamp(Context c, StreetPropLibrary.Kind kind, Vector3 position, Vector3 facing, bool lit)
        {
            GameObject go = StreetPropLibrary.Build(kind, c.A, c.Rng, c.Lamps, lit);
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing));
            c.Result.LampPositions.Add(position);
        }

        private static void PointLight(Context c, Vector3 position, float range, float intensity, Transform parent)
        {
            var go = new GameObject("CanopyLight");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.intensity = intensity;
            light.color = new Color(1f, 0.95f, 0.85f);
            light.shadows = LightShadows.None;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            go.layer = Layers.PlayerIndex;
        }

        /// <summary>Concrete jersey wall with a fence on top all the way round: cars stay in the district.</summary>
        private static void BuildBarrier(Context c, float x, float z)
        {
            GameAssets a = c.A;
            float bh = c.Layout.BarrierHeight, fh = c.Layout.FenceHeight;
            float lengthX = x * 2f + 1f, lengthZ = z * 2f + 1f;
            void Side(string name, Vector3 centre, Vector3 size)
            {
                Piece(c.Barrier, $"Barrier_{name}", centre + Vector3.up * (bh / 2f - 0.2f), size + Vector3.up * bh, a.Concrete);
                Piece(c.Barrier, $"Fence_{name}", centre + Vector3.up * (bh - 0.2f + fh / 2f), new Vector3(size.x > 0.9f ? size.x : 0.04f, fh, size.z > 0.9f ? size.z : 0.04f), a.Fence, collider: true);
                float length = Mathf.Max(size.x, size.z);
                bool alongX = size.x > size.z;
                int posts = Mathf.FloorToInt(length / 8f);
                for (int i = 0; i <= posts; i++)
                {
                    float p = -length / 2f + i * (length / posts);
                    Vector3 at = centre + (alongX ? new Vector3(p, 0f, 0f) : new Vector3(0f, 0f, p));
                    Piece(c.Barrier, $"Post_{name}_{i}", at + Vector3.up * (bh - 0.2f + fh / 2f), new Vector3(0.08f, fh, 0.08f), a.MetalDark, collider: false);
                }
            }

            Side("W", new Vector3(-x, 0f, 0f), new Vector3(0.6f, 0f, lengthZ));
            Side("E", new Vector3(x, 0f, 0f), new Vector3(0.6f, 0f, lengthZ));
            Side("S", new Vector3(0f, 0f, -z), new Vector3(lengthX, 0f, 0.6f));
            Side("N", new Vector3(0f, 0f, z), new Vector3(lengthX, 0f, 0.6f));
        }

        /// <summary>
        /// Outdoor spawn points: a manhole on the front sidewalk of every outer block, four around the
        /// office, and storm drains in the avenue gutters beside it. The group starts inactive; the
        /// front door activates it.
        /// </summary>
        private static void BuildVents(Context c, Area[,] blocks, List<(float min, float max)> xs, List<(float min, float max)> zs, List<Area> nsRoads)
        {
            int cc = c.Layout.Columns / 2, cr = c.Layout.Rows / 2;
            float sw = c.Layout.SidewalkWidth;
            for (int ci = 0; ci < c.Layout.Columns; ci++)
            {
                for (int ri = 0; ri < c.Layout.Rows; ri++)
                {
                    if (ci == cc && ri == cr)
                    {
                        continue;
                    }

                    var lot = new Area(xs[ci].min, xs[ci].max, zs[ri].min, zs[ri].max);
                    Vector3 facing = FacingToCentre(ci - cc, ri - cr);
                    Vector3 along = Vector3.Cross(Vector3.up, facing);
                    float halfForward = Mathf.Abs(facing.x) > 0.5f ? lot.Width / 2f : lot.Depth / 2f;
                    float halfRight = Mathf.Abs(facing.x) > 0.5f ? lot.Depth / 2f : lot.Width / 2f;
                    Vector3 pos = Vector3.zero;
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        pos = lot.Center + facing * (halfForward + sw / 2f) + along * (float)((c.Rng.NextDouble() * 2f - 1f) * (halfRight - 5f));
                        if (Clear(pos, c.TreePositions, 2.6f))
                        {
                            break; // not through a tree pit
                        }
                    }

                    StreetPropLibrary.BuildManhole(c.A, c.Vents, pos, along);
                }
            }

            Area office = new(xs[cc].min, xs[cc].max, zs[cr].min, zs[cr].max);
            StreetPropLibrary.BuildManhole(c.A, c.Vents, new Vector3(office.X1 - 7f, 0f, office.Z1 - 8f), Vector3.forward);
            StreetPropLibrary.BuildManhole(c.A, c.Vents, new Vector3(office.X1 - 7f, 0f, office.Z0 + 8f), Vector3.back);
            StreetPropLibrary.BuildManhole(c.A, c.Vents, new Vector3(office.X0 + 7f, 0f, 15f), Vector3.forward);
            StreetPropLibrary.BuildManhole(c.A, c.Vents, new Vector3(office.X0 + 7f, 0f, -15f), Vector3.back);

            // Storm drains in the gutters of the two avenues flanking the office block.
            foreach (Area road in nsRoads)
            {
                if (Mathf.Abs(road.Center.x) > c.Layout.CentreLotWidth)
                {
                    continue;
                }

                float x = road.Center.x < 0f ? road.X1 - 1.2f : road.X0 + 1.2f;
                StreetPropLibrary.BuildStormDrain(c.A, c.Vents, new Vector3(x, c.RoadTop, 25f), Vector3.forward);
                StreetPropLibrary.BuildStormDrain(c.A, c.Vents, new Vector3(x, c.RoadTop, -25f), Vector3.back);
            }
        }

        /// <summary>
        /// A sparse light-probe grid so zombies and cars outdoors pick up the sky rather than the
        /// office's baked bounce, and one big reflection probe so car paint has a dusk sky to mirror.
        /// </summary>
        private static void BuildProbes(Context c, BuildingGenerator.Result building, float edgeX, float edgeZ)
        {
            var group = c.Probes.gameObject.AddComponent<LightProbeGroup>();
            var positions = new List<Vector3>();
            float spacing = c.Layout.ProbeSpacing;
            Bounds office = building.Footprint;
            bool Blocked(Vector3 p)
            {
                if (office.Contains(new Vector3(p.x, office.center.y, p.z)))
                {
                    return true;
                }

                foreach (Bounds b in c.Result.Masses)
                {
                    if (b.Contains(new Vector3(p.x, Mathf.Clamp(p.y, b.min.y, b.max.y), p.z)))
                    {
                        return true;
                    }
                }

                return false;
            }

            for (float x = -edgeX + spacing / 2f; x < edgeX; x += spacing)
            {
                for (float z = -edgeZ + spacing / 2f; z < edgeZ; z += spacing)
                {
                    var low = new Vector3(x, 1.5f, z);
                    if (!Blocked(low))
                    {
                        positions.Add(low);
                    }

                    bool coarse = Mathf.RoundToInt((x + edgeX - spacing / 2f) / spacing) % 2 == 0 && Mathf.RoundToInt((z + edgeZ - spacing / 2f) / spacing) % 2 == 0;
                    var high = new Vector3(x, 6f, z);
                    if (coarse && !Blocked(high))
                    {
                        positions.Add(high);
                    }
                }
            }

            group.probePositions = positions.ToArray();

            var probeGo = new GameObject("DistrictReflection");
            probeGo.transform.SetParent(c.Probes, false);
            probeGo.transform.position = new Vector3(0f, 20f, 0f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.size = new Vector3(edgeX * 2f + 20f, 60f, edgeZ * 2f + 20f);
            probe.boxProjection = false;
            probe.resolution = 128;
            probe.hdr = true;
            probe.cullingMask = (1 << Layers.EnvironmentIndex) | 1;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.importance = 0; // the rooms' own probes win indoors
        }

        // ------------------------------------------------------------------ primitives

        /// <summary>A painted parking bay: two white lines and a registered spot between them.</summary>
        private static void Bay(Context c, Vector3 centre, Vector3 facing, string lot, float width = 0f, float depth = 0f)
        {
            width = width > 0f ? width : c.Layout.BayWidth;
            depth = depth > 0f ? depth : c.Layout.BayDepth;
            facing.y = 0f;
            facing.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, facing);
            float y = centre.y + 0.006f;
            bool alongX = Mathf.Abs(facing.x) > 0.5f;
            Vector3 lineSize = alongX ? new Vector3(depth, 0.01f, 0.1f) : new Vector3(0.1f, 0.01f, depth);
            Decal(c.Markings, "BayLine", new Vector3(centre.x, y, centre.z) + right * (width / 2f), lineSize, c.A.PaintWhite);
            Decal(c.Markings, "BayLine", new Vector3(centre.x, y, centre.z) - right * (width / 2f), lineSize, c.A.PaintWhite);

            float yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            int index = c.Result.ParkingSpots.Count;
            var spot = new GameObject($"Spot_{lot}_{index}");
            spot.transform.SetParent(c.Spots, false);
            spot.transform.SetPositionAndRotation(centre, Quaternion.Euler(0f, yaw, 0f));
            c.Result.ParkingSpots.Add(new ParkingSpot(centre, yaw, lot));
        }

        private static GameObject Prop(Context c, StreetPropLibrary.Kind kind, Vector3 position, float yaw)
        {
            GameObject go = StreetPropLibrary.Build(kind, c.A, c.Rng, c.Props);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return go;
        }

        /// <summary>A ground slab whose top is at <paramref name="top"/>; world-scale UVs, Environment, collider.</summary>
        private static GameObject Slab(Transform parent, string name, Area area, float top, float thickness, Material material)
        {
            var size = new Vector3(area.Width, thickness, area.Depth);
            return Piece(parent, name, area.Center + Vector3.up * (top - thickness / 2f), size, material);
        }

        /// <summary>A thin painted marking lying on a surface: no collider, no shadows.</summary>
        private static GameObject Decal(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            GameObject go = PrefabFactory.Primitive(PrimitiveType.Cube, name, parent, position, size, material, collider: false);
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.renderingLayerMask = ExteriorRenderingLayer | 1u;
            go.layer = Layers.EnvironmentIndex;
            return go;
        }

        /// <summary>
        /// A cube with per-metre UVs (so brick courses are brick-sized on a tower and on a kiosk alike),
        /// placed in <paramref name="parent"/>'s local space. Sizes are quantised to a quarter metre
        /// so the district shares a few dozen meshes rather than minting one per block.
        /// </summary>
        private static GameObject Piece(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material, bool collider = true)
        {
            GameObject go = PrefabFactory.Primitive(PrimitiveType.Cube, name, parent, localPosition, size, material, collider);
            var q = new Vector3(Quantize(size.x), Quantize(size.y), Quantize(size.z));
            go.GetComponent<MeshFilter>().sharedMesh = MeshLibrary.WorldCube(q);
            go.GetComponent<Renderer>().renderingLayerMask = ExteriorRenderingLayer | 1u;
            go.layer = Layers.EnvironmentIndex;
            return go;
        }

        private static float Quantize(float v) => Mathf.Max(0.01f, Mathf.Round(v * 4f) / 4f);

        private static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }
    }
}
