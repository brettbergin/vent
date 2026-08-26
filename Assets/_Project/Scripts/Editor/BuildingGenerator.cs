using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Vent.Core.Events;
using Vent.Core.Utility;
using Vent.Enemies.Spawning;
using Vent.Gameplay.World;

namespace Vent.Editor
{
    /// <summary>Tunable knobs for the building. Small on purpose; the layout is a grid of rooms.</summary>
    public sealed class BuildingLayout
    {
        public int Columns = 4;
        public int Rows = 3;
        public float CellSize = 15f; // room edge length; the whole building scales off this
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
        /// <summary>Cut a front door into the lobby's outer wall. Off for the menu backdrop.</summary>
        public bool FrontDoor = true;
        public float FrontDoorWidth = 2.2f;
        /// <summary>A pavement apron around the building. Off when a district lays its own streets.</summary>
        public bool Apron = true;
        /// <summary>Half extents (XZ) the skyline must stay clear of; zero means "the building plus a margin".</summary>
        public Vector2 ExteriorClearHalfExtents = Vector2.zero;
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
    /// The player starts sealed in: every outer wall is glazed and solid, every room has a ceiling,
    /// and the only way out is the lobby's front door, which stays locked until level 4
    /// (<see cref="FrontDoor"/>).
    /// </summary>
    public static class BuildingGenerator
    {
        public sealed class Result
        {
            public GameObject Root;
            public Vector3 PlayerSpawn;
            public float PlayerYaw;
            /// <summary>Centre of the front door opening at floor level, and the direction out of the building.</summary>
            public Vector3 FrontDoorPosition;
            public Vector3 FrontDoorOutward = Vector3.right;
            /// <summary>The walls-and-roof volume, slightly padded: "inside the building" for the atmosphere blend.</summary>
            public Bounds Footprint;
            public (int c, int r) LobbyCell;
            public Transform LightsRoot;
            public Transform VentsRoot;
            public readonly List<AirVent> Vents = new();
            public readonly List<Vector3> RoomCenters = new();
            public readonly List<Vector3> WindowCenters = new();
        }

        // BatchingStatic matters: without the GPU Resident Drawer (see README, "GPU Resident Drawer")
        // every block is its own draw call otherwise.
        private static readonly StaticEditorFlags StaticFlags =
            StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.ContributeGI;

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
            Transform decals = Child(result.Root.transform, "Decals");
            Transform foliage = Child(result.Root.transform, "Foliage");
            result.LightsRoot = lights;
            result.VentsRoot = vents;

            int cols = layout.Columns, rows = layout.Rows;
            float cell = layout.CellSize, t = layout.WallThickness, h = layout.Height;
            // The lobby is the room with the front door: on the +X edge, middle row, so the street
            // is a straight walk from the spawn. Without a front door it stays in the middle.
            int lobbyC = layout.FrontDoor ? cols - 1 : cols / 2, lobbyR = rows / 2;
            result.LobbyCell = (lobbyC, lobbyR);
            result.Footprint = new Bounds(new Vector3(0f, h / 2f, 0f), new Vector3(cols * cell + 2f, h + 3f, rows * cell + 2f));

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
                    // Ceiling panels in a grid that grows with the room: one per ~7.5 m of edge, so a 10 m
                    // room has one and a 15 m room has four. A single point light in a big room leaves
                    // dark corners and a hot centre; an office ceiling does not look like that.
                    int panels = Mathf.Max(1, Mathf.RoundToInt(cell / 7.5f));
                    float pitch = cell / panels;
                    for (int pz = 0; pz < panels; pz++)
                    {
                        for (int px = 0; px < panels; px++)
                        {
                            Vector3 at = center + new Vector3((px - (panels - 1) / 2f) * pitch, 0f, (pz - (panels - 1) / 2f) * pitch);
                            Block(room, $"LightPanel_{px}_{pz}", at + Vector3.up * (h - 0.03f), new Vector3(1.6f, 0.06f, 1.6f), a.LightPanel, collider: false);

                            var lightGo = new GameObject($"Light_{c}_{r}_{px}_{pz}");
                            lightGo.transform.SetParent(lights, false);
                            lightGo.transform.position = at + Vector3.up * (h - 0.5f);
                            Light light = lightGo.AddComponent<Light>();
                            light.type = LightType.Point;
                            light.range = pitch * 1.1f;
                            light.intensity = 5.5f;
                            light.color = new Color(1f, 0.93f, 0.8f);
                            // No shadows: a point light is six shadow-atlas slices, and with 28 shadowed window
                            // lights the atlas must always fit or URP re-picks shadow casters per frame — lights
                            // that lose their shadow shine through walls, which reads as blinking.
                            light.shadows = LightShadows.None;
                            light.lightmapBakeType = LightmapBakeType.Mixed; // direct in real time, bounce baked (SceneBuilder bakes)
                            lightGo.layer = Layers.PlayerIndex; // see PrefabFactory: lights live on a layer both cameras cull in
                        }
                    }

                    DressCeiling(room, a, rng, center, cell, t, h, panels, pitch);
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
                    bool entrance = outer && layout.FrontDoor && c == cols - 1 && r == lobbyR;
                    Vector3 left = CellCenter(Mathf.Max(c, 0), r, cols, rows, cell);
                    float x = c < 0 ? left.x - cell / 2f : left.x + cell / 2f;
                    var wallCenter = new Vector3(x, 0f, left.z);
                    if (outer)
                    {
                        // Outer walls get windows; the light outside them is what brightens the rooms.
                        // The lobby's outer wall also carries the front door opening (the door itself is
                        // added by BuildFrontDoor after the NavMesh bake, so the doorway is walkable).
                        Vector3 inward = c < 0 ? Vector3.right : Vector3.left;
                        BuildWindowWall(geometry, lights, a, layout, wallCenter, alongZ: true, cell, t, h, inward, $"WallX_{c}_{r}", result.WindowCenters,
                            doorWidth: entrance ? layout.FrontDoorWidth : 0f, doorHeight: layout.DoorHeight, rng: rng, foliage: foliage);
                        if (entrance)
                        {
                            result.FrontDoorPosition = wallCenter;
                            result.FrontDoorOutward = Vector3.right;
                            doorCenters.Add(wallCenter); // keeps furniture and vents clear of the entrance
                        }
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
                        BuildWindowWall(geometry, lights, a, layout, wallCenter, alongZ: false, cell, t, h, inward, $"WallZ_{c}_{r}", result.WindowCenters, rng: rng, foliage: foliage);
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

            // ---- Player spawn: the lobby, facing the front door (or +Z when there is none) -------
            Vector3 spawnCenter = CellCenter(lobbyC, lobbyR, cols, rows, cell);
            result.PlayerSpawn = spawnCenter;
            result.PlayerYaw = layout.FrontDoor ? 90f : 0f;

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
                            Vector3 footprint = new Vector3(pos.x, 0f, pos.z);
                            ventPlaced = Clear(footprint, result.WindowCenters, layout.WindowWidth / 2f + 0.7f)
                                         && Clear(footprint, doorCenters, layout.FrontDoorWidth / 2f + 0.9f);
                        }

                        if (!ventPlaced)
                        {
                            continue; // this wall is all window; the other sides will do
                        }

                        VentGrime(decals, a, rng, pos, normal, layout.VentHeight);
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
                    bool isSpawnRoom = c == lobbyC && r == lobbyR;
                    RoomType type = isSpawnRoom ? RoomType.Lobby : PickRoomType(rng);
                    Transform room = Child(props, $"Room_{c}_{r}_{type}");
                    var placer = new RoomDresser(a, rng, room, center, cell, t, doorCenters, ventFloorPoints, spawnCenter);
                    placer.Dress(type);
                }
            }

            // ---- Exterior: what you see through the windows -------------------------------------
            if (layout.Exterior)
            {
                BuildExterior(result.Root.transform, a, rng, cols * cell, rows * cell, h, layout.ExteriorClearHalfExtents, layout.Apron);
            }

            // ---- Probes: baked bounce light for movers, and per-room reflections -------------------
            BuildProbes(result.Root.transform, result.RoomCenters, cell, h);

            // ---- Static flags & NavMesh --------------------------------------------------------------
            foreach (Transform child in geometry.GetComponentsInChildren<Transform>())
            {
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, StaticFlags);
            }

            // Leaves are probe-lit, never lightmapped: a cutout card in a lightmap is wasted texels and
            // a bleed of black around every leaf. They still batch, occlude and cast.
            foreach (Transform child in props.GetComponentsInChildren<Transform>())
            {
                bool leaves = child.name == FoliageLibrary.RendererName;
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, leaves ? StaticFlags & ~StaticEditorFlags.ContributeGI : StaticFlags);
            }

            foreach (Transform child in foliage.GetComponentsInChildren<Transform>())
            {
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, StaticFlags & ~StaticEditorFlags.ContributeGI);
            }

            foreach (Transform child in decals.GetComponentsInChildren<Transform>())
            {
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, StaticFlags & ~StaticEditorFlags.ContributeGI);
            }

            if (layout.BakeNavMesh)
            {
                BakeNavMesh(result.Root);
            }

            return result;
        }

        /// <summary>
        /// Bake the walkable surface from every Environment collider in the scene (the building and,
        /// when present, the district's streets) and save it next to the scene. Called separately from
        /// <see cref="Generate"/> when other generators add walkable ground first; the door leaves are
        /// added afterwards so the doorway itself is walkable and carved only while shut.
        /// </summary>
        public static void BakeNavMesh(GameObject root)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var surface = root.GetComponent<NavMeshSurface>();
            if (surface == null)
            {
                surface = root.AddComponent<NavMeshSurface>();
            }

            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = LayerMask.GetMask(Layers.Environment);
            // 0.2 m voxels: a 1 m gap between desks is still five voxels wide, and a city block of
            // streets bakes in well under a minute. Larger tiles keep the tile count sane outdoors.
            surface.overrideVoxelSize = true;
            surface.voxelSize = 0.2f;
            surface.overrideTileSize = true;
            surface.tileSize = 256;
            surface.minRegionArea = 4f;
            surface.BuildNavMesh();
            if (surface.navMeshData != null)
            {
                AssetDatabase.DeleteAsset(Paths.BuildingNavMesh);
                AssetDatabase.CreateAsset(surface.navMeshData, Paths.BuildingNavMesh);
            }

            Debug.Log($"[Vent] NavMesh baked in {clock.Elapsed.TotalSeconds:0}s");
        }

        // ------------------------------------------------------------------ front door

        /// <summary>
        /// Commercial double glass doors in the lobby's entrance opening: two hinged leaves that swing
        /// outward, a threshold, an exit sign and the card-reader lamp that goes green at level 4.
        /// Built after the NavMesh bake and left non-static: the leaves move, and their colliders
        /// must not be baked into the walkable surface. Layer Environment so bullets and the sealed-
        /// lobby test still hit a closed door.
        /// </summary>
        public static FrontDoor BuildFrontDoor(GameAssets a, Result building, LevelEventChannel level, StringEventChannel announcement, GameObject exteriorVents)
        {
            float width = 2.2f, height = 2.4f, depth = 0.05f;
            float leaf = width / 2f - 0.01f; // hinge-to-meeting-stile; the two meeting stiles all but touch

            var rootGo = new GameObject("FrontDoor");
            Transform root = rootGo.transform;
            root.SetParent(building.Root.transform, false);

            Transform hingeL = Child(root, "HingeL");
            hingeL.localPosition = new Vector3(-width / 2f, 0f, 0f);
            Transform hingeR = Child(root, "HingeR");
            hingeR.localPosition = new Vector3(width / 2f, 0f, 0f);
            BuildLeaf(a, hingeL, +1f, leaf, height, depth);
            BuildLeaf(a, hingeR, -1f, leaf, height, depth);

            // Threshold plate across the opening, exit sign above it on the inside, lamp + card reader on the right jamb.
            Block(root, "Threshold", new Vector3(0f, 0.01f, 0f), new Vector3(width, 0.02f, 0.35f), a.MetalDark, collider: false);
            Block(root, "ExitSign", new Vector3(0f, height + 0.35f, -0.18f), new Vector3(0.5f, 0.18f, 0.04f), a.LedGreen, collider: false);
            Block(root, "CardReader", new Vector3(width / 2f + 0.05f, 1.05f, -0.185f), new Vector3(0.09f, 0.14f, 0.02f), a.MetalDark, collider: false);
            GameObject lamp = Block(root, "LockLamp", new Vector3(width / 2f + 0.05f, 1.2f, -0.19f), new Vector3(0.08f, 0.08f, 0.03f), a.LedAmber, collider: false);

            // Shut doors carve the doorway out of the NavMesh so nothing paths through glass.
            var obstacle = rootGo.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = new Vector3(0f, height / 2f, 0f);
            obstacle.size = new Vector3(width, height, 0.6f);
            obstacle.carving = true;
            obstacle.carveOnlyStationary = false;

            var door = rootGo.AddComponent<FrontDoor>();
            door.Configure(level, announcement, hingeL, hingeR, obstacle, lamp.GetComponent<Renderer>(), exteriorVents);

            Layers.SetRecursively(rootGo, Layers.EnvironmentIndex);
            root.SetPositionAndRotation(building.FrontDoorPosition, Quaternion.LookRotation(building.FrontDoorOutward));
            return door;
        }

        /// <summary>One leaf hanging off a hinge: stiles, rails, a glass pane, a push bar, and a single collider for the lot.</summary>
        private static void BuildLeaf(GameAssets a, Transform hinge, float sign, float leaf, float height, float depth)
        {
            float mid = sign * leaf / 2f;
            // Block() positions in world space and the door root is still at the origin with identity
            // rotation here, so hinge-local coordinates only need the hinge's own offset added.
            Vector3 o = hinge.position;
            Block(hinge, "StileHinge", o + new Vector3(sign * 0.03f, height / 2f, 0f), new Vector3(0.06f, height - 0.02f, depth), a.MetalDark, collider: false);
            Block(hinge, "StileMeet", o + new Vector3(sign * (leaf - 0.03f), height / 2f, 0f), new Vector3(0.06f, height - 0.02f, depth), a.MetalDark, collider: false);
            Block(hinge, "RailTop", o + new Vector3(mid, height - 0.04f, 0f), new Vector3(leaf, 0.06f, depth), a.MetalDark, collider: false);
            Block(hinge, "RailBottom", o + new Vector3(mid, 0.16f, 0f), new Vector3(leaf, 0.3f, depth), a.MetalDark, collider: false);
            GameObject pane = Block(hinge, "LeafPane", o + new Vector3(mid, (height + 0.31f) / 2f, 0f), new Vector3(leaf - 0.1f, height - 0.31f - 0.07f, 0.012f), a.WindowGlass, collider: false);
            pane.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Block(hinge, "PushBar", o + new Vector3(mid, 1.0f, -0.07f), new Vector3(leaf - 0.2f, 0.04f, 0.05f), a.MetalGrey, collider: false);

            // One collider for the whole leaf, in hinge space so it swings with it. A touch wider
            // than the leaf so the two overlap at the meeting stiles: a shut door has no crack for
            // a bullet (or the sealed-lobby test's ray) to slip through.
            var box = hinge.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(mid, height / 2f, 0f);
            box.size = new Vector3(leaf + 0.06f, height, 0.06f);
        }

        // ------------------------------------------------------------------ windows & exterior

        /// <summary>
        /// An outer wall: solid below the sill and above the head, piers between the windows, and a
        /// glass pane (collider on, so the building stays sealed and glass is shootable) with a frame
        /// and mullion in each opening. A warm spot light sits outside every window pointing in — the
        /// only light that comes "through" the windows, which is why it never leaks through walls.
        /// </summary>
        private static void BuildWindowWall(Transform parent, Transform lights, GameAssets a, BuildingLayout layout, Vector3 center, bool alongZ,
            float length, float thickness, float height, Vector3 inward, string name, List<Vector3> windowCenters, float doorWidth = 0f, float doorHeight = 0f,
            System.Random rng = null, Transform foliage = null)
        {
            Vector3 along = alongZ ? Vector3.forward : Vector3.right;
            Vector3 Size(float len, float hgt) => alongZ ? new Vector3(thickness, hgt, len) : new Vector3(len, hgt, thickness);
            Transform wall = Child(parent, name);

            float sill = layout.WindowSill, top = Mathf.Min(layout.WindowTop, height - 0.2f), w = layout.WindowWidth;
            int n = Mathf.Max(1, layout.WindowsPerWall);
            float full = length + thickness;
            bool door = doorWidth > 0f;
            doorHeight = Mathf.Min(doorHeight, top); // the "Above" band is the lintel

            if (door)
            {
                // The entrance: the wall below the sill is split around the opening, and static jambs
                // and a header frame it. The leaves themselves come later (BuildFrontDoor).
                float side = full / 2f - doorWidth / 2f;
                Block(wall, "BelowL", center - along * (doorWidth / 2f + side / 2f) + Vector3.up * (sill / 2f), Size(side, sill), a.Wall);
                Block(wall, "BelowR", center + along * (doorWidth / 2f + side / 2f) + Vector3.up * (sill / 2f), Size(side, sill), a.Wall);
                WallTrim(wall, a, center - along * (doorWidth / 2f + side / 2f), alongZ, side, thickness, height, inward);
                WallTrim(wall, a, center + along * (doorWidth / 2f + side / 2f), alongZ, side, thickness, height, inward);
                Vector3 jambSize = Size(0.1f, doorHeight) + (alongZ ? new Vector3(0.04f, 0f, 0f) : new Vector3(0f, 0f, 0.04f));
                Block(wall, "DoorJambL", center - along * (doorWidth / 2f + 0.05f) + Vector3.up * (doorHeight / 2f), jambSize, a.MetalDark);
                Block(wall, "DoorJambR", center + along * (doorWidth / 2f + 0.05f) + Vector3.up * (doorHeight / 2f), jambSize, a.MetalDark);
                Block(wall, "DoorHeader", center + Vector3.up * (doorHeight + 0.04f), Size(doorWidth + 0.2f, 0.08f) + (alongZ ? new Vector3(0.04f, 0f, 0f) : new Vector3(0f, 0f, 0.04f)), a.MetalDark, collider: false);
            }
            else
            {
                Block(wall, "Below", center + Vector3.up * (sill / 2f), Size(full, sill), a.Wall);
                WallTrim(wall, a, center, alongZ, full, thickness, height, inward);
            }

            Block(wall, "Above", center + Vector3.up * (top + (height - top) / 2f), Size(full, height - top), a.Wall);

            // Windows evenly spaced; piers fill the gaps (including the corner overlap at both ends
            // and, on the entrance wall, either side of the door opening).
            var edges = new List<float> { -full / 2f };
            if (door)
            {
                edges.Add(-doorWidth / 2f);
                edges.Add(doorWidth / 2f);
            }

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
                light.range = length * 1.2f; // across this room; the far wall's shadow stops it there
                light.intensity = 16f; // a low sun through a window is bright; it falls off fast
                light.color = new Color(1f, 0.72f, 0.48f); // dusk
                light.shadows = LightShadows.Hard;
                light.shadowStrength = 0.9f;
                light.lightmapBakeType = LightmapBakeType.Mixed;
                // Low tier (256 px): 28 of these fit the 2048 atlas with room to spare, so every window
                // light is shadowed every frame — the shadow is what keeps it inside its room.
                var tier = new SerializedObject(light.GetUniversalAdditionalLightData());
                tier.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
                tier.ApplyModifiedPropertiesWithoutUndo();
                lightGo.layer = Layers.PlayerIndex;
            }

            edges.Add(full / 2f);
            edges.Sort();
            var piers = new List<(float a0, float a1)>();
            for (int i = 0; i < edges.Count; i += 2)
            {
                float a0 = edges[i], a1 = edges[i + 1];
                if (a1 - a0 < 0.01f)
                {
                    continue;
                }

                piers.Add((a0, a1));
                Block(wall, $"Pier{i / 2}", center + along * ((a0 + a1) / 2f) + Vector3.up * ((sill + top) / 2f), Size(a1 - a0, top - sill), a.Wall);
            }

            // The outer face is outdoors: let the sun light it (its shadow map keeps the inner face dark).
            foreach (Renderer r in wall.GetComponentsInChildren<Renderer>())
            {
                r.renderingLayerMask = (1u << 1) | 1u;
            }

            if (rng != null && foliage != null)
            {
                IvyAlong(foliage, a, rng, center, along, -inward, thickness, door ? doorWidth : 0f, piers, height);
            }
        }

        /// <summary>
        /// Ivy on the outer face of a wall: nobody has trimmed it for a while. It climbs the solid piers
        /// between the windows, never across the glass and never across the entrance. Ivy laid along the
        /// base regardless of what was behind it read as a hedge parked against the building, and it sat
        /// over the window sills.
        /// </summary>
        private static void IvyAlong(Transform parent, GameAssets a, System.Random rng, Vector3 wallCenter, Vector3 along, Vector3 outward, float thickness, float keepClear, List<(float a0, float a1)> piers, float wallHeight)
        {
            // A pier has to be wide enough to carry a tile; the tile's own edges thin out, so a little
            // overhang onto the jamb is fine, a tile centred on glass is not.
            var usable = new List<(float a0, float a1)>();
            foreach ((float a0, float a1) in piers)
            {
                if (a1 - a0 < FoliageLibrary.IvyTileWidth * 0.55f)
                {
                    continue;
                }

                float centreAlong = (a0 + a1) / 2f;
                if (keepClear > 0f && Mathf.Abs(centreAlong) < keepClear / 2f + 1.6f)
                {
                    continue;
                }

                usable.Add((a0, a1));
            }

            if (usable.Count == 0)
            {
                return;
            }

            int patches = rng.NextDouble() < 0.3 ? 0 : 1 + rng.Next(3);
            var taken = new HashSet<int>();
            for (int p = 0; p < patches && taken.Count < usable.Count; p++)
            {
                int pick = rng.Next(usable.Count);
                while (!taken.Add(pick))
                {
                    pick = (pick + 1) % usable.Count;
                }

                (float a0, float a1) = usable[pick];
                // Most of the way up the pier: enough to read as a climb, short of the parapet.
                float height = Rand(rng, wallHeight * 0.45f, wallHeight * 0.8f);
                GameObject ivy = FoliageLibrary.IvyTile(parent, a, rng, height, rng.Next(6));
                ivy.transform.SetPositionAndRotation(wallCenter + along * ((a0 + a1) / 2f) + outward * (thickness / 2f), Quaternion.LookRotation(outward, Vector3.up));
                StreetPropLibrary.MarkExterior(ivy);
            }
        }

        /// <summary>
        /// Ground, a sun on its own rendering layer (so it lights only the outside), a dusk skybox and a
        /// few distant building silhouettes. Default layer: not baked into the NavMesh, not shootable.
        /// </summary>
        /// <summary>Clear street between the pavement apron and the nearest far building, metres.</summary>
        public const float ExteriorGap = 6f;

        /// <summary>Distance from a rectangle's centre to its edge along <paramref name="dir"/> (XZ).</summary>
        public static float RectangleRadius(Vector3 dir, float halfWidth, float halfDepth)
        {
            float ax = Mathf.Abs(dir.x), az = Mathf.Abs(dir.z);
            if (ax < 1e-4f) return halfDepth;
            if (az < 1e-4f) return halfWidth;
            return Mathf.Min(halfWidth / ax, halfDepth / az);
        }

        private static void BuildExterior(Transform root, GameAssets a, System.Random rng, float width, float depth, float height, Vector2 clearHalfExtents, bool apron)
        {
            Transform exterior = Child(root, "Exterior");
            const uint exteriorRenderingLayer = 1u << 1;
            bool district = clearHalfExtents.sqrMagnitude > 0f;
            Vector2 clear = district ? clearHalfExtents : new Vector2(width / 2f + 4f, depth / 2f + 4f);

            // Ground: world-scale UVs (Block) so the asphalt tiles per metre rather than stretching once over 800 m.
            GameObject ground = Block(exterior, "Ground", new Vector3(0f, -0.3f, 0f), new Vector3(800f, 0.2f, 800f), district ? a.Dirt : a.Asphalt);
            ground.GetComponent<Renderer>().renderingLayerMask = exteriorRenderingLayer | 1u;
            GameObject apronGo = null;
            if (apron)
            {
                // Pavement apron and kerb right outside the glass so the ground reads at close range;
                // walkable, so the front door leads somewhere even without a district.
                apronGo = Block(exterior, "Apron", new Vector3(0f, -0.15f, 0f), new Vector3(width + 8f, 0.3f, depth + 8f), a.Concrete);
                apronGo.GetComponent<Renderer>().renderingLayerMask = exteriorRenderingLayer | 1u;
            }

            for (int i = 0; i < 14; i++)
            {
                float angle = i * (360f / 14f) + (float)rng.NextDouble() * 15f;
                // Bigger silhouettes when they stand beyond a district: they have to read from 250 m.
                float scale = district ? 1.8f : 1f;
                var size = new Vector3((8f + (float)rng.NextDouble() * 14f) * scale, (6f + (float)rng.NextDouble() * 22f) * scale, (8f + (float)rng.NextDouble() * 14f) * scale);
                // Measured from the clear area's edge in this direction (not its centre), so a bigger
                // building never ends up with a neighbour standing inside its rooms or its streets.
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                float edge = RectangleRadius(dir, clear.x, clear.y);
                float blockRadius = Mathf.Max(size.x, size.z) * 0.71f; // half-diagonal: it is rotated below
                float dist = edge + blockRadius + ExteriorGap + (float)rng.NextDouble() * 45f;
                Vector3 pos = dir * dist;
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
            // Soft shadows now that there is a street to stand in. Stays Realtime (never Mixed): baked
            // GI ignores rendering layers, so a baked sun would pour dusk bounce through the windows
            // into every lightmap.
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            // URP takes a light's rendering layers from its UniversalAdditionalLightData, not from
            // Light.renderingLayerMask. The sun *lights* the exterior layer only (interiors are lit
            // through the windows), but *everything* casts its shadow: otherwise the roof would not
            // block it from a zombie or a car standing in a sunlit room.
            sun.renderingLayerMask = (int)exteriorRenderingLayer;
            UniversalAdditionalLightData sunData = sun.GetUniversalAdditionalLightData();
            sunData.renderingLayers = exteriorRenderingLayer;
            sunData.customShadowLayers = true;
            sunData.shadowRenderingLayers = uint.MaxValue;
            RenderSettings.sun = sun;

            foreach (Transform child in exterior.GetComponentsInChildren<Transform>())
            {
                child.gameObject.layer = 0; // Default: no NavMesh, no bullet hits, no occlusion queries
            }

            if (apronGo != null)
            {
                apronGo.layer = Layers.EnvironmentIndex; // the one exterior surface that is walkable
            }
        }

        /// <summary>
        /// Light probes in a grid through every room (two heights, so a standing zombie and a toppled
        /// one both pick up the right bounce), and one box-projected reflection probe per room that
        /// sees only the Environment layer — never the sky, never the zombies.
        /// </summary>
        private static void BuildProbes(Transform root, List<Vector3> roomCenters, float cell, float height)
        {
            Transform probes = Child(root, "Probes");
            var group = probes.gameObject.AddComponent<LightProbeGroup>();
            var positions = new List<Vector3>();
            int per = Mathf.Max(2, Mathf.RoundToInt(cell / 4f)); // ~4 m apart
            foreach (Vector3 center in roomCenters)
            {
                for (int z = 0; z < per; z++)
                {
                    for (int x = 0; x < per; x++)
                    {
                        var at = new Vector3(
                            center.x + (x - (per - 1) / 2f) * (cell * 0.8f / (per - 1)),
                            0f,
                            center.z + (z - (per - 1) / 2f) * (cell * 0.8f / (per - 1)));
                        positions.Add(at + Vector3.up * 0.5f);
                        positions.Add(at + Vector3.up * (height - 0.7f));
                    }
                }

                var probeGo = new GameObject($"Reflection_{center.x:0}_{center.z:0}");
                probeGo.transform.SetParent(probes, false);
                probeGo.transform.position = center + Vector3.up * (height / 2f);
                var probe = probeGo.AddComponent<ReflectionProbe>();
                // Rendered once when the scene loads, on the player's GPU. A headless editor bake wrote
                // garbage cubemaps (see SceneRendersTests, which guards against the magenta that caused).
                probe.mode = ReflectionProbeMode.Realtime;
                probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
                probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
                probe.size = new Vector3(cell, height, cell);
                probe.boxProjection = true;
                probe.resolution = 128;
                probe.hdr = true;
                probe.cullingMask = 1 << Layers.EnvironmentIndex;
                probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
                probe.backgroundColor = new Color(0.02f, 0.02f, 0.025f);
                probe.importance = 1;
            }

            group.probePositions = positions.ToArray();
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
            /// <summary>Floor area relative to the 10 m room the counts below were tuned for.</summary>
            private readonly float area;

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
                area = (cellSize / 10f) * (cellSize / 10f);
            }

            /// <summary>A count tuned for a 10 m room, scaled to this room's floor area.</summary>
            private int Scaled(int count) => Mathf.Max(1, Mathf.RoundToInt(count * area));

            public void Dress(RoomType type)
            {
                switch (type)
                {
                    case RoomType.Office:
                        Litter();
                        int desks = Scaled(2) + rng.Next(2);
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

                        for (int i = 0; i < Scaled(1); i++) Wall(PropLibrary.Kind.FilingCabinet);
                        if (rng.NextDouble() < 0.7) Wall(PropLibrary.Kind.FilingCabinet);
                        for (int i = 0; i < Scaled(1); i++) if (rng.NextDouble() < 0.6) Wall(PropLibrary.Kind.Bookshelf);
                        if (rng.NextDouble() < 0.5) Wall(PropLibrary.Kind.Whiteboard);
                        for (int i = 0; i < Scaled(1); i++) if (rng.NextDouble() < 0.6) Free(PropLibrary.Kind.PottedPlant, Rand(rng, 0f, 360f));
                        if (rng.NextDouble() < 0.55) Corner(PropLibrary.Kind.PlantLarge);
                        for (int i = 0; i < Scaled(1); i++) if (rng.NextDouble() < 0.4) Free(PropLibrary.Kind.CubicleWall, rng.Next(2) * 90f);
                        break;

                    case RoomType.Conference:
                        float tableYaw = rng.Next(2) * 90f;
                        // One table in a 10 m room; a bigger room seats a row of them along its axis.
                        int tables = Scaled(1);
                        Vector3 tableAxis = Quaternion.Euler(0f, tableYaw, 0f) * Vector3.forward;
                        for (int n = 0; n < tables; n++)
                        {
                            Vector3 tableAt = center + tableAxis * ((n - (tables - 1) / 2f) * 4.2f);
                            GameObject table = At(PropLibrary.Kind.ConferenceTable, tableAt, tableYaw);
                            if (table == null)
                            {
                                continue;
                            }

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
                        if (rng.NextDouble() < 0.8) Corner(PropLibrary.Kind.PlantLarge);
                        for (int i = 0; i < Scaled(1); i++) if (rng.NextDouble() < 0.6) Corner(PropLibrary.Kind.PottedPlant);
                        if (area > 1.5f) Wall(PropLibrary.Kind.Bookshelf);
                        Litter();
                        break;

                    case RoomType.BreakRoom:
                        Litter();
                        Wall(PropLibrary.Kind.VendingMachine);
                        Wall(PropLibrary.Kind.VendingMachine);
                        Wall(PropLibrary.Kind.WaterCooler);
                        for (int i = 0; i < Scaled(1); i++) Wall(PropLibrary.Kind.Couch);
                        Free(PropLibrary.Kind.TrashBin, 0f);
                        for (int n = 0; n < Scaled(1); n++)
                        {
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
                        }

                        for (int i = 0; i < Scaled(1); i++) if (rng.NextDouble() < 0.6) Free(PropLibrary.Kind.PottedPlant, Rand(rng, 0f, 360f));
                        if (rng.NextDouble() < 0.6) Corner(PropLibrary.Kind.PlantLarge);
                        break;

                    case RoomType.Lobby:
                        Litter();
                        Wall(PropLibrary.Kind.ReceptionCounter);
                        for (int i = 0; i < Scaled(1); i++) Wall(PropLibrary.Kind.Couch);
                        if (rng.NextDouble() < 0.7) Wall(PropLibrary.Kind.Couch);
                        for (int i = 0; i < Scaled(2); i++) Corner(PropLibrary.Kind.PlantLarge);
                        for (int i = 0; i < Scaled(1); i++) Free(PropLibrary.Kind.PottedPlant, Rand(rng, 0f, 360f));
                        for (int i = 0; i < Scaled(1); i++) Wall(PropLibrary.Kind.Bookshelf);
                        if (area > 1.5f) Wall(PropLibrary.Kind.Whiteboard);
                        Wall(PropLibrary.Kind.TrashBin);
                        break;

                    case RoomType.Storage:
                        Litter(posters: 0);
                        int units = Scaled(3) + rng.Next(3);
                        for (int i = 0; i < units; i++) Wall(PropLibrary.Kind.Shelving);
                        for (int i = 0; i < Scaled(1); i++) if (rng.NextDouble() < 0.7) Free(PropLibrary.Kind.Shelving, rng.Next(2) * 90f);
                        if (rng.NextDouble() < 0.5) Wall(PropLibrary.Kind.Copier);
                        break;

                    case RoomType.ServerRoom:
                        // Two rows of racks facing an aisle down the middle of the room.
                        float rowYaw = rng.Next(2) * 90f;
                        var rowAxis = Quaternion.Euler(0f, rowYaw, 0f) * Vector3.right;
                        var rowNormal = Quaternion.Euler(0f, rowYaw, 0f) * Vector3.forward;
                        int perRow = Mathf.RoundToInt((3 + rng.Next(2)) * Mathf.Sqrt(area));
                        int rowPairs = area > 1.5f ? 2 : 1; // a big room gets two aisles
                        for (int pair = 0; pair < rowPairs; pair++)
                        {
                            float aisle = (pair - (rowPairs - 1) / 2f) * 4.6f;
                            for (int row = -1; row <= 1; row += 2)
                            {
                                for (int i = 0; i < perRow; i++)
                                {
                                    Vector3 pos = center + rowAxis * ((i - (perRow - 1) / 2f) * 0.75f) + rowNormal * (aisle + row * 1.4f);
                                    At(PropLibrary.Kind.ServerRack, pos, rowYaw + (row > 0 ? 180f : 0f));
                                }
                            }
                        }

                        Wall(PropLibrary.Kind.FilingCabinet);
                        break;
                }
            }

            /// <summary>Posters on the walls and paper on the floor: the wear every room gets regardless of purpose.</summary>
            private void Litter(int posters = -1)
            {
                int posterCount = posters >= 0 ? posters : Scaled(1) + rng.Next(2);
                for (int i = 0; i < posterCount; i++)
                {
                    Wall(PropLibrary.Kind.Poster);
                }

                int piles = rng.NextDouble() < 0.75 ? Scaled(1) : 0;
                for (int i = 0; i < piles; i++)
                {
                    Vector2 fp = PropLibrary.Footprint(PropLibrary.Kind.PaperScatter);
                    for (int attempt = 0; attempt < 8; attempt++)
                    {
                        float half = cell / 2f - 1.2f;
                        Vector3 pos = center + new Vector3(Rand(rng, -half, half), 0f, Rand(rng, -half, half));
                        if (Fits(pos, fp, ignoreProps: true))
                        {
                            GameObject go = PropLibrary.Build(PropLibrary.Kind.PaperScatter, a, rng, parent);
                            go.transform.position = pos; // not recorded: litter never blocks furniture
                            break;
                        }
                    }
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

            /// <summary>Into a corner of the room, turned to face the middle: where a big plant goes.</summary>
            private GameObject Corner(PropLibrary.Kind kind)
            {
                Vector2 fp = PropLibrary.Footprint(kind);
                float inset = cell / 2f - wall / 2f - Mathf.Max(fp.x, fp.y) / 2f - 0.25f;
                var corners = new List<Vector3> { new(-inset, 0f, -inset), new(inset, 0f, -inset), new(-inset, 0f, inset), new(inset, 0f, inset) };
                Shuffle(corners, rng);
                foreach (Vector3 corner in corners)
                {
                    Vector3 pos = center + corner;
                    if (Fits(pos, fp))
                    {
                        float yaw = Mathf.Atan2(-corner.x, -corner.z) * Mathf.Rad2Deg + Rand(rng, -20f, 20f);
                        return Spawn(kind, pos, Quaternion.Euler(0f, yaw, 0f), fp);
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
                WallTrim(wall, a, center, alongZ, length + thickness, thickness, height);
                return;
            }

            float side = (length - doorWidth) / 2f;
            float segment = side + thickness / 2f;              // extends into the corner like solid walls do
            float sideOffset = doorWidth / 2f + segment / 2f;   // inner edge lands exactly on the door opening
            Block(wall, "Left", center - along * sideOffset + Vector3.up * (height / 2f), Size(segment, height), a.Wall);
            Block(wall, "Right", center + along * sideOffset + Vector3.up * (height / 2f), Size(segment, height), a.Wall);
            WallTrim(wall, a, center - along * sideOffset, alongZ, segment, thickness, height);
            WallTrim(wall, a, center + along * sideOffset, alongZ, segment, thickness, height);
            float lintel = height - doorHeight;
            Block(wall, "Lintel", center + Vector3.up * (doorHeight + lintel / 2f), Size(doorWidth + 0.02f, lintel), a.Wall);

            // Door frame trim: purely visual, reads as a doorway from a distance.
            Vector3 frameSize = alongZ ? new Vector3(thickness + 0.04f, doorHeight, 0.08f) : new Vector3(0.08f, doorHeight, thickness + 0.04f);
            Block(wall, "FrameL", center - along * (doorWidth / 2f) + Vector3.up * (doorHeight / 2f), frameSize, a.Trim, collider: false);
            Block(wall, "FrameR", center + along * (doorWidth / 2f) + Vector3.up * (doorHeight / 2f), frameSize, a.Trim, collider: false);
        }

        /// <summary>Skirting board at the floor and a cornice at the ceiling on both faces of a wall piece (or one, if <paramref name="onlyFace"/> is given).</summary>
        private static void WallTrim(Transform wall, GameAssets a, Vector3 center, bool alongZ, float length, float thickness, float height, Vector3? onlyFace = null)
        {
            const float skirtH = 0.12f, skirtD = 0.018f, corniceH = 0.07f, corniceD = 0.03f;
            Vector3 normal = alongZ ? Vector3.right : Vector3.forward;
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 face = normal * side;
                if (onlyFace.HasValue && Vector3.Dot(face, onlyFace.Value) < 0.5f)
                {
                    continue;
                }

                Vector3 skirtSize = alongZ ? new Vector3(skirtD, skirtH, length) : new Vector3(length, skirtH, skirtD);
                Vector3 corniceSize = alongZ ? new Vector3(corniceD, corniceH, length) : new Vector3(length, corniceH, corniceD);
                Block(wall, side < 0 ? "SkirtingA" : "SkirtingB", center + face * (thickness / 2f + skirtD / 2f) + Vector3.up * (skirtH / 2f), skirtSize, a.Trim, collider: false);
                Block(wall, side < 0 ? "CorniceA" : "CorniceB", center + face * (thickness / 2f + corniceD / 2f) + Vector3.up * (height - corniceH / 2f), corniceSize, a.Wall, collider: false);
            }
        }

        /// <summary>
        /// The things a real ceiling has that a box does not: a metal frame recessing each light panel, a
        /// cable tray hugging one wall, a duct run across the room, and the odd water stain on the tiles.
        /// </summary>
        private static void DressCeiling(Transform room, GameAssets a, System.Random rng, Vector3 center, float cell, float wallThickness, float h, int panels, float pitch)
        {
            for (int pz = 0; pz < panels; pz++)
            {
                for (int px = 0; px < panels; px++)
                {
                    Vector3 at = center + new Vector3((px - (panels - 1) / 2f) * pitch, 0f, (pz - (panels - 1) / 2f) * pitch);
                    Block(room, $"PanelFrame_{px}_{pz}", at + Vector3.up * (h - 0.015f), new Vector3(1.72f, 0.03f, 1.72f), a.MetalGrey, collider: false);
                }
            }

            // Cable tray along one wall, 60 cm in from it, with a conduit riding beside it.
            bool alongX = rng.NextDouble() < 0.5;
            float side = rng.NextDouble() < 0.5 ? -1f : 1f;
            float inset = cell / 2f - wallThickness - 0.6f;
            Vector3 trayCenter = center + (alongX ? Vector3.forward : Vector3.right) * (side * inset) + Vector3.up * (h - 0.14f);
            Vector3 traySize = alongX ? new Vector3(cell - 1.2f, 0.06f, 0.22f) : new Vector3(0.22f, 0.06f, cell - 1.2f);
            Block(room, "CableTray", trayCenter, traySize, a.MetalDark, collider: false);
            Vector3 conduitOffset = (alongX ? Vector3.forward : Vector3.right) * (-side * 0.22f);
            Vector3 conduitSize = alongX ? new Vector3(cell - 1.2f, 0.05f, 0.05f) : new Vector3(0.05f, 0.05f, cell - 1.2f);
            Block(room, "Conduit", trayCenter + conduitOffset + Vector3.up * 0.02f, conduitSize, a.MetalGrey, collider: false);

            // A duct across the room, just under the ceiling.
            if (rng.NextDouble() < 0.7)
            {
                float where = Rand(rng, -cell * 0.3f, cell * 0.3f);
                Vector3 ductCenter = center + (alongX ? Vector3.right : Vector3.forward) * where + Vector3.up * (h - 0.25f);
                Vector3 ductSize = alongX ? new Vector3(0.45f, 0.35f, cell - 0.4f) : new Vector3(cell - 0.4f, 0.35f, 0.45f);
                Block(room, "Duct", ductCenter, ductSize, a.VentMetal, collider: false);
            }

            // Water stains on the tiles.
            int stains = rng.NextDouble() < 0.6 ? 1 + rng.Next(2) : 0;
            for (int i = 0; i < stains; i++)
            {
                Vector3 at = center + new Vector3(Rand(rng, -cell * 0.4f, cell * 0.4f), h - 0.004f, Rand(rng, -cell * 0.4f, cell * 0.4f));
                float size = Rand(rng, 0.8f, 1.8f);
                Block(room, $"CeilingStain{i}", at, new Vector3(size, 0.004f, size * Rand(rng, 0.6f, 1.2f)), a.Stain, collider: false);
            }
        }

        /// <summary>Grime under a vent grate: a smear where the air has been blowing dust for years, and a drip streak down the wall.</summary>
        private static void VentGrime(Transform parent, GameAssets a, System.Random rng, Vector3 grate, Vector3 normal, float ventHeight)
        {
            Vector3 along = Vector3.Cross(Vector3.up, normal);
            Vector3 face = grate + normal * 0.012f; // just proud of the wall
            Vector3 SizeOf(float w, float hgt) => new Vector3(Mathf.Abs(along.x) * w + Mathf.Abs(normal.x) * 0.006f, hgt, Mathf.Abs(along.z) * w + Mathf.Abs(normal.z) * 0.006f);
            float smearH = Rand(rng, 0.8f, 1.3f);
            Block(parent, "VentSmear", new Vector3(face.x, ventHeight - 0.3f - smearH / 2f, face.z), SizeOf(Rand(rng, 0.9f, 1.3f), smearH), a.Stain, collider: false);
            if (rng.NextDouble() < 0.7)
            {
                float dripH = Rand(rng, 1.2f, ventHeight - 0.3f);
                Vector3 dripAt = face + along * Rand(rng, -0.35f, 0.35f);
                Block(parent, "VentDrip", new Vector3(dripAt.x, ventHeight - 0.35f - dripH / 2f, dripAt.z), SizeOf(Rand(rng, 0.06f, 0.14f), dripH), a.Stain, collider: false);
            }
        }

        private static GameObject Block(Transform parent, string name, Vector3 worldCenter, Vector3 size, Material material, bool collider = true)
        {
            GameObject go = PrefabFactory.Primitive(PrimitiveType.Cube, name, parent, Vector3.zero, size, material, collider);
            // World-scale UVs: textures tile per metre on every block regardless of its size.
            go.GetComponent<MeshFilter>().sharedMesh = MeshLibrary.WorldCube(size);
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
