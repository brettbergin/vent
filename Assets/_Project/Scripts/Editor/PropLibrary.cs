using UnityEngine;
using Vent.Core.Utility;

namespace Vent.Editor
{
    /// <summary>
    /// Commercial-building furniture built from primitives. Each builder returns a root whose
    /// origin is the centre of its floor footprint, facing +Z, sized in metres; every piece has a
    /// collider on the Environment layer so it blocks bullets, carves the NavMesh and reads as
    /// cover. <see cref="Footprint"/> tells the generator how much floor each one needs.
    /// </summary>
    public static class PropLibrary
    {
        public enum Kind
        {
            Desk, OfficeChair, FilingCabinet, Bookshelf, WaterCooler, PottedPlant, ConferenceTable, VendingMachine,
            Couch, ReceptionCounter, ServerRack, Shelving, TrashBin, Whiteboard, Copier, CubicleWall,
        }

        /// <summary>Floor footprint (x = width across the front, y = depth), metres.</summary>
        public static Vector2 Footprint(Kind kind) => kind switch
        {
            Kind.Desk => new Vector2(1.6f, 0.8f),
            Kind.OfficeChair => new Vector2(0.6f, 0.6f),
            Kind.FilingCabinet => new Vector2(0.5f, 0.6f),
            Kind.Bookshelf => new Vector2(1.2f, 0.4f),
            Kind.WaterCooler => new Vector2(0.4f, 0.4f),
            Kind.PottedPlant => new Vector2(0.5f, 0.5f),
            Kind.ConferenceTable => new Vector2(3.2f, 1.2f),
            Kind.VendingMachine => new Vector2(0.9f, 0.8f),
            Kind.Couch => new Vector2(2.0f, 0.9f),
            Kind.ReceptionCounter => new Vector2(2.6f, 0.8f),
            Kind.ServerRack => new Vector2(0.6f, 1.0f),
            Kind.Shelving => new Vector2(1.8f, 0.6f),
            Kind.TrashBin => new Vector2(0.4f, 0.4f),
            Kind.Whiteboard => new Vector2(1.8f, 0.1f),
            Kind.Copier => new Vector2(1.0f, 0.7f),
            Kind.CubicleWall => new Vector2(1.8f, 0.1f),
            _ => Vector2.one,
        };

        /// <summary>True for pieces that belong flat against a wall, back to it.</summary>
        public static bool WallMounted(Kind kind) => kind is Kind.FilingCabinet or Kind.Bookshelf or Kind.WaterCooler or Kind.VendingMachine
            or Kind.ServerRack or Kind.Shelving or Kind.Whiteboard or Kind.Copier or Kind.Couch;

        public static GameObject Build(Kind kind, GameAssets a, System.Random rng, Transform parent)
        {
            var root = new GameObject(kind.ToString());
            root.transform.SetParent(parent, false);
            Transform t = root.transform;
            switch (kind)
            {
                case Kind.Desk: Desk(t, a, rng); break;
                case Kind.OfficeChair: OfficeChair(t, a); break;
                case Kind.FilingCabinet: FilingCabinet(t, a); break;
                case Kind.Bookshelf: Bookshelf(t, a, rng); break;
                case Kind.WaterCooler: WaterCooler(t, a); break;
                case Kind.PottedPlant: PottedPlant(t, a, rng); break;
                case Kind.ConferenceTable: ConferenceTable(t, a); break;
                case Kind.VendingMachine: VendingMachine(t, a); break;
                case Kind.Couch: Couch(t, a); break;
                case Kind.ReceptionCounter: ReceptionCounter(t, a); break;
                case Kind.ServerRack: ServerRack(t, a, rng); break;
                case Kind.Shelving: Shelving(t, a, rng); break;
                case Kind.TrashBin: TrashBin(t, a); break;
                case Kind.Whiteboard: Whiteboard(t, a); break;
                case Kind.Copier: Copier(t, a); break;
                case Kind.CubicleWall: CubicleWall(t, a); break;
            }

            Layers.SetRecursively(root, Layers.EnvironmentIndex);
            return root;
        }

        // ------------------------------------------------------------------ pieces

        private static void Desk(Transform t, GameAssets a, System.Random rng)
        {
            Box(t, "Top", new Vector3(0f, 0.74f, 0f), new Vector3(1.6f, 0.04f, 0.8f), a.Wood);
            Box(t, "Modesty", new Vector3(0f, 0.45f, -0.3f), new Vector3(1.5f, 0.5f, 0.03f), a.Wood, collider: false);
            Box(t, "Pedestal", new Vector3(0.55f, 0.35f, 0.05f), new Vector3(0.42f, 0.7f, 0.6f), a.MetalGrey);
            for (int i = 0; i < 3; i++)
            {
                Box(t, $"Drawer{i}", new Vector3(0.55f, 0.13f + i * 0.22f, 0.36f), new Vector3(0.36f, 0.18f, 0.01f), a.Plastic, collider: false);
                Box(t, $"Handle{i}", new Vector3(0.55f, 0.13f + i * 0.22f, 0.37f), new Vector3(0.12f, 0.015f, 0.01f), a.MetalGrey, collider: false);
            }

            Box(t, "LegL", new Vector3(-0.72f, 0.36f, 0f), new Vector3(0.05f, 0.72f, 0.7f), a.MetalGrey);
            // Monitor on a stand, keyboard, a mug or a stack of paper.
            Box(t, "MonitorStand", new Vector3(-0.2f, 0.8f, 0.2f), new Vector3(0.2f, 0.08f, 0.16f), a.Plastic, collider: false);
            Box(t, "MonitorPost", new Vector3(-0.2f, 0.9f, 0.22f), new Vector3(0.04f, 0.2f, 0.03f), a.Plastic, collider: false);
            Box(t, "Monitor", new Vector3(-0.2f, 1.05f, 0.22f), new Vector3(0.55f, 0.34f, 0.03f), a.Plastic, collider: false);
            Box(t, "Screen", new Vector3(-0.2f, 1.05f, 0.203f), new Vector3(0.5f, 0.29f, 0.005f), a.Screen, collider: false);
            Box(t, "Keyboard", new Vector3(-0.2f, 0.775f, -0.1f), new Vector3(0.44f, 0.02f, 0.15f), a.Plastic, collider: false);
            if (rng.NextDouble() < 0.6)
            {
                Box(t, "Paper", new Vector3(0.25f, 0.775f, -0.05f), new Vector3(0.22f, 0.03f, 0.3f), a.Paper, collider: false);
            }
            else
            {
                Cyl(t, "Mug", new Vector3(0.2f, 0.81f, -0.15f), 0.04f, 0.05f, a.Paper, collider: false);
            }
        }

        private static void OfficeChair(Transform t, GameAssets a)
        {
            Cyl(t, "Base", new Vector3(0f, 0.03f, 0f), 0.3f, 0.02f, a.Plastic, collider: false);
            for (int i = 0; i < 5; i++)
            {
                GameObject spoke = Box(t, $"Spoke{i}", new Vector3(0f, 0.04f, 0f), new Vector3(0.05f, 0.03f, 0.56f), a.Plastic, collider: false);
                spoke.transform.localRotation = Quaternion.Euler(0f, i * 72f, 0f);
            }

            Cyl(t, "Post", new Vector3(0f, 0.25f, 0f), 0.03f, 0.2f, a.MetalGrey);
            Box(t, "Seat", new Vector3(0f, 0.48f, 0f), new Vector3(0.5f, 0.08f, 0.5f), a.Fabric);
            Box(t, "Back", new Vector3(0f, 0.78f, -0.23f), new Vector3(0.46f, 0.5f, 0.06f), a.Fabric);
            Box(t, "ArmL", new Vector3(-0.26f, 0.65f, -0.02f), new Vector3(0.04f, 0.04f, 0.3f), a.Plastic, collider: false);
            Box(t, "ArmR", new Vector3(0.26f, 0.65f, -0.02f), new Vector3(0.04f, 0.04f, 0.3f), a.Plastic, collider: false);
        }

        private static void FilingCabinet(Transform t, GameAssets a)
        {
            Box(t, "Body", new Vector3(0f, 0.66f, 0f), new Vector3(0.5f, 1.32f, 0.6f), a.MetalGrey);
            for (int i = 0; i < 4; i++)
            {
                Box(t, $"Drawer{i}", new Vector3(0f, 0.18f + i * 0.31f, 0.302f), new Vector3(0.44f, 0.27f, 0.004f), a.MetalDark, collider: false);
                Box(t, $"Handle{i}", new Vector3(0f, 0.2f + i * 0.31f, 0.31f), new Vector3(0.14f, 0.02f, 0.012f), a.MetalGrey, collider: false);
                Box(t, $"Label{i}", new Vector3(0f, 0.28f + i * 0.31f, 0.306f), new Vector3(0.08f, 0.04f, 0.004f), a.Paper, collider: false);
            }
        }

        private static void Bookshelf(Transform t, GameAssets a, System.Random rng)
        {
            Box(t, "SideL", new Vector3(-0.59f, 0.9f, 0f), new Vector3(0.02f, 1.8f, 0.4f), a.Wood);
            Box(t, "SideR", new Vector3(0.59f, 0.9f, 0f), new Vector3(0.02f, 1.8f, 0.4f), a.Wood);
            Box(t, "Back", new Vector3(0f, 0.9f, -0.19f), new Vector3(1.2f, 1.8f, 0.02f), a.Wood);
            Box(t, "Top", new Vector3(0f, 1.79f, 0f), new Vector3(1.2f, 0.02f, 0.4f), a.Wood);
            for (int s = 0; s < 4; s++)
            {
                float y = 0.02f + s * 0.44f;
                Box(t, $"Shelf{s}", new Vector3(0f, y, 0f), new Vector3(1.18f, 0.02f, 0.38f), a.Wood, collider: s == 0);
                // Books: a run of varied thickness and colour, leaving a gap here and there.
                float x = -0.56f;
                while (x < 0.5f)
                {
                    float w = 0.03f + (float)rng.NextDouble() * 0.05f;
                    if (rng.NextDouble() < 0.12)
                    {
                        x += 0.12f;
                        continue;
                    }

                    float h = 0.22f + (float)rng.NextDouble() * 0.14f;
                    Material m = rng.Next(3) switch { 0 => a.BookA, 1 => a.BookB, _ => a.BookC };
                    Box(t, "Book", new Vector3(x + w / 2f, y + 0.01f + h / 2f, 0.02f), new Vector3(w, h, 0.26f), m, collider: false);
                    x += w + 0.004f;
                }
            }
        }

        private static void WaterCooler(Transform t, GameAssets a)
        {
            Box(t, "Cabinet", new Vector3(0f, 0.5f, 0f), new Vector3(0.36f, 1.0f, 0.36f), a.Paper);
            Box(t, "Tray", new Vector3(0f, 0.72f, 0.19f), new Vector3(0.3f, 0.03f, 0.06f), a.Plastic, collider: false);
            Box(t, "Tap", new Vector3(0f, 0.85f, 0.19f), new Vector3(0.05f, 0.05f, 0.06f), a.Plastic, collider: false);
            Cyl(t, "Bottle", new Vector3(0f, 1.24f, 0f), 0.14f, 0.24f, a.Glass);
            Cyl(t, "Neck", new Vector3(0f, 1.02f, 0f), 0.06f, 0.03f, a.Glass, collider: false);
        }

        private static void PottedPlant(Transform t, GameAssets a, System.Random rng)
        {
            Cyl(t, "Pot", new Vector3(0f, 0.2f, 0f), 0.2f, 0.2f, a.Terracotta);
            Cyl(t, "Soil", new Vector3(0f, 0.395f, 0f), 0.17f, 0.005f, a.MetalDark, collider: false);
            Cyl(t, "Stem", new Vector3(0f, 0.7f, 0f), 0.02f, 0.35f, a.Wood, collider: false);
            int leaves = 5 + rng.Next(3);
            for (int i = 0; i < leaves; i++)
            {
                float ang = i * (360f / leaves) + (float)rng.NextDouble() * 20f;
                Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                GameObject leaf = Box(t, $"Leaf{i}", new Vector3(0f, 1.0f, 0f) + dir * 0.22f, new Vector3(0.12f, 0.03f, 0.45f), a.Plant, collider: false);
                leaf.transform.localRotation = Quaternion.Euler(-35f - (float)rng.NextDouble() * 20f, ang, 0f);
            }

            Sphere(t, "Crown", new Vector3(0f, 1.1f, 0f), 0.22f, a.Plant, collider: false);
        }

        private static void ConferenceTable(Transform t, GameAssets a)
        {
            Box(t, "Top", new Vector3(0f, 0.74f, 0f), new Vector3(3.2f, 0.05f, 1.2f), a.Wood);
            Box(t, "PedestalL", new Vector3(-1.1f, 0.36f, 0f), new Vector3(0.5f, 0.72f, 0.6f), a.MetalGrey);
            Box(t, "PedestalR", new Vector3(1.1f, 0.36f, 0f), new Vector3(0.5f, 0.72f, 0.6f), a.MetalGrey);
            Box(t, "Beam", new Vector3(0f, 0.6f, 0f), new Vector3(2.2f, 0.06f, 0.1f), a.MetalGrey, collider: false);
            Box(t, "Phone", new Vector3(0f, 0.78f, 0f), new Vector3(0.2f, 0.04f, 0.2f), a.Plastic, collider: false);
        }

        private static void VendingMachine(Transform t, GameAssets a)
        {
            Box(t, "Body", new Vector3(0f, 0.95f, 0f), new Vector3(0.9f, 1.9f, 0.8f), a.VendingRed);
            Box(t, "Glass", new Vector3(-0.12f, 1.15f, 0.402f), new Vector3(0.55f, 1.2f, 0.005f), a.Glass, collider: false);
            for (int row = 0; row < 5; row++)
            {
                Box(t, $"Shelf{row}", new Vector3(-0.12f, 0.62f + row * 0.24f, 0.39f), new Vector3(0.52f, 0.01f, 0.01f), a.MetalGrey, collider: false);
                for (int col = 0; col < 4; col++)
                {
                    Material m = (row + col) % 3 == 0 ? a.BookA : (row + col) % 3 == 1 ? a.BookC : a.Paper;
                    Cyl(t, "Can", new Vector3(-0.3f + col * 0.12f, 0.68f + row * 0.24f, 0.38f), 0.03f, 0.055f, m, collider: false);
                }
            }

            Box(t, "Panel", new Vector3(0.3f, 1.3f, 0.402f), new Vector3(0.2f, 0.5f, 0.005f), a.MetalDark, collider: false);
            Box(t, "Keypad", new Vector3(0.3f, 1.35f, 0.406f), new Vector3(0.1f, 0.14f, 0.004f), a.Screen, collider: false);
            Box(t, "Flap", new Vector3(-0.12f, 0.3f, 0.402f), new Vector3(0.5f, 0.2f, 0.005f), a.MetalDark, collider: false);
            Box(t, "Sign", new Vector3(0f, 1.82f, 0.402f), new Vector3(0.8f, 0.1f, 0.005f), a.LightPanel, collider: false);
        }

        private static void Couch(Transform t, GameAssets a)
        {
            Box(t, "Seat", new Vector3(0f, 0.25f, 0.05f), new Vector3(2.0f, 0.5f, 0.8f), a.Fabric);
            Box(t, "Back", new Vector3(0f, 0.6f, -0.35f), new Vector3(2.0f, 0.5f, 0.2f), a.Fabric);
            Box(t, "ArmL", new Vector3(-0.95f, 0.4f, 0.05f), new Vector3(0.1f, 0.3f, 0.8f), a.Fabric);
            Box(t, "ArmR", new Vector3(0.95f, 0.4f, 0.05f), new Vector3(0.1f, 0.3f, 0.8f), a.Fabric);
            Box(t, "CushionL", new Vector3(-0.45f, 0.52f, 0.1f), new Vector3(0.85f, 0.06f, 0.7f), a.FabricLight, collider: false);
            Box(t, "CushionR", new Vector3(0.45f, 0.52f, 0.1f), new Vector3(0.85f, 0.06f, 0.7f), a.FabricLight, collider: false);
        }

        private static void ReceptionCounter(Transform t, GameAssets a)
        {
            Box(t, "Front", new Vector3(0f, 0.55f, 0.3f), new Vector3(2.6f, 1.1f, 0.06f), a.Wood);
            Box(t, "Ledge", new Vector3(0f, 1.12f, 0.3f), new Vector3(2.7f, 0.04f, 0.3f), a.MetalGrey);
            Box(t, "Worktop", new Vector3(0f, 0.75f, -0.1f), new Vector3(2.5f, 0.04f, 0.7f), a.Wood);
            Box(t, "Base", new Vector3(0f, 0.35f, -0.1f), new Vector3(2.4f, 0.7f, 0.6f), a.MetalGrey);
            Box(t, "Monitor", new Vector3(-0.6f, 1.0f, -0.1f), new Vector3(0.45f, 0.3f, 0.03f), a.Plastic, collider: false);
            Box(t, "Screen", new Vector3(-0.6f, 1.0f, -0.083f), new Vector3(0.4f, 0.25f, 0.005f), a.Screen, collider: false);
            Box(t, "Phone", new Vector3(0.5f, 0.79f, -0.1f), new Vector3(0.2f, 0.05f, 0.18f), a.Plastic, collider: false);
        }

        private static void ServerRack(Transform t, GameAssets a, System.Random rng)
        {
            Box(t, "Frame", new Vector3(0f, 1.0f, 0f), new Vector3(0.6f, 2.0f, 1.0f), a.MetalDark);
            Box(t, "Door", new Vector3(0f, 1.0f, 0.502f), new Vector3(0.56f, 1.9f, 0.004f), a.MetalGrey, collider: false);
            for (int u = 0; u < 12; u++)
            {
                float y = 0.15f + u * 0.15f;
                Box(t, $"Unit{u}", new Vector3(0f, y, 0.506f), new Vector3(0.5f, 0.11f, 0.004f), a.MetalDark, collider: false);
                Material led = rng.NextDouble() < 0.85 ? a.LedGreen : a.LedAmber;
                Box(t, $"Led{u}", new Vector3(0.2f, y, 0.51f), new Vector3(0.02f, 0.02f, 0.004f), led, collider: false);
            }
        }

        private static void Shelving(Transform t, GameAssets a, System.Random rng)
        {
            foreach (float x in new[] { -0.88f, 0.88f })
            {
                foreach (float z in new[] { -0.28f, 0.28f })
                {
                    Box(t, "Post", new Vector3(x, 1.0f, z), new Vector3(0.04f, 2.0f, 0.04f), a.MetalGrey);
                }
            }

            for (int s = 0; s < 4; s++)
            {
                float y = 0.1f + s * 0.6f;
                Box(t, $"Shelf{s}", new Vector3(0f, y, 0f), new Vector3(1.8f, 0.03f, 0.6f), a.MetalGrey, collider: s == 0);
                int boxes = 1 + rng.Next(3);
                for (int b = 0; b < boxes; b++)
                {
                    float w = 0.35f + (float)rng.NextDouble() * 0.3f;
                    float h = 0.25f + (float)rng.NextDouble() * 0.25f;
                    float bx = -0.7f + (float)rng.NextDouble() * 1.4f;
                    Box(t, "Carton", new Vector3(bx, y + 0.015f + h / 2f, (float)rng.NextDouble() * 0.1f - 0.05f), new Vector3(w, h, 0.45f), a.Prop, collider: false);
                }
            }
        }

        private static void TrashBin(Transform t, GameAssets a)
        {
            Cyl(t, "Bin", new Vector3(0f, 0.35f, 0f), 0.18f, 0.35f, a.MetalDark);
            Cyl(t, "Rim", new Vector3(0f, 0.71f, 0f), 0.19f, 0.01f, a.MetalGrey, collider: false);
        }

        private static void Whiteboard(Transform t, GameAssets a)
        {
            // Wall-hung: origin at the floor against the wall, board at eye height.
            Box(t, "Board", new Vector3(0f, 1.5f, 0.03f), new Vector3(1.8f, 1.1f, 0.03f), a.Paper);
            Box(t, "Frame", new Vector3(0f, 1.5f, 0.02f), new Vector3(1.86f, 1.16f, 0.02f), a.MetalGrey, collider: false);
            Box(t, "Tray", new Vector3(0f, 0.93f, 0.06f), new Vector3(1.0f, 0.03f, 0.06f), a.MetalGrey, collider: false);
            Box(t, "Marker", new Vector3(-0.2f, 0.955f, 0.06f), new Vector3(0.12f, 0.02f, 0.02f), a.BookA, collider: false);
            Box(t, "Scrawl", new Vector3(-0.3f, 1.65f, 0.047f), new Vector3(0.7f, 0.02f, 0.003f), a.MetalDark, collider: false);
            Box(t, "Scrawl2", new Vector3(0.1f, 1.45f, 0.047f), new Vector3(0.9f, 0.02f, 0.003f), a.BookB, collider: false);
        }

        private static void Copier(Transform t, GameAssets a)
        {
            Box(t, "Body", new Vector3(0f, 0.5f, 0f), new Vector3(1.0f, 1.0f, 0.7f), a.Paper);
            Box(t, "Lid", new Vector3(0f, 1.03f, -0.05f), new Vector3(0.7f, 0.06f, 0.5f), a.Plastic, collider: false);
            Box(t, "Panel", new Vector3(0.3f, 1.02f, 0.2f), new Vector3(0.3f, 0.03f, 0.15f), a.Screen, collider: false);
            Box(t, "TrayL", new Vector3(-0.6f, 0.7f, 0f), new Vector3(0.22f, 0.02f, 0.4f), a.Plastic, collider: false);
            Box(t, "Paper", new Vector3(-0.6f, 0.72f, 0f), new Vector3(0.2f, 0.02f, 0.3f), a.Paper, collider: false);
        }

        private static void CubicleWall(Transform t, GameAssets a)
        {
            Box(t, "Panel", new Vector3(0f, 0.75f, 0f), new Vector3(1.8f, 1.5f, 0.08f), a.FabricLight);
            Box(t, "Cap", new Vector3(0f, 1.51f, 0f), new Vector3(1.82f, 0.03f, 0.1f), a.MetalGrey, collider: false);
            Box(t, "Foot", new Vector3(0f, 0.02f, 0f), new Vector3(1.82f, 0.04f, 0.12f), a.MetalGrey, collider: false);
        }

        // ------------------------------------------------------------------ primitives

        private static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material material, bool collider = true)
            => PrefabFactory.Primitive(PrimitiveType.Cube, name, parent, localPos, size, material, collider);

        private static GameObject Cyl(Transform parent, string name, Vector3 localPos, float radius, float halfHeight, Material material, bool collider = true)
            => PrefabFactory.Primitive(PrimitiveType.Cylinder, name, parent, localPos, new Vector3(radius * 2f, halfHeight, radius * 2f), material, collider);

        private static GameObject Sphere(Transform parent, string name, Vector3 localPos, float radius, Material material, bool collider = true)
            => PrefabFactory.Primitive(PrimitiveType.Sphere, name, parent, localPos, Vector3.one * radius * 2f, material, collider);
    }
}
