using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
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
        }

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
                    light.intensity = 2.6f;
                    light.color = new Color(1f, 0.93f, 0.8f);
                    light.shadows = LightShadows.None;
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
                    BuildWall(geometry, a, wallCenter, alongZ: true, cell, t, h, hasDoor, layout.DoorWidth, layout.DoorHeight, $"WallX_{c}_{r}");
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
                    BuildWall(geometry, a, wallCenter, alongZ: false, cell, t, h, hasDoor, layout.DoorWidth, layout.DoorHeight, $"WallZ_{c}_{r}");
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
                        float offset = (float)(rng.NextDouble() * (cell / 2f - 2.6f) + 1.6f) * (rng.NextDouble() < 0.5 ? -1f : 1f);
                        Vector3 wallInner = center - normal * (cell / 2f - t / 2f) + along * offset;
                        var pos = new Vector3(wallInner.x, layout.VentHeight, wallInner.z);

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

            // ---- Props: cover, avoiding doors, vent landings and the spawn ----------------------
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    Vector3 center = CellCenter(c, r, cols, rows, cell);
                    int count = rng.Next(1, 4);
                    for (int i = 0; i < count; i++)
                    {
                        bool desk = rng.NextDouble() < 0.4;
                        Vector3 size = desk ? new Vector3(1.8f, 0.8f, 0.9f) : new Vector3(1f, 1f, 1f);
                        Vector3 pos = Vector3.zero;
                        bool placed = false;
                        for (int attempt = 0; attempt < 12 && !placed; attempt++)
                        {
                            float half = cell / 2f - 2f;
                            pos = center + new Vector3(Rand(rng, -half, half), size.y / 2f, Rand(rng, -half, half));
                            placed = Clear(pos, doorCenters, 2.2f) && Clear(pos, ventFloorPoints, 1.6f)
                                     && Vector3.Distance(pos, spawnCenter) > 2.5f;
                        }

                        if (!placed)
                        {
                            continue;
                        }

                        GameObject prop = Block(props, desk ? "Desk" : "Crate", pos, size, desk ? a.PropAlt : a.Prop);
                        prop.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    }
                }
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
            float sideOffset = doorWidth / 2f + side / 2f;
            Block(wall, "Left", center - along * sideOffset + Vector3.up * (height / 2f), Size(side + thickness / 2f, height), a.Wall);
            Block(wall, "Right", center + along * sideOffset + Vector3.up * (height / 2f), Size(side + thickness / 2f, height), a.Wall);
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
