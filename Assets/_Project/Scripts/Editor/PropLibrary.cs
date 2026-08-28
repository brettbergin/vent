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
            Poster, PaperScatter,
            /// <summary>A floor-standing fig, palm or monstera in a big pot: the corner piece.</summary>
            PlantLarge,
            /// <summary>A hand-sized fern or snake plant for a desk or a counter.</summary>
            DeskPlant,
        }

        /// <summary>Floor footprint (x = width across the front, y = depth), metres.</summary>
        public static Vector2 Footprint(Kind kind) => kind switch
        {
            Kind.Desk => new Vector2(1.6f, 0.8f),
            Kind.OfficeChair => new Vector2(0.6f, 0.6f),
            Kind.FilingCabinet => new Vector2(0.5f, 0.6f),
            Kind.Bookshelf => new Vector2(1.2f, 0.4f),
            Kind.WaterCooler => new Vector2(0.4f, 0.4f),
            Kind.PottedPlant => new Vector2(0.6f, 0.6f),
            Kind.PlantLarge => new Vector2(1.0f, 1.0f),
            Kind.DeskPlant => new Vector2(0.2f, 0.2f),
            Kind.ConferenceTable => new Vector2(3.2f, 1.2f),
            Kind.VendingMachine => new Vector2(0.9f, 0.8f),
            Kind.Couch => new Vector2(2.0f, 0.9f),
            Kind.ReceptionCounter => new Vector2(2.6f, 0.8f),
            Kind.ServerRack => new Vector2(0.6f, 1.0f),
            Kind.Shelving => new Vector2(1.8f, 0.6f),
            Kind.TrashBin => new Vector2(0.4f, 0.4f),
            Kind.Whiteboard => new Vector2(1.8f, 0.1f),
            Kind.Poster => new Vector2(0.7f, 0.05f),
            Kind.PaperScatter => new Vector2(1.2f, 1.2f),
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
                case Kind.FilingCabinet: FilingCabinet(t, a, rng); break;
                case Kind.Bookshelf: Bookshelf(t, a, rng); break;
                case Kind.WaterCooler: WaterCooler(t, a); break;
                case Kind.PottedPlant: PottedPlant(t, a, rng); break;
                case Kind.PlantLarge: PlantLarge(t, a, rng); break;
                case Kind.DeskPlant: DeskPlant(t, a, rng); break;
                case Kind.ConferenceTable: ConferenceTable(t, a); break;
                case Kind.VendingMachine: VendingMachine(t, a); break;
                case Kind.Couch: Couch(t, a); break;
                case Kind.ReceptionCounter: ReceptionCounter(t, a, rng); break;
                case Kind.ServerRack: ServerRack(t, a, rng); break;
                case Kind.Shelving: Shelving(t, a, rng); break;
                case Kind.TrashBin: TrashBin(t, a); break;
                case Kind.Whiteboard: Whiteboard(t, a); break;
                case Kind.Copier: Copier(t, a); break;
                case Kind.CubicleWall: CubicleWall(t, a); break;
                case Kind.Poster: Poster(t, a, rng); break;
                case Kind.PaperScatter: PaperScatter(t, a, rng); break;
            }

            Layers.SetRecursively(root, Layers.EnvironmentIndex);
            return root;
        }

        // ------------------------------------------------------------------ pieces

        private static void Desk(Transform t, GameAssets a, System.Random rng)
        {
            Box(t, "Top", new Vector3(0f, 0.74f, 0f), new Vector3(1.6f, 0.04f, 0.8f), a.Wood);
            // Modesty board on the visitor side (+Z), which is the whole point of one: it hides the
            // sitter's legs from the room. It used to be at -0.3, i.e. across the sitter's own
            // knees, which left the chair side looking like the back of the desk once the drawers
            // moved there too. The screen, keyboard and chair are all on -Z; this belongs opposite.
            Box(t, "Modesty", new Vector3(0f, 0.45f, 0.3f), new Vector3(1.5f, 0.5f, 0.03f), a.Wood, collider: false);
            Box(t, "Pedestal", new Vector3(0.55f, 0.35f, 0.05f), new Vector3(0.42f, 0.7f, 0.6f), a.MetalGrey);

            // Drawers open toward the chair (-Z), the same side as the keyboard and the screen —
            // where a desk's pedestal drawers actually are. They used to face the back of the desk,
            // which meant spotting the lit monitor from the chair and then walking round to open it.
            // The anchor is turned to face that way, so the leaf and the direction it slides need no
            // special case.
            const float drawerFace = -0.26f, drawerBase = 0.13f;
            Empty(t, "DrawerAnchor", new Vector3(0.55f, drawerBase, drawerFace)).transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            for (int i = 1; i < 3; i++)
            {
                Box(t, $"Drawer{i}", new Vector3(0.55f, drawerBase + i * 0.22f, drawerFace), new Vector3(0.36f, 0.18f, 0.01f), a.Plastic, collider: false);
                Box(t, $"Handle{i}", new Vector3(0.55f, drawerBase + i * 0.22f, drawerFace - 0.01f), new Vector3(0.12f, 0.015f, 0.01f), a.MetalGrey, collider: false);
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
                Box(t, "Paper2", new Vector3(0.28f, 0.79f, -0.02f), new Vector3(0.21f, 0.005f, 0.29f), a.Paper, collider: false).transform.localRotation = Quaternion.Euler(0f, 12f, 0f);
            }

            if (rng.NextDouble() < 0.5)
            {
                // A mug, sometimes with a pen beside it.
                GameObject mug = PrefabFactory.Primitive(PrimitiveType.Cylinder, "Mug", t, new Vector3(0.55f, 0.805f, -0.15f), new Vector3(0.08f, 0.045f, 0.08f), rng.NextDouble() < 0.5 ? a.PosterB : a.Paper, false);
                PrefabFactory.Primitive(PrimitiveType.Cylinder, "MugHandle", mug.transform, new Vector3(0.6f, 0f, 0f), new Vector3(0.35f, 0.6f, 0.15f), mug.GetComponent<Renderer>().sharedMaterial, false);
            }

            if (rng.NextDouble() < 0.6)
            {
                Box(t, "Pen", new Vector3(0.05f, 0.78f, -0.22f), new Vector3(0.14f, 0.008f, 0.008f), a.MetalDark, collider: false).transform.localRotation = Quaternion.Euler(0f, Rand(rng, -40f, 40f), 0f);
            }
            else
            {
                Cyl(t, "Mug", new Vector3(0.2f, 0.81f, -0.15f), 0.04f, 0.05f, a.Paper, collider: false);
            }

            if (rng.NextDouble() < 0.4)
            {
                // Someone's desk plant, on the corner the monitor leaves free.
                DeskPlant(t, a, rng).transform.localPosition = new Vector3(0.62f, 0.76f, 0.26f);
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

        private static void FilingCabinet(Transform t, GameAssets a, System.Random rng)
        {
            Box(t, "Body", new Vector3(0f, 0.66f, 0f), new Vector3(0.5f, 1.32f, 0.6f), a.MetalGrey);
            if (rng.NextDouble() < 0.35)
            {
                // A pothos on top, trailing down the front.
                FoliageLibrary.Pothos(t, a, rng, drop: Rand(rng, 0.45f, 0.8f), clearance: 0.14f, variant: rng.Next(4))
                    .transform.localPosition = new Vector3(Rand(rng, -0.1f, 0.1f), 1.32f, 0.16f);
            }

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
            if (rng.NextDouble() < 0.5)
            {
                FoliageLibrary.Pothos(t, a, rng, drop: Rand(rng, 0.6f, 1.0f), clearance: 0.12f, variant: rng.Next(4))
                    .transform.localPosition = new Vector3(Rand(rng, -0.42f, 0.42f), 1.8f, 0.08f);
            }

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

        /// <summary>A knee-high plant in a 38 cm pot: a monstera, fern, snake plant, small palm or young fig, one of four grown variants each.</summary>
        private static void PottedPlant(Transform t, GameAssets a, System.Random rng)
        {
            (FoliageLibrary.Plant plant, float scale) = rng.Next(5) switch
            {
                0 => (FoliageLibrary.Plant.Monstera, 0.9f),
                1 => (FoliageLibrary.Plant.Fern, 1.0f),
                2 => (FoliageLibrary.Plant.SnakePlant, 1.0f),
                3 => (FoliageLibrary.Plant.Palm, 0.8f),
                _ => (FoliageLibrary.Plant.FiddleFig, 0.7f),
            };
            FoliageLibrary.PottedPlant(t, "Plant", a, rng, plant, scale, potRadius: 0.19f, potHeight: 0.38f, PotMaterial(a, rng), rng.Next(4));
        }

        /// <summary>A head-high floor plant in a 54 cm pot: the thing that stands in the corner of a lobby.</summary>
        private static void PlantLarge(Transform t, GameAssets a, System.Random rng)
        {
            (FoliageLibrary.Plant plant, float scale) = rng.Next(3) switch
            {
                0 => (FoliageLibrary.Plant.FiddleFig, 1.05f),
                1 => (FoliageLibrary.Plant.Palm, 1.25f),
                _ => (FoliageLibrary.Plant.Monstera, 1.35f),
            };
            FoliageLibrary.PottedPlant(t, "Plant", a, rng, plant, scale, potRadius: 0.27f, potHeight: 0.5f, PotMaterial(a, rng), rng.Next(4));
        }

        /// <summary>A hand-sized fern or snake plant in a 12 cm pot; the desk and counter builders place it on their tops.</summary>
        private static GameObject DeskPlant(Transform t, GameAssets a, System.Random rng)
        {
            (FoliageLibrary.Plant plant, float scale) = rng.NextDouble() < 0.5 ? (FoliageLibrary.Plant.Fern, 0.32f) : (FoliageLibrary.Plant.SnakePlant, 0.38f);
            return FoliageLibrary.PottedPlant(t, "DeskPlant", a, rng, plant, scale, potRadius: 0.06f, potHeight: 0.1f, PotMaterial(a, rng), rng.Next(4));
        }

        private static Material PotMaterial(GameAssets a, System.Random rng) => rng.Next(4) switch { 0 => a.Ceramic, 1 => a.CeramicDark, _ => a.Terracotta };

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

        private static void ReceptionCounter(Transform t, GameAssets a, System.Random rng)
        {
            Box(t, "Front", new Vector3(0f, 0.55f, 0.3f), new Vector3(2.6f, 1.1f, 0.06f), a.Wood);
            Box(t, "Ledge", new Vector3(0f, 1.12f, 0.3f), new Vector3(2.7f, 0.04f, 0.3f), a.MetalGrey);
            Box(t, "Worktop", new Vector3(0f, 0.75f, -0.1f), new Vector3(2.5f, 0.04f, 0.7f), a.Wood);
            Box(t, "Base", new Vector3(0f, 0.35f, -0.1f), new Vector3(2.4f, 0.7f, 0.6f), a.MetalGrey);
            Box(t, "Monitor", new Vector3(-0.6f, 1.0f, -0.1f), new Vector3(0.45f, 0.3f, 0.03f), a.Plastic, collider: false);
            Box(t, "Screen", new Vector3(-0.6f, 1.0f, -0.083f), new Vector3(0.4f, 0.25f, 0.005f), a.Screen, collider: false);
            Box(t, "Phone", new Vector3(0.5f, 0.79f, -0.1f), new Vector3(0.2f, 0.05f, 0.18f), a.Plastic, collider: false);
            if (rng.NextDouble() < 0.7)
            {
                DeskPlant(t, a, rng).transform.localPosition = new Vector3(1.0f, 1.14f, 0.3f);
            }
        }

        private static void ServerRack(Transform t, GameAssets a, System.Random rng)
        {
            Box(t, "Frame", new Vector3(0f, 1.0f, 0f), new Vector3(0.6f, 2.0f, 1.0f), a.MetalDark);
            // Chest height on the rack front, proud of the blades: where the patch panel goes.
            Empty(t, "PanelAnchor", new Vector3(0f, 1.35f, 0.515f));
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
            Empty(t, "FaceAnchor", new Vector3(0f, 1.5f, 0.05f));
        }

        private static void Copier(Transform t, GameAssets a)
        {
            Box(t, "Body", new Vector3(0f, 0.5f, 0f), new Vector3(1.0f, 1.0f, 0.7f), a.Paper);
            Box(t, "Lid", new Vector3(0f, 1.03f, -0.05f), new Vector3(0.7f, 0.06f, 0.5f), a.Plastic, collider: false);
            Box(t, "Panel", new Vector3(0.3f, 1.02f, 0.2f), new Vector3(0.3f, 0.03f, 0.15f), a.Screen, collider: false);
            Box(t, "TrayL", new Vector3(-0.6f, 0.7f, 0f), new Vector3(0.22f, 0.02f, 0.4f), a.Plastic, collider: false);
            Box(t, "Paper", new Vector3(-0.6f, 0.72f, 0f), new Vector3(0.2f, 0.02f, 0.3f), a.Paper, collider: false);
        }

        private static void Poster(Transform t, GameAssets a, System.Random rng)
        {
            // Wall-hung sheet at eye height with a coloured block and a title bar: reads as a poster from across the room.
            Material accent = rng.Next(3) switch { 0 => a.PosterA, 1 => a.PosterB, _ => a.PosterC };
            float h = Rand(rng, 1.35f, 1.65f);
            Box(t, "Sheet", new Vector3(0f, h, 0.012f), new Vector3(0.6f, 0.85f, 0.006f), a.Paper, collider: false);
            Box(t, "Art", new Vector3(0f, h + 0.1f, 0.016f), new Vector3(0.5f, 0.5f, 0.004f), accent, collider: false);
            Box(t, "Title", new Vector3(0f, h - 0.28f, 0.016f), new Vector3(0.42f, 0.05f, 0.004f), a.MetalDark, collider: false);
            Box(t, "Line", new Vector3(-0.05f, h - 0.36f, 0.016f), new Vector3(0.3f, 0.02f, 0.004f), a.MetalDark, collider: false);
        }

        private static void PaperScatter(Transform t, GameAssets a, System.Random rng)
        {
            // Sheets that slid off a desk. No colliders: it is litter, not cover.
            int sheets = 3 + rng.Next(4);
            for (int i = 0; i < sheets; i++)
            {
                var pos = new Vector3(Rand(rng, -0.5f, 0.5f), 0.003f + i * 0.002f, Rand(rng, -0.5f, 0.5f));
                Box(t, $"Sheet{i}", pos, new Vector3(0.21f, 0.004f, 0.297f), a.Paper, collider: false).transform.localRotation = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);
            }
        }

        private static float Rand(System.Random rng, float min, float max) => (float)(min + rng.NextDouble() * (max - min));

        private static void CubicleWall(Transform t, GameAssets a)
        {
            Box(t, "Panel", new Vector3(0f, 0.75f, 0f), new Vector3(1.8f, 1.5f, 0.08f), a.FabricLight);
            Box(t, "Cap", new Vector3(0f, 1.51f, 0f), new Vector3(1.82f, 0.03f, 0.1f), a.MetalGrey, collider: false);
            Box(t, "Foot", new Vector3(0f, 0.02f, 0f), new Vector3(1.82f, 0.04f, 0.12f), a.MetalGrey, collider: false);
        }

        // ------------------------------------------------------------------ primitives

        // ------------------------------------------------------------------ key hunt

        /// <summary>
        /// The sliding bottom drawer of a desk. Built as its own root so it can move (the rest of
        /// the desk is batching-static) and so the <c>Front</c> box is the only collider under it:
        /// <c>PlayerInteractor</c> resolves an interactable by walking *up* from whatever it hit,
        /// so a hit on the desktop or a leg finds nothing and only the drawer front prompts.
        /// </summary>
        /// <summary>
        /// The sliding drawer: a front with a handle, and behind it a real box — bottom, two sides
        /// and a back — that lives inside the pedestal and comes out with the front, so an open
        /// drawer is a drawer and not a floating plate. Every drawer holds a pad and a pen (this is
        /// the "stationery" the README promises); the key lies on the bottom, hidden until the hunt
        /// puts it there.
        /// </summary>
        public static GameObject DrawerLeaf(Transform parent, GameAssets a)
        {
            var root = new GameObject("Drawer");
            root.transform.SetParent(parent, false);
            Transform t = root.transform;

            // 3 cm proud of the pedestal's front face, so the interactor's ray reaches the drawer
            // before the pedestal collider stops it. Local +Z is the pull-out direction.
            const float width = 0.36f, height = 0.18f, depth = 0.32f, wall = 0.012f;
            Box(t, "Front", new Vector3(0f, 0f, 0.005f), new Vector3(width, height, 0.03f), a.Plastic);
            Box(t, "Handle", new Vector3(0f, 0f, 0.025f), new Vector3(0.12f, 0.015f, 0.012f), a.MetalGrey, collider: false);

            // The box behind the front, open at the top: bottom at the front's lower edge, low sides, a back.
            float bottomY = -height / 2f + wall / 2f, sideHeight = 0.11f, centreZ = -depth / 2f - 0.01f;
            Box(t, "Bottom", new Vector3(0f, bottomY, centreZ), new Vector3(width - 0.02f, wall, depth), a.MetalGrey, collider: false);
            Box(t, "SideL", new Vector3(-(width - 0.02f) / 2f + wall / 2f, bottomY + sideHeight / 2f, centreZ), new Vector3(wall, sideHeight, depth), a.MetalGrey, collider: false);
            Box(t, "SideR", new Vector3((width - 0.02f) / 2f - wall / 2f, bottomY + sideHeight / 2f, centreZ), new Vector3(wall, sideHeight, depth), a.MetalGrey, collider: false);
            Box(t, "Back", new Vector3(0f, bottomY + sideHeight / 2f, centreZ - depth / 2f + wall / 2f), new Vector3(width - 0.02f, sideHeight, wall), a.MetalGrey, collider: false);

            // Stationery: a pad and a pen, so an early drawer is not empty, just uninteresting.
            float floorY = bottomY + wall / 2f;
            Box(t, "Pad", new Vector3(-0.07f, floorY + 0.006f, centreZ + 0.02f), new Vector3(0.15f, 0.012f, 0.21f), a.Paper, collider: false);
            Box(t, "Pen", new Vector3(0.09f, floorY + 0.004f, centreZ + 0.03f), new Vector3(0.008f, 0.008f, 0.14f), a.MetalDark, collider: false)
                .transform.localRotation = Quaternion.Euler(0f, -8f, 0f);

            // The key, on the bottom near the front where it reads as soon as the drawer is out. Big
            // for a key — a hand's length — because it has to be seen from standing height in a
            // dim room; a light in the drawer only washed it out.
            var key = new GameObject("Key");
            key.transform.SetParent(t, false);
            key.transform.localPosition = new Vector3(0.06f, floorY + 0.006f, -0.2f); // mid-back: what a standing player sees first over the front
            key.transform.localRotation = Quaternion.Euler(0f, 35f, 0f);
            Box(key.transform, "Shank", new Vector3(0f, 0f, 0f), new Vector3(0.016f, 0.01f, 0.13f), a.KeyBrass, collider: false);
            Box(key.transform, "Bit", new Vector3(0.022f, 0f, -0.05f), new Vector3(0.034f, 0.01f, 0.03f), a.KeyBrass, collider: false);
            Box(key.transform, "Bit2", new Vector3(0.018f, 0f, -0.02f), new Vector3(0.026f, 0.01f, 0.014f), a.KeyBrass, collider: false);
            Cyl(key.transform, "Bow", new Vector3(0f, 0f, 0.085f), 0.032f, 0.005f, a.KeyBrass, collider: false)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            key.SetActive(false);

            Layers.SetRecursively(root, Layers.EnvironmentIndex);
            return root;
        }

        /// <summary>
        /// The patch panel on a rack front: a plate of ports whose lamps go from amber to green
        /// when the cables land. Its own root and its own collider, for the same reason the drawer
        /// has one — the rack itself must not become one big interactable.
        /// </summary>
        public static GameObject PatchPanelRig(Transform parent, GameAssets a, out Renderer[] leds, out Light lamp)
        {
            var root = new GameObject("PatchPanel");
            root.transform.SetParent(parent, false);
            Transform t = root.transform;

            Box(t, "Plate", new Vector3(0f, 0f, 0.008f), new Vector3(0.52f, 0.2f, 0.016f), a.MetalDark);
            for (int i = 0; i < 8; i++)
            {
                float x = (i - 3.5f) * 0.056f;
                Box(t, $"Port{i}", new Vector3(x, -0.04f, 0.017f), new Vector3(0.034f, 0.05f, 0.006f), a.Plastic, collider: false);
            }

            // A rack already carries twelve blade LEDs, and at aisle distance one more small lamp
            // among them is invisible — the player ends up reading twenty rack faces one at a time.
            // So the panel wears a full-width status bar rather than pinpricks.
            var lamps = new Renderer[4];
            lamps[0] = Box(t, "StatusBar", new Vector3(0f, 0.06f, 0.018f), new Vector3(0.46f, 0.03f, 0.005f),
                a.LedAmber, collider: false).GetComponent<Renderer>();
            for (int i = 1; i < lamps.Length; i++)
            {
                lamps[i] = Box(t, $"PortLed{i}", new Vector3((i - 2) * 0.14f, -0.008f, 0.018f),
                    new Vector3(0.05f, 0.022f, 0.005f), a.LedAmber, collider: false).GetComponent<Renderer>();
            }

            // ...and its own light, so the one live rack glows down the aisle. Unshadowed, and only
            // ever one of these is switched on at a time.
            var lampGo = new GameObject("PanelGlow");
            lampGo.transform.SetParent(t, false);
            lampGo.transform.localPosition = new Vector3(0f, 0.05f, 0.35f);
            lampGo.layer = Layers.PlayerIndex;
            lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.range = 3.4f;
            lamp.intensity = 1.1f;
            lamp.color = new Color(1f, 0.62f, 0.18f);
            lamp.shadows = LightShadows.None;

            leds = lamps;
            Layers.SetRecursively(root, Layers.EnvironmentIndex);
            return root;
        }

        /// <summary>
        /// A coiled patch cable, left on a shelf or a desktop.
        ///
        /// Deliberately louder than it is realistic. Three of these hide among the clutter of an
        /// eleven-room floor, and a flat 26 cm disc on a desk covered in paper and mugs is invisible
        /// from the doorway — the player ends up sweeping every surface in the building rather than
        /// exploring it. So the coil stands a tagged loop up out of the pile to give it a silhouette
        /// above desk height, and carries its own small unshadowed light: whatever room it is in
        /// reads as "something is in here" from the door.
        /// </summary>
        /// <summary>A printed floor plan on a desk: the map texture on a sheet of paper, a second sheet folded under it, and a collider so the interactor's ray finds it.</summary>
        public static GameObject BuildingMapSheet(Transform parent, GameAssets a)
        {
            var root = new GameObject("BuildingMap");
            root.transform.SetParent(parent, false);
            Transform t = root.transform;
            Box(t, "Under", new Vector3(0.02f, 0.003f, -0.01f), new Vector3(0.34f, 0.004f, 0.26f), a.Paper, collider: false)
                .transform.localRotation = Quaternion.Euler(0f, 7f, 0f);
            GameObject sheet = Box(t, "Sheet", new Vector3(0f, 0.008f, 0f), new Vector3(0.34f, 0.004f, 0.26f), a.BuildingMapPaper, collider: false);
            sheet.transform.localRotation = Quaternion.Euler(0f, -5f, 0f);
            // The map is drawn with north (+Z) up and east (+X) right; a cube's top face maps its texture that way already.
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.02f, 0f);
            collider.size = new Vector3(0.36f, 0.05f, 0.28f);
            return root;
        }

        /// <summary>A vanity mirror on a stand: a chrome disc in a dark rim, tilted back a little, that catches the room light from across it.</summary>
        public static GameObject VanityMirror(Transform parent, GameAssets a)
        {
            var root = new GameObject("Mirror");
            root.transform.SetParent(parent, false);
            Transform t = root.transform;
            Cyl(t, "Base", new Vector3(0f, 0.008f, 0f), 0.06f, 0.008f, a.MetalDark, collider: false);
            Box(t, "Post", new Vector3(0f, 0.09f, -0.02f), new Vector3(0.012f, 0.16f, 0.012f), a.MetalDark, collider: false);
            var head = new GameObject("Head");
            head.transform.SetParent(t, false);
            head.transform.localPosition = new Vector3(0f, 0.2f, -0.01f);
            head.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
            Cyl(head.transform, "Rim", new Vector3(0f, 0f, -0.006f), 0.1f, 0.006f, a.MetalDark, collider: false)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Cyl(head.transform, "Glass", new Vector3(0f, 0f, 0.002f), 0.088f, 0.003f, a.Chrome, collider: false)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.15f, 0f);
            collider.size = new Vector3(0.22f, 0.32f, 0.12f);
            return root;
        }

        public static GameObject CableCoil(Transform parent, GameAssets a, System.Random rng)
        {
            var root = new GameObject("CableCoil");
            root.transform.SetParent(parent, false);
            Transform t = root.transform;

            Cyl(t, "Coil", new Vector3(0f, 0.03f, 0f), 0.15f, 0.03f, a.CableBlue, collider: false);
            Cyl(t, "Hub", new Vector3(0f, 0.032f, 0f), 0.06f, 0.033f, a.MetalDark, collider: false);

            // The silhouette: a loop of cable standing proud of whatever it is lying on, so it
            // breaks the horizontal line of a desktop or a shelf.
            Box(t, "LoopL", new Vector3(-0.06f, 0.19f, 0f), new Vector3(0.025f, 0.32f, 0.025f), a.CableBlue, collider: false)
                .transform.localRotation = Quaternion.Euler(0f, 0f, 9f);
            Box(t, "LoopR", new Vector3(0.06f, 0.19f, 0f), new Vector3(0.025f, 0.32f, 0.025f), a.CableBlue, collider: false)
                .transform.localRotation = Quaternion.Euler(0f, 0f, -9f);
            Box(t, "LoopTop", new Vector3(0f, 0.35f, 0f), new Vector3(0.15f, 0.025f, 0.025f), a.CableBlue, collider: false);
            Box(t, "Tag", new Vector3(0f, 0.26f, 0.012f), new Vector3(0.09f, 0.05f, 0.004f), a.LedAmber, collider: false);
            Box(t, "Plug", new Vector3(0.15f, 0.04f, 0.06f), new Vector3(0.035f, 0.03f, 0.06f), a.MetalGrey, collider: false)
                .transform.localRotation = Quaternion.Euler(0f, Rand(rng, -25f, 25f), 0f);

            // Unshadowed and short-ranged: only three are ever switched on at once, and the building
            // already runs close to its shadow atlas.
            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(t, false);
            // Well clear of the coil's own geometry. Sitting just above the loop, it lit the cable
            // at point-blank range and the whole thing bloomed to a white blob — findable, but it
            // read as a desk lamp rather than a blue coil of cable.
            glowGo.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            glowGo.layer = Layers.PlayerIndex;
            Light glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            // Enough to say "something is in here" from the doorway, not enough to blow the coil
            // out to white: it sits close to its own light, and bloom is already on the profile.
            glow.range = 3.0f;
            glow.intensity = 0.7f;
            glow.color = new Color(0.35f, 0.6f, 1f);
            glow.shadows = LightShadows.None;

            // One box on the root rather than a capsule per cylinder: a scaled capsule collider does
            // not match a flattened cylinder, and the whole thing needs to stay small — Environment
            // is also the mask bullets and zombie line-of-sight use.
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.16f, 0.01f);
            box.size = new Vector3(0.30f, 0.34f, 0.30f);

            Layers.SetRecursively(root, Layers.EnvironmentIndex);
            return root;
        }

        /// <summary>
        /// The printed face laid over the lobby whiteboard: the hint, drawn as real letters by
        /// <see cref="TextureFactory.WhiteboardHint"/>.
        ///
        /// Uses <see cref="MeshLibrary.Card"/> rather than a thin box. Unity's primitive cube does
        /// not give its faces a consistent UV handedness, so a box here renders the hint mirrored or
        /// upside down depending on which face ends up pointing into the room, and correcting it by
        /// rotating the plate is guesswork. The card owns its four UVs and faces +Z, which is the
        /// direction a wall-mounted whiteboard already faces, so it reads the right way round by
        /// construction.
        /// </summary>
        public static GameObject WhiteboardFace(Transform parent, GameAssets a)
        {
            var root = new GameObject("HintFace");
            root.transform.SetParent(parent, false);
            root.AddComponent<MeshFilter>().sharedMesh = MeshLibrary.Card(1.7f, 1.0f);
            root.AddComponent<MeshRenderer>().sharedMaterial = a.WhiteboardHint;
            Layers.SetRecursively(root, Layers.EnvironmentIndex);
            return root;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>A named, empty transform: somewhere for a later pass to hang a rig.</summary>
        private static GameObject Empty(Transform parent, string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go;
        }

        private static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material material, bool collider = true)
            => PrefabFactory.Primitive(PrimitiveType.Cube, name, parent, localPos, size, material, collider);

        private static GameObject Cyl(Transform parent, string name, Vector3 localPos, float radius, float halfHeight, Material material, bool collider = true)
            => PrefabFactory.Primitive(PrimitiveType.Cylinder, name, parent, localPos, new Vector3(radius * 2f, halfHeight, radius * 2f), material, collider);

        private static GameObject Sphere(Transform parent, string name, Vector3 localPos, float radius, Material material, bool collider = true)
            => PrefabFactory.Primitive(PrimitiveType.Sphere, name, parent, localPos, Vector3.one * radius * 2f, material, collider);
    }
}
