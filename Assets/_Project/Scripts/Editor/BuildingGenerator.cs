using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Vent.Core.Utility;
using Vent.Enemies.Spawning;

namespace Vent.Editor
{
    /// <summary>Tunable knobs for the building. Small on purpose; the layout is a grid of rooms.</summary>
    public sealed class BuildingLayout
    {
        public int Columns = 4;
        public int Rows = 3;
        public float CellSize = 10f;
        public float WallThickness = 0.3f;
        public float Height = 3.2f;
        public float DoorWidth = 2.0f;
        public float DoorHeight = 2.4f;
        public float VentHeight = 2.45f;
        public float WindowWidth = 1.8f;
        public float WindowSill = 1.0f;
        public float WindowTop = 2.4f;
        /// <summary>Windows per outer wall segment.</summary>
        public int WindowsPerWall = 2;
        public bool Exterior = true;
        public int Seed = 1337;
        /// <summary>Fraction of non-tree adjacencies that also get a door (adds loops).</summary>
        public float ExtraDoorChance = 0.35f;
        public bool BakeNavMesh = true;
    }

    /// <summary>
    /// Procedurally builds the sealed, single-storey building from primitives: a grid of rooms
    /// joined by doorways (a random spanning tree plus a few extra doors so there are loops),
    /// ceiling lights, cover props, and AC vents high on the walls. Deterministic for a seed.
    ///
    /// The player can never leave: every outer wall is solid and every room has a ceiling.
    /// </summary>
    public static class BuildingGenerator
    {
        public sealed class Result
        {
            public GameObject Root;
            public Vector3 PlayerSpawn;
            public float PlayerYaw;
            public readonly List<AirVent> Vents = new();
            public readonly List<Vector3> RoomCenters = new();
            public readonly List<Vector3> WindowCenters = new();
        }

        // BatchingStatic matters: without the GPU Resident Drawer (see README, "GPU Resident Drawer")
        // every block is its own draw call otherwise.
        private static readonly StaticEditorFlags StaticFlags =
            StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;

        public static Result Generate(GameAssets a, BuildingLayout layout, Transform parent = null)
        {
            var rng = new System.Random(layout.Seed);
            var result = new Result { Root = new GameObject("Building") };
            if (parent != null)
            {
                result.Root.transform.SetParent(parent, false);
            }

            Transform geometry = Child(result.Root.transform, "Geometry");
            Transform lights = Child(result.Root.transform, "Lights");
            Transform vents = Child(result.Root.transform, "Vents");
            Transform props = Child(result.Root.transform, "Props");

            int cols = layout.Columns, rows = layout.Rows;
            float cell = layout.CellSize, t = layout.WallThickness, h = layout.Height;

            HashSet<(int, int)> doors = PlanDoors(cols, rows, rng, layout.ExtraDoorChance);
            var ventFloorPoints = new List<Vector3>();
            var doorCenters = new List<Vector3>();

            // ---- Rooms: floor, ceiling, light ---------------------------------------------
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    Vector3 center = CellCenter(c, r, cols, rows, cell);
                    result.RoomCenters.Add(center);
                    Transform room = Child(geometry, $"Room_{c}_{r}");
                    room.position = center;

                    Block(room, "Floor", center + Vector3.down * 0.1f, new Vector3(cell, 0.2f, cell), a.Floor);
                    Block(room, "Ceiling", center + Vector3.up * (h + 0.1f), new Vector3(cell, 0.2f, cell), a.Ceiling);
                    Block(room, "LightPanel", center + Vector3.up * (h - 0.03f), new Vector3(1.6f, 0.06f, 1.6f), a.LightPanel, collider: false);

                    var lightGo = new GameObject($"Light_{c}_{r}");
                    lightGo.transform.SetParent(lights, false);
                    lightGo.transform.position = center + Vector3.up * (h - 0.5f);
                    Light light = lightGo.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.range = cell * 0.9f;
                    light.intensity = 4.4f;
                    light.color = new Color(1f, 0.93f, 0.8f);
                    // No shadows: a point light is six shadow-atlas slices, and with 28 shadowed window
                    // lights the atlas must always fit or URP re-picks shadow casters per frame — lights
                    // that lose their shadow shine through walls, which reads as blinking.
                    light.shadows = LightShadows.None;
                    lightGo.layer = Layers.PlayerIndex; // see PrefabFactory: lights live on a layer both cameras cull in
                }
            }

            // ---- Walls: every boundary once ------------------------------------------------
            // Vertical boundaries (walls running along Z) at x = boundary between column c and c+1.
            for (int r = 0; r < rows; r++)
            {
                for (int c = -1; c < cols; c++)
                {
                    bool outer = c < 0 || c == cols - 1;
                    bool hasDoor = !outer && doors.Contains(Key(Index(c, r, cols), Index(c + 1, r, cols)));
                    Vector3 left = CellCenter(Mathf.Max(c, 0), r, cols, rows, cell);
                    float x = c < 0 ? left.x - cell / 2f : left.x + cell / 2f;
                    var wallCenter = new Vector3(x, 0f, left.z);
                    if (outer)
                    {
                        // Outer walls get windows; the light outside them is what brightens the rooms.
                        Vector3 inward = c < 0 ? Vector3.right : Vector3.left;
                        BuildWindowWall(geometry, lights, a, layout, wallCenter, alongZ: true, cell, t, h, inward, $"WallX_{c}_{r}", result.WindowCenters);
                    }
                    else
                    {
                        BuildWall(geometry, a, wallCenter, alongZ: true, cell, t, h, hasDoor, layout.DoorWidth, layout.DoorHeight, $"WallX_{c}_{r}");
                    }

                    if (hasDoor)
                    {
                        doorCenters.Add(wallCenter);
                    }
                }
            }

            // Horizontal boundaries (walls running along X) at z = boundary between row r and r+1.
            for (int c = 0; c < cols; c++)
            {
                for (int r = -1; r < rows; r++)
                {
                    bool outer = r < 0 || r == rows - 1;
                    bool hasDoor = !outer && doors.Contains(Key(Index(c, r, cols), Index(c, r + 1, cols)));
                    Vector3 below = CellCenter(c, Mathf.Max(r, 0), cols, rows, cell);
                    float z = r < 0 ? below.z - cell / 2f : below.z + cell / 2f;
                    var wallCenter = new Vector3(below.x, 0f, z);
                    if (outer)
                    {
                        Vector3 inward = r < 0 ? Vector3.forward : Vector3.back;
                        BuildWindowWall(geometry, lights, a, layout, wallCenter, alongZ: false, cell, t, h, inward, $"WallZ_{c}_{r}", result.WindowCenters);
                    }
                    else
                    {
                        BuildWall(geometry, a, wallCenter, alongZ: false, cell, t, h, hasDoor, layout.DoorWidth, layout.DoorHeight, $"WallZ_{c}_{r}");
                    }

                    if (hasDoor)
                    {
                        doorCenters.Add(wallCenter);
                    }
                }
            }

            // ---- Player spawn: the most central room, facing +Z ------------------------------
            Vector3 spawnCenter = CellCenter(cols / 2, rows / 2, cols, rows, cell);
            result.PlayerSpawn = spawnCenter;
            result.PlayerYaw = 0f;

            // ---- Vents: 1-2 per room, on walls, clear of doorways ------------------------------
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    Vector3 center = CellCenter(c, r, cols, rows, cell);
                    int count = 1 + (rng.NextDouble() < 0.6 ? 1 : 0);
                    var sides = new List<int> { 0, 1, 2, 3 };
                    Shuffle(sides, rng);
                    for (int i = 0; i < count; i++)
                    {
                        int side = sides[i];
                        // Facing into the room; wall inner face is cell/2 - t/2 from the centre.
                        Vector3 normal = side switch { 0 => Vector3.back, 1 => Vector3.forward, 2 => Vector3.left, _ => Vector3.right };
                        Vector3 along = side < 2 ? Vector3.right : Vector3.forward;
                        Vector3 pos = Vector3.zero;
                        bool ventPlaced = false;
                        for (int attempt = 0; attempt < 8 && !ventPlaced; attempt++)
                        {
                            float offset = (float)(rng.NextDouble() * (cell / 2f - 2.6f) + 1.6f) * (rng.NextDouble() < 0.5 ? -1f : 1f);
                            Vector3 wallInner = center - normal * (cell / 2f - t / 2f) + along * offset;
                            pos = new Vector3(wallInner.x, layout.VentHeight, wallInner.z);
                            ventPlaced = Clear(new Vector3(pos.x, 0f, pos.z), result.WindowCenters, layout.WindowWidth / 2f + 0.7f);
                        }

                        if (!ventPlaced)
                        {
                            continue; // this wall is all window; the other sides will do
                        }

                        var ventGo = (GameObject)PrefabUtility.InstantiatePrefab(a.VentPrefab, vents);
                        ventGo.name = $"Vent_{c}_{r}_{side}";
                        ventGo.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(normal));
                        var vent = ventGo.GetComponent<AirVent>();

                        // Floor point sits just inside the room at floor level, directly below the grate.
                        Transform floorPoint = ventGo.transform.Find("FloorPoint");
                        floorPoint.position = new Vector3(pos.x, 0f, pos.z) + normal * 0.9f;
                        ventFloorPoints.Add(floorPoint.position);
                        result.Vents.Add(vent);
                    }
                }
            }

            // ---- Furniture: each room gets a purpose and is dressed for it -----------------------
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    Vector3 center = CellCenter(c, r, cols, rows, cell);
                    bool isSpawnRoom = c == cols / 2 && r == rows / 2;
                    RoomType type = isSpawnRoom ? RoomType.Lobby : PickRoomType(rng);
                    Transform room = Child(props, $"Room_{c}_{r}_{type}");
                    var placer = new RoomDresser(a, rng, room, center, cell, t, doorCenters, ventFloorPoints, spawnCenter);
                    placer.Dress(type);
                }
            }

            // ---- Exterior: what you see through the windows -------------------------------------
            if (layout.Exterior)
            {
                BuildExterior(result.Root.transform, a, rng, cols * cell, rows * cell, h);
            }

            // ---- Static flags & NavMesh --------------------------------------------------------------
            foreach (Transform child in geometry.GetComponentsInChildren<Transform>())
            {
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, StaticFlags);
            }

            foreach (Transform child in props.GetComponentsInChildren<Transform>())
            {
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, StaticFlags);
            }

            if (layout.BakeNavMesh)
            {
                var surface = result.Root.AddComponent<NavMeshSurface>();
                surface.collectObjects = CollectObjects.All;
                surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
                surface.layerMask = LayerMask.GetMask(Layers.Environment);
                surface.BuildNavMesh();
                if (surface.navMeshData != null)
                {
                    AssetDatabase.DeleteAsset(Paths.BuildingNavMesh);
                    AssetDatabase.CreateAsset(surface.navMeshData, Paths.BuildingNavMesh);
                }
            }

            return result;
        }

        // ------------------------------------------------------------------ windows & exterior

        /// <summary>
        /// An outer wall: solid below the sill and above the head, piers between the windows, and a
        /// glass pane (collider on, so the building stays sealed and glass is shootable) with a frame
        /// and mullion in each opening. A warm spot light sits outside every window pointing in — the
        /// only light that comes "through" the windows, which is why it never leaks through walls.
        /// </summary>
        private static void BuildWindowWall(Transform parent, Transform lights, GameAssets a, BuildingLayout layout, Vector3 center, bool alongZ,
            float length, float thickness, float height, Vector3 inward, string name, List<Vector3> windowCenters)
        {
            Vector3 along = alongZ ? Vector3.forward : Vector3.right;
            Vector3 Size(float len, float hgt) => alongZ ? new Vector3(thickness, hgt, len) : new Vector3(len, hgt, thickness);
            Transform wall = Child(parent, name);

            float sill = layout.WindowSill, top = Mathf.Min(layout.WindowTop, height - 0.2f), w = layout.WindowWidth;
            int n = Mathf.Max(1, layout.WindowsPerWall);
            float full = length + thickness;

            Block(wall, "Below", center + Vector3.up * (sill / 2f), Size(full, sill), a.Wall);
            Block(wall, "Above", center + Vector3.up * (top + (height - top) / 2f), Size(full, height - top), a.Wall);

            // Windows evenly spaced; piers fill the gaps (including the corner overlap at both ends).
            var edges = new List<float> { -full / 2f };
            for (int i = 0; i < n; i++)
            {
                float wc = (i - (n - 1) / 2f) * (length / n);
                edges.Add(wc - w / 2f);
                edges.Add(wc + w / 2f);

                Vector3 windowCenter = center + along * wc + Vector3.up * ((sill + top) / 2f);
                windowCenters.Add(center + along * wc);
                float paneT = 0.04f;
                Vector3 paneSize = alongZ ? new Vector3(paneT, top - sill, w) : new Vector3(w, top - sill, paneT);
                GameObject glass = Block(wall, $"Glass{i}", windowCenter, paneSize, a.WindowGlass);
                // A transparent pane still casts a full shadow unless told otherwise; that would block
                // the very light the window exists for.
                glass.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                // Frame + mullion, visual only
                Vector3 frameDepth = alongZ ? new Vector3(thickness + 0.02f, 0f, 0f) : new Vector3(0f, 0f, thickness + 0.02f);
                Block(wall, $"Sill{i}", windowCenter - Vector3.up * ((top - sill) / 2f + 0.03f), Size(w + 0.12f, 0.06f) + frameDepth, a.Trim, collider: false);
                Block(wall, $"Head{i}", windowCenter + Vector3.up * ((top - sill) / 2f + 0.03f), Size(w + 0.12f, 0.06f) + frameDepth, a.Trim, collider: false);
                Block(wall, $"JambL{i}", windowCenter - along * (w / 2f + 0.03f), Size(0.06f, top - sill) + frameDepth, a.Trim, collider: false);
                Block(wall, $"JambR{i}", windowCenter + along * (w / 2f + 0.03f), Size(0.06f, top - sill) + frameDepth, a.Trim, collider: false);
                Block(wall, $"Mullion{i}", windowCenter, Size(0.05f, top - sill) + frameDepth * 0.5f, a.Trim, collider: false);
                Block(wall, $"Transom{i}", windowCenter, Size(w, 0.05f) + frameDepth * 0.5f, a.Trim, collider: false);

                var lightGo = new GameObject($"WindowLight_{name}_{i}");
                lightGo.transform.SetParent(lights, false);
                lightGo.transform.position = windowCenter - inward * 1.3f + Vector3.up * 0.3f;
                lightGo.transform.rotation = Quaternion.LookRotation(inward + Vector3.down * 0.35f);
                Light light = lightGo.AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = 95f;
                light.innerSpotAngle = 60f;
                light.range = 12f; // across this room; the far wall's shadow stops it there
                light.intensity = 16f; // a low sun through a window is bright; it falls off fast
                light.color = new Color(1f, 0.72f, 0.48f); // dusk
                light.shadows = LightShadows.Hard;
                light.shadowStrength = 0.9f;
                // Low tier (256 px): 28 of these fit the 2048 atlas with room to spare, so every window
                // light is shadowed every frame — the shadow is what keeps it inside its room.
                var tier = new SerializedObject(light.GetUniversalAdditionalLightData());
                tier.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
                tier.ApplyModifiedPropertiesWithoutUndo();
                lightGo.layer = Layers.PlayerIndex;
            }

            edges.Add(full / 2f);
            for (int i = 0; i < edges.Count; i += 2)
            {
                float a0 = edges[i], a1 = edges[i + 1];
                if (a1 - a0 < 0.01f)
                {
                    continue;
                }

                Block(wall, $"Pier{i / 2}", center + along * ((a0 + a1) / 2f) + Vector3.up * ((sill + top) / 2f), Size(a1 - a0, top - sill), a.Wall);
            }
        }

        /// <summary>
        /// Ground, a sun on its own rendering layer (so it lights only the outside), a dusk skybox and a
        /// few distant building silhouettes. Default layer: not baked into the NavMesh, not shootable.
        /// </summary>
        private static void BuildExterior(Transform root, GameAssets a, System.Random rng, float width, float depth, float height)
        {
            Transform exterior = Child(root, "Exterior");
            const uint exteriorRenderingLayer = 1u << 1;

            GameObject ground = PrefabFactory.Primitive(PrimitiveType.Cube, "Ground", exterior, new Vector3(0f, -0.3f, 0f), new Vector3(400f, 0.2f, 400f), a.Asphalt, collider: true);
            ground.GetComponent<Renderer>().renderingLayerMask = exteriorRenderingLayer | 1u;
            // Pavement apron and kerb right outside the glass so the ground reads at close range.
            GameObject apron = PrefabFactory.Primitive(PrimitiveType.Cube, "Apron", exterior, new Vector3(0f, -0.17f, 0f), new Vector3(width + 8f, 0.06f, depth + 8f), a.Concrete, collider: false);
            apron.GetComponent<Renderer>().renderingLayerMask = exteriorRenderingLayer | 1u;

            for (int i = 0; i < 14; i++)
            {
                float angle = i * (360f / 14f) + (float)rng.NextDouble() * 15f;
                float dist = 28f + (float)rng.NextDouble() * 45f;
                Vector3 pos = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * dist;
                var size = new Vector3(8f + (float)rng.NextDouble() * 14f, 6f + (float)rng.NextDouble() * 22f, 8f + (float)rng.NextDouble() * 14f);
                GameObject block = PrefabFactory.Primitive(PrimitiveType.Cube, $"Building{i}", exterior, pos + Vector3.up * (size.y / 2f - 0.2f), size, a.DistantBuilding, collider: false);
                block.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                block.GetComponent<Renderer>().renderingLayerMask = exteriorRenderingLayer | 1u;
                // Lit windows on the far buildings: a few emissive strips.
                int floors = Mathf.Max(1, (int)(size.y / 3f));
                for (int f = 0; f < floors; f++)
                {
                    if (rng.NextDouble() < 0.55)
                    {
                        GameObject strip = PrefabFactory.Primitive(PrimitiveType.Cube, "Lit", block.transform, new Vector3(0f, -0.5f + (f + 0.5f) / floors, 0.501f), new Vector3(0.8f, 0.25f / floors, 0.01f), a.LightPanel, collider: false);
                        strip.GetComponent<Renderer>().renderingLayerMask = exteriorRenderingLayer | 1u;
                    }
                }
            }

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(exterior, false);
            sunGo.transform.rotation = Quaternion.Euler(14f, 205f, 0f); // low dusk sun
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.62f, 0.38f);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.None;
            // URP takes a light's rendering layers from its UniversalAdditionalLightData, not from
            // Light.renderingLayerMask. Exterior only: interiors are lit through the windows.
            sun.renderingLayerMask = (int)exteriorRenderingLayer;
            UniversalAdditionalLightData sunData = sun.GetUniversalAdditionalLightData();
            sunData.renderingLayers = exteriorRenderingLayer;
            sunData.customShadowLayers = false;
            RenderSettings.sun = sun;

            foreach (Transform child in exterior.GetComponentsInChildren<Transform>())
            {
                child.gameObject.layer = 0; // Default: no NavMesh, no bullet hits, no occlusion queries
            }
        }

        // ------------------------------------------------------------------ rooms

        public enum RoomType { Office, Conference, BreakRoom, Lobby, Storage, ServerRoom }

        private static RoomType PickRoomType(System.Random rng)
        {
            double roll = rng.NextDouble();
            return roll < 0.40 ? RoomType.Office
                : roll < 0.55 ? RoomType.Conference
                : roll < 0.70 ? RoomType.BreakRoom
                : roll < 0.85 ? RoomType.Storage
                : RoomType.ServerRoom;
        }

        /// <summary>
        /// Places <see cref="PropLibrary"/> pieces in one room: against walls (back to the wall,
        /// away from doors and vent landings) or free-standing, never overlapping each other and
        /// never crowding the player spawn. Everything it makes is static, collidable Environment.
        /// </summary>
        private sealed class RoomDresser
        {
            private readonly GameAssets a;
            private readonly System.Random rng;
            private readonly Transform parent;
            private readonly Vector3 center;
            private readonly float cell, wall;
            private readonly List<Vector3> doors, vents;
            private readonly Vector3 spawn;
            private readonly List<(Vector3 pos, float radius)> placed = new();

            public RoomDresser(GameAssets assets, System.Random random, Transform room, Vector3 roomCenter, float cellSize, float wallThickness,
                List<Vector3> doorCenters, List<Vector3> ventLandings, Vector3 spawnCenter)
            {
                a = assets;
                rng = random;
                parent = room;
                center = roomCenter;
                cell = cellSize;
                wall = wallThickness;
                doors = doorCenters;
                vents = ventLandings;
                spawn = spawnCenter;
            }

            public void Dress(RoomType type)
            {
                switch (type)
                {
                    case RoomType.Office:
                        int desks = 2 + rng.Next(2);
                        for (int i = 0; i < desks; i++)
                        {
                            float yaw = rng.Next(4) * 90f;
                            GameObject desk = Free(PropLibrary.Kind.Desk, yaw);
                            if (desk != null)
                            {
                                Vector3 behind = desk.transform.position - desk.transform.forward * 0.75f;
                                At(PropLibrary.Kind.OfficeChair, behind, yaw + Rand(rng, -15f, 15f));
                                if (rng.NextDouble() < 0.5)
                                {
                                    At(PropLibrary.Kind.TrashBin, desk.transform.position - desk.transform.right * 1.05f - desk.transform.forward * 0.3f, 0f);
                                }
                            }
                        }

                        Wall(PropLibrary.Kind.FilingCabinet);
                        if (rng.NextDouble() < 0.7) Wall(PropLibrary.Kind.FilingCabinet);
                        if (rng.NextDouble() < 0.6) Wall(PropLibrary.Kind.Bookshelf);
                        if (rng.NextDouble() < 0.5) Wall(PropLibrary.Kind.Whiteboard);
                        if (rng.NextDouble() < 0.6) Free(PropLibrary.Kind.PottedPlant, Rand(rng, 0f, 360f));
                        if (rng.NextDouble() < 0.4) Free(PropLibrary.Kind.CubicleWall, rng.Next(2) * 90f);
                        break;

                    case RoomType.Conference:
                        float tableYaw = rng.Next(2) * 90f;
                        GameObject table = At(PropLibrary.Kind.ConferenceTable, center, tableYaw);
                        if (table != null)
                        {
                            for (int side = -1; side <= 1; side += 2)
                            {
                                for (int i = -1; i <= 1; i++)
                                {
                                    Vector3 pos = table.transform.position + table.transform.right * (i * 1.0f) + table.transform.forward * (side * 0.95f);
                                    At(PropLibrary.Kind.OfficeChair, pos, tableYaw + (side > 0 ? 180f : 0f) + Rand(rng, -10f, 10f));
                                }
                            }
                        }

                        Wall(PropLibrary.Kind.Whiteboard);
                        Wall(PropLibrary.Kind.WaterCooler);
                        if (rng.NextDouble() < 0.7) Free(PropLibrary.Kind.PottedPlant, Rand(rng, 0f, 360f));
                        break;

                    case RoomType.BreakRoom:
                        Wall(PropLibrary.Kind.VendingMachine);
                        Wall(PropLibrary.Kind.VendingMachine);
                        Wall(PropLibrary.Kind.WaterCooler);
                        Wall(PropLibrary.Kind.Couch);
                        Free(PropLibrary.Kind.TrashBin, 0f);
                        GameObject cafe = Free(PropLibrary.Kind.ConferenceTable, rng.Next(2) * 90f);
                        if (cafe != null)
                        {
                            for (int side = -1; side <= 1; side += 2)
                            {
                                for (int i = -1; i <= 1; i += 2)
                                {
                                    Vector3 pos = cafe.transform.position + cafe.transform.right * (i * 0.8f) + cafe.transform.forward * (side * 0.95f);
                                    At(PropLibrary.Kind.OfficeChair, pos, cafe.transform.eulerAngles.y + (side > 0 ? 180f : 0f) + Rand(rng, -20f, 20f));
                                }
                            }
                        }

                        if (rng.NextDouble() < 0.6) Free(PropLibrary.Kind.PottedPlant, Rand(rng, 0f, 360f));
                        break;

                    case RoomType.Lobby:
                        Wall(PropLibrary.Kind.ReceptionCounter);
                        Wall(PropLibrary.Kind.Couch);
                        if (rng.NextDouble() < 0.7) Wall(PropLibrary.Kind.Couch);
                        Free(PropLibrary.Kind.PottedPlant, Rand(rng, 0f, 360f));
                        Free(PropLibrary.Kind.PottedPlant, Rand(rng, 0f, 360f));
                        Wall(PropLibrary.Kind.TrashBin);
                        break;

                    case RoomType.Storage:
                        int units = 3 + rng.Next(3);
                        for (int i = 0; i < units; i++) Wall(PropLibrary.Kind.Shelving);
                        if (rng.NextDouble() < 0.7) Free(PropLibrary.Kind.Shelving, rng.Next(2) * 90f);
                        if (rng.NextDouble() < 0.5) Wall(PropLibrary.Kind.Copier);
                        break;

                    case RoomType.ServerRoom:
                        // Two rows of racks facing an aisle down the middle of the room.
                        float rowYaw = rng.Next(2) * 90f;
                        var rowAxis = Quaternion.Euler(0f, rowYaw, 0f) * Vector3.right;
                        var rowNormal = Quaternion.Euler(0f, rowYaw, 0f) * Vector3.forward;
                        int perRow = 3 + rng.Next(2);
                        for (int row = -1; row <= 1; row += 2)
                        {
                            for (int i = 0; i < perRow; i++)
                            {
                                Vector3 pos = center + rowAxis * ((i - (perRow - 1) / 2f) * 0.75f) + rowNormal * (row * 1.4f);
                                At(PropLibrary.Kind.ServerRack, pos, rowYaw + (row > 0 ? 180f : 0f));
                            }
                        }

                        Wall(PropLibrary.Kind.FilingCabinet);
                        break;
                }
            }

            private GameObject Wall(PropLibrary.Kind kind)
            {
                Vector2 fp = PropLibrary.Footprint(kind);
                var sides = new List<int> { 0, 1, 2, 3 };
                Shuffle(sides, rng);
                foreach (int side in sides)
                {
                    Vector3 normal = side switch { 0 => Vector3.back, 1 => Vector3.forward, 2 => Vector3.left, _ => Vector3.right };
                    Vector3 along = side < 2 ? Vector3.right : Vector3.forward;
                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        float half = cell / 2f - wall / 2f - fp.x / 2f - 0.3f;
                        float offset = Rand(rng, -half, half);
                        Vector3 pos = center - normal * (cell / 2f - wall / 2f - fp.y / 2f - 0.02f) + along * offset;
                        if (Fits(pos, fp))
                        {
                            return Spawn(kind, pos, Quaternion.LookRotation(normal), fp);
                        }
                    }
                }

                return null;
            }

            private GameObject Free(PropLibrary.Kind kind, float yaw)
            {
                Vector2 fp = PropLibrary.Footprint(kind);
                for (int attempt = 0; attempt < 14; attempt++)
                {
                    float half = cell / 2f - 1.6f - Mathf.Max(fp.x, fp.y) / 2f;
                    Vector3 pos = center + new Vector3(Rand(rng, -half, half), 0f, Rand(rng, -half, half));
                    if (Fits(pos, fp))
                    {
                        return Spawn(kind, pos, Quaternion.Euler(0f, yaw, 0f), fp);
                    }
                }

                return null;
            }

            private GameObject At(PropLibrary.Kind kind, Vector3 pos, float yaw)
            {
                Vector2 fp = PropLibrary.Footprint(kind);
                pos.y = 0f;
                float limit = cell / 2f - wall - Mathf.Max(fp.x, fp.y) / 2f;
                if (Mathf.Abs(pos.x - center.x) > limit || Mathf.Abs(pos.z - center.z) > limit || !Fits(pos, fp, ignoreProps: kind == PropLibrary.Kind.OfficeChair))
                {
                    return null;
                }

                return Spawn(kind, pos, Quaternion.Euler(0f, yaw, 0f), fp);
            }

            private bool Fits(Vector3 pos, Vector2 fp, bool ignoreProps = false)
            {
                float radius = Mathf.Max(fp.x, fp.y) / 2f;
                if (!Clear(pos, doors, 1.9f + radius) || !Clear(pos, vents, 1.3f + radius) || Vector3.Distance(pos, spawn) < 2.4f + radius)
                {
                    return false;
                }

                if (ignoreProps)
                {
                    return true;
                }

                foreach ((Vector3 other, float otherRadius) in placed)
                {
                    if (Vector3.Distance(pos, other) < radius + otherRadius + 0.15f)
                    {
                        return false;
                    }
                }

                return true;
            }

            private GameObject Spawn(PropLibrary.Kind kind, Vector3 pos, Quaternion rotation, Vector2 fp)
            {
                GameObject go = PropLibrary.Build(kind, a, rng, parent);
                go.transform.SetPositionAndRotation(pos, rotation);
                placed.Add((pos, Mathf.Max(fp.x, fp.y) / 2f));
                return go;
            }
        }

        // ------------------------------------------------------------------ pieces

        private static void BuildWall(Transform parent, GameAssets a, Vector3 center, bool alongZ, float length, float thickness, float height,
            bool door, float doorWidth, float doorHeight, string name)
        {
            Vector3 along = alongZ ? Vector3.forward : Vector3.right;
            Vector3 Size(float len, float hgt) => alongZ ? new Vector3(thickness, hgt, len) : new Vector3(len, hgt, thickness);
            Transform wall = Child(parent, name);

            if (!door)
            {
                Block(wall, "Solid", center + Vector3.up * (height / 2f), Size(length + thickness, height), a.Wall);
                return;
            }

            float side = (length - doorWidth) / 2f;
            float segment = side + thickness / 2f;              // extends into the corner like solid walls do
            float sideOffset = doorWidth / 2f + segment / 2f;   // inner edge lands exactly on the door opening
            Block(wall, "Left", center - along * sideOffset + Vector3.up * (height / 2f), Size(segment, height), a.Wall);
            Block(wall, "Right", center + along * sideOffset + Vector3.up * (height / 2f), Size(segment, height), a.Wall);
            float lintel = height - doorHeight;
            Block(wall, "Lintel", center + Vector3.up * (doorHeight + lintel / 2f), Size(doorWidth + 0.02f, lintel), a.Wall);

            // Door frame trim: purely visual, reads as a doorway from a distance.
            Vector3 frameSize = alongZ ? new Vector3(thickness + 0.04f, doorHeight, 0.08f) : new Vector3(0.08f, doorHeight, thickness + 0.04f);
            Block(wall, "FrameL", center - along * (doorWidth / 2f) + Vector3.up * (doorHeight / 2f), frameSize, a.Trim, collider: false);
            Block(wall, "FrameR", center + along * (doorWidth / 2f) + Vector3.up * (doorHeight / 2f), frameSize, a.Trim, collider: false);
        }

        private static GameObject Block(Transform parent, string name, Vector3 worldCenter, Vector3 size, Material material, bool collider = true)
        {
            GameObject go = PrefabFactory.Primitive(PrimitiveType.Cube, name, parent, Vector3.zero, size, material, collider);
            go.transform.position = worldCenter;
            go.layer = Layers.EnvironmentIndex;
            return go;
        }

        // ------------------------------------------------------------------ layout

        /// <summary>Randomised DFS spanning tree over the grid, plus extra doors for loops.</summary>
        private static HashSet<(int, int)> PlanDoors(int cols, int rows, System.Random rng, float extraChance)
        {
            var doors = new HashSet<(int, int)>();
            var visited = new bool[cols * rows];
            var stack = new Stack<int>();
            int start = Index(cols / 2, rows / 2, cols);
            visited[start] = true;
            stack.Push(start);

            while (stack.Count > 0)
            {
                int current = stack.Peek();
                List<int> neighbours = Neighbours(current, cols, rows);
                Shuffle(neighbours, rng);
                bool advanced = false;
                foreach (int n in neighbours)
                {
                    if (visited[n])
                    {
                        continue;
                    }

                    visited[n] = true;
                    doors.Add(Key(current, n));
                    stack.Push(n);
                    advanced = true;
                    break;
                }

                if (!advanced)
                {
                    stack.Pop();
                }
            }

            for (int i = 0; i < cols * rows; i++)
            {
                foreach (int n in Neighbours(i, cols, rows))
                {
                    if (n > i && !doors.Contains(Key(i, n)) && rng.NextDouble() < extraChance)
                    {
                        doors.Add(Key(i, n));
                    }
                }
            }

            return doors;
        }

        private static List<int> Neighbours(int index, int cols, int rows)
        {
            int c = index % cols, r = index / cols;
            var list = new List<int>(4);
            if (c > 0) list.Add(Index(c - 1, r, cols));
            if (c < cols - 1) list.Add(Index(c + 1, r, cols));
            if (r > 0) list.Add(Index(c, r - 1, cols));
            if (r < rows - 1) list.Add(Index(c, r + 1, cols));
            return list;
        }

        private static int Index(int c, int r, int cols) => r * cols + c;
        private static (int, int) Key(int x, int y) => x < y ? (x, y) : (y, x);

        public static Vector3 CellCenter(int c, int r, int cols, int rows, float cell)
        {
            return new Vector3((c - (cols - 1) / 2f) * cell, 0f, (r - (rows - 1) / 2f) * cell);
        }

        private static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void Shuffle<T>(IList<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private static float Rand(System.Random rng, float min, float max) => (float)(min + rng.NextDouble() * (max - min));

        private static bool Clear(Vector3 pos, List<Vector3> points, float radius)
        {
            foreach (Vector3 p in points)
            {
                if (Vector3.Distance(new Vector3(pos.x, 0f, pos.z), new Vector3(p.x, 0f, p.z)) < radius)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
