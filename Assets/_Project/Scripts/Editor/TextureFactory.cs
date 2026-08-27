using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Vent.Editor
{
    /// <summary>
    /// Synthesises every texture the game uses — albedo and normal maps for drywall, ceiling tiles,
    /// vinyl floor, wood, concrete, asphalt, brushed metal and fabric — from noise and simple
    /// patterns, and writes them as PNGs. Like the sounds and the geometry, the recipes are
    /// deterministic, so a clean checkout regenerates identical files; there is still no
    /// hand-made binary art in the repository.
    ///
    /// Every texture is authored at a known physical size (<see cref="TextureSet.MetersPerTile"/>).
    /// Building blocks carry world-scale UVs (<see cref="MeshLibrary"/>), so the material tiling is
    /// simply 1 / metres-per-tile and a floor tile is 50 cm on every block, whatever the block's size.
    /// </summary>
    public static class TextureFactory
    {
        /// <summary>An albedo/normal pair and the physical size one repeat covers.</summary>
        public sealed class TextureSet
        {
            public Texture2D Albedo;
            public Texture2D Normal;
            public float MetersPerTile;
            public float NormalStrength = 1f;
        }

        private const int Size = 512;

        public static TextureSet Drywall() => Make("Drywall", metersPerTile: 2f, normalStrength: 0.35f, (x, y, n) =>
        {
            // Fine plaster grain plus faint roller streaks.
            float grain = n.Fbm(x, y, 18, 18, 4) * 0.05f;
            float streak = n.Fbm(x, y, 3, 40, 2) * 0.03f;
            float h = 0.5f + grain + streak;
            return (new Color(0.92f + grain, 0.92f + grain, 0.90f + grain), h);
        });

        public static TextureSet CeilingTile() => Make("CeilingTile", metersPerTile: 1.2f, normalStrength: 1.2f, (x, y, n) =>
        {
            // 60 cm acoustic tiles in a T-bar grid, perforated.
            float u = Frac(x * 2f), v = Frac(y * 2f);
            float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
            bool bar = edge < 0.02f;
            float dots = 0f;
            float du = Frac(u * 30f) - 0.5f, dv = Frac(v * 30f) - 0.5f;
            if (du * du + dv * dv < 0.03f) dots = 0.12f;
            float fleck = n.Fbm(x, y, 40, 40, 3) * 0.06f;
            float h = bar ? 0.2f : 0.6f - dots * 2f;
            Color c = bar ? new Color(0.78f, 0.78f, 0.76f) : new Color(0.90f - dots + fleck, 0.90f - dots + fleck, 0.87f - dots + fleck);
            return (c, h);
        });

        public static TextureSet VinylFloor() => Make("VinylFloor", metersPerTile: 1f, normalStrength: 0.8f, (x, y, n) =>
        {
            // 50 cm tiles with thin grout, each tile a slightly different marbled tone.
            float u = Frac(x * 2f), v = Frac(y * 2f);
            int tx = Mathf.FloorToInt(x * 2f), ty = Mathf.FloorToInt(y * 2f);
            float tileTone = n.Hash(tx, ty) * 0.08f - 0.04f;
            float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
            bool grout = edge < 0.012f;
            float marble = n.Fbm(Frac(x + tx * 0.37f), Frac(y + ty * 0.61f), 6, 6, 4) * 0.10f;
            float scuff = n.Fbm(x, y, 25, 25, 3) * 0.04f;
            float g = 0.62f + tileTone + marble - scuff;
            float h = grout ? 0.3f : 0.55f + marble * 0.5f;
            Color c = grout ? new Color(0.40f, 0.40f, 0.41f) : new Color(g, g, g * 1.02f);
            return (c, h);
        });

        public static TextureSet Wood() => Make("Wood", metersPerTile: 1.5f, normalStrength: 0.5f, (x, y, n) =>
        {
            // Grain: stretched noise along x with rings.
            float grain = n.Fbm(x, y, 3, 40, 4);
            float rings = Frac(grain * 4f + x * 1.5f);
            float ring = Mathf.SmoothStep(0.0f, 1f, Mathf.Abs(rings - 0.5f) * 2f) * 0.25f;
            float g = 0.62f + ring * 0.6f - grain * 0.2f;
            float h = 0.5f + ring * 0.4f;
            return (new Color(g, g * 0.78f, g * 0.58f), h);
        });

        public static TextureSet Concrete() => Make("Concrete", metersPerTile: 2f, normalStrength: 0.7f, (x, y, n) =>
        {
            float big = n.Fbm(x, y, 4, 4, 3) * 0.12f;
            float pits = n.Fbm(x, y, 60, 60, 2);
            float pit = pits > 0.62f ? (pits - 0.62f) * 0.8f : 0f;
            float g = 0.66f + big - pit;
            return (new Color(g, g, g * 0.98f), 0.5f + big - pit * 2f);
        });

        public static TextureSet Asphalt() => Make("Asphalt", metersPerTile: 3f, normalStrength: 0.8f, (x, y, n) =>
        {
            float g = 0.55f + n.Fbm(x, y, 50, 50, 3) * 0.3f + n.Fbm(x, y, 5, 5, 2) * 0.1f;
            return (new Color(g, g, g * 1.03f), g);
        });

        public static TextureSet BrushedMetal() => Make("BrushedMetal", metersPerTile: 1f, normalStrength: 0.3f, (x, y, n) =>
        {
            float streak = n.Fbm(x, y, 2, 120, 3) * 0.12f;
            float g = 0.82f + streak;
            return (new Color(g, g, g), 0.5f + streak);
        });

        public static TextureSet Fabric() => Make("Fabric", metersPerTile: 0.5f, normalStrength: 0.6f, (x, y, n) =>
        {
            float weave = (Mathf.Sin(x * 180f * Mathf.PI) * Mathf.Sin(y * 180f * Mathf.PI)) * 0.08f;
            float fuzz = n.Fbm(x, y, 30, 30, 3) * 0.06f;
            float g = 0.80f + weave + fuzz;
            return (new Color(g, g, g), 0.5f + weave * 2f);
        });

        // ------------------------------------------------------------------ the district

        public static TextureSet Brick() => Make("Brick", metersPerTile: 1f, normalStrength: 1.0f, (x, y, n) =>
        {
            // Running bond: 4 bricks × 12 courses per metre, every other course offset half a brick.
            int course = Mathf.FloorToInt(y * 12f);
            float u = Frac(x * 4f + (course % 2 == 0 ? 0f : 0.5f)), v = Frac(y * 12f);
            bool mortar = u < 0.06f || v < 0.09f;
            int bx = Mathf.FloorToInt(x * 4f + (course % 2 == 0 ? 0f : 0.5f));
            float tone = n.Hash(bx, course) * 0.16f - 0.08f;
            float grit = n.Fbm(x, y, 40, 40, 2) * 0.06f;
            float h = mortar ? 0.25f : 0.62f + grit;
            Color c = mortar ? new Color(0.78f, 0.76f, 0.72f) : new Color(0.82f + tone, 0.72f + tone * 0.8f, 0.66f + tone * 0.6f);
            return (c, h);
        });

        public static TextureSet Stucco() => Make("Stucco", metersPerTile: 2f, normalStrength: 0.6f, (x, y, n) =>
        {
            float bumps = n.Fbm(x, y, 30, 30, 3) * 0.14f;
            float stain = n.Fbm(x, y, 3, 6, 2) * 0.06f;
            float g = 0.86f + bumps - stain;
            return (new Color(g, g * 0.99f, g * 0.96f), 0.45f + bumps);
        });

        public static TextureSet MetalPanel() => Make("MetalPanel", metersPerTile: 1f, normalStrength: 1.1f, (x, y, n) =>
        {
            // Vertical corrugation every 10 cm, faint rust streaks running down.
            float rib = Mathf.Abs(Frac(x * 10f) - 0.5f) * 2f;
            float streak = n.Fbm(x, y, 12, 2, 2) * 0.08f;
            float g = 0.80f - rib * 0.08f - streak;
            return (new Color(g, g, g * 1.02f), 0.35f + rib * 0.4f);
        });

        public static TextureSet Pavers() => Make("Pavers", metersPerTile: 1.5f, normalStrength: 0.9f, (x, y, n) =>
        {
            // 50 cm concrete pavers with scored joints; each a little different.
            float u = Frac(x * 3f), v = Frac(y * 3f);
            int px = Mathf.FloorToInt(x * 3f), py = Mathf.FloorToInt(y * 3f);
            float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
            bool joint = edge < 0.03f;
            float tone = n.Hash(px, py) * 0.12f - 0.06f;
            float grit = n.Fbm(x, y, 45, 45, 2) * 0.06f;
            float g = 0.72f + tone + grit;
            return (joint ? new Color(0.42f, 0.42f, 0.43f) : new Color(g, g, g * 0.98f), joint ? 0.3f : 0.55f + grit);
        });

        public static TextureSet Grass() => Make("Grass", metersPerTile: 2f, normalStrength: 0.7f, (x, y, n) =>
        {
            float blades = n.Fbm(x, y, 60, 60, 3);
            float patches = n.Fbm(x, y, 4, 4, 2);
            float g = 0.55f + blades * 0.35f;
            var c = new Color(g * (0.55f + patches * 0.25f), g, g * 0.45f);
            return (c, 0.4f + blades * 0.4f);
        });

        public static TextureSet Dirt() => Make("Dirt", metersPerTile: 2f, normalStrength: 0.8f, (x, y, n) =>
        {
            float clods = n.Fbm(x, y, 20, 20, 3);
            float ruts = Mathf.Abs(Mathf.Sin(y * 6f * Mathf.PI)) * n.Fbm(x, y, 2, 2, 1) * 0.12f;
            float g = 0.62f + clods * 0.25f - ruts;
            return (new Color(g, g * 0.86f, g * 0.7f), 0.4f + clods * 0.4f - ruts);
        });

        // ------------------------------------------------------------------ nature

        public static TextureSet Bark() => Make("Bark", metersPerTile: 1f, normalStrength: 1.2f, (x, y, n) =>
        {
            // Vertical ridges and deep fissures, the plates between them a little lighter and lichen-flecked.
            float ridges = n.Fbm(x, y, 16, 3, 3);
            float fissure = n.Fbm(x + 0.37f, y, 9, 2, 2);
            float f = Mathf.SmoothStep(0.56f, 0.74f, fissure);
            float lichen = n.Fbm(x, y, 7, 7, 2) > 0.66f ? 0.08f : 0f;
            float g = 0.62f + ridges * 0.25f - f * 0.35f;
            var c = new Color(g + lichen * 0.3f, g * 0.86f + lichen, g * 0.7f + lichen * 0.6f);
            return (c, 0.5f + ridges * 0.35f - f * 0.45f);
        });

        public static TextureSet Birch() => Make("Birch", metersPerTile: 1f, normalStrength: 0.9f, (x, y, n) =>
        {
            // Chalk-white bark with black horizontal lenticels and the odd dark scar.
            float grain = n.Fbm(x, y, 3, 30, 3) * 0.08f;
            float lent = n.Fbm(x, y * 3f, 40, 4, 2);
            bool lenticel = lent > 0.68f && Frac(y * 9f) < 0.35f;
            float scar = n.Fbm(x, y, 4, 4, 2) > 0.78f ? 0.5f : 0f;
            float g = 0.86f + grain - scar;
            if (lenticel) g -= 0.5f;
            return (new Color(g, g * 0.98f, g * 0.94f), lenticel ? 0.35f : 0.55f + grain - scar * 0.3f);
        });

        public static TextureSet Mulch() => Make("Mulch", metersPerTile: 0.6f, normalStrength: 1.1f, (x, y, n) =>
        {
            // Bark chips: small dark flakes over damp soil.
            float chips = n.Fbm(x, y, 22, 22, 3);
            float chip = Mathf.SmoothStep(0.5f, 0.62f, chips);
            float damp = n.Fbm(x, y, 5, 5, 2) * 0.12f;
            float g = 0.30f + chip * 0.28f - damp;
            return (new Color(g * 1.05f, g * 0.78f, g * 0.55f), 0.35f + chip * 0.45f);
        });

        // ------------------------------------------------------------------ sprites (RGBA, clamped)

        /// <summary>Muzzle flash: a white-hot core, an orange corona and eight ragged spikes.</summary>
        /// <summary>
        /// The lobby whiteboard's face: the hint the whole key hunt hangs off, drawn with
        /// <see cref="GlyphFont"/> so the player reads it off the wall rather than through a menu.
        /// Non-square (1024x640) because the board is 1.8 x 1.1 m and Unity's cube gives each face
        /// 0..1 UVs — a square texture here would squash every letter by about a third.
        /// </summary>
        public static Texture2D WhiteboardHint()
        {
            const int W = 1024, H = 640;
            var noise = new Noise("WhiteboardHint".GetHashCode());
            var px = new Color[W * H];

            // The board itself: cool off-white, with the vertical smear a dry cloth leaves behind
            // and a ghost of whatever was written here last.
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)W, v = y / (float)H;
                    float wipe = noise.Value(u, v, 24, 3) * 0.06f;
                    float ghost = Mathf.Max(0f, noise.Value(u, v, 9, 7) - 0.72f) * 0.5f;
                    float shade = 0.93f - wipe - ghost;
                    px[y * W + x] = new Color(shade * 0.99f, shade, shade * 1.01f);
                }
            }

            var ink = new Color(0.09f, 0.11f, 0.16f);
            var accent = new Color(0.65f, 0.12f, 0.14f);
            var blue = new Color(0.13f, 0.26f, 0.55f);

            const int Scale = 5;
            const int Left = 120;
            int line = 130;
            int Step(int rows) => GlyphFont.GlyphHeight * Scale + rows;

            GlyphFont.Draw(px, W, H, "SERVERS ARE DOWN", Left, line, Scale, ink);
            Rule(px, W, H, Left, line + GlyphFont.GlyphHeight * Scale + 8, GlyphFont.Width("SERVERS ARE DOWN") * Scale, 4, ink);
            line += Step(46);

            GlyphFont.Draw(px, W, H, "FIND 3 PATCH CABLES", Left, line, Scale, blue);
            line += Step(16);
            GlyphFont.Draw(px, W, H, "PLUG THEM IN AT THE RACK", Left, line, Scale, blue);
            line += Step(16);
            GlyphFont.Draw(px, W, H, "IN THE SERVER ROOM", Left, line, Scale, blue);
            line += Step(46);

            GlyphFont.Draw(px, W, H, "> ONE MONITOR COMES BACK", Left, line, Scale, ink);
            line += Step(16);
            GlyphFont.Draw(px, W, H, "> THE KEY IS IN ITS DRAWER", Left, line, Scale, accent);
            Rule(px, W, H, Left, line + GlyphFont.GlyphHeight * Scale + 8, GlyphFont.Width("> THE KEY IS IN ITS DRAWER") * Scale, 3, accent);

            return WriteImage("T_WhiteboardHint", px, W, isNormal: false, hasAlpha: false, clamp: true, height: H);
        }

        /// <summary>A marker rule under a line of text.</summary>
        private static void Rule(Color[] px, int width, int height, int left, int top, int length, int thickness, Color ink)
        {
            for (int dy = 0; dy < thickness; dy++)
            {
                int y = top + dy;
                if (y < 0 || y >= height)
                {
                    continue;
                }

                int rowStart = (height - 1 - y) * width;
                for (int dx = 0; dx < length; dx++)
                {
                    int x = left + dx;
                    if (x >= 0 && x < width)
                    {
                        px[rowStart + x] = ink;
                    }
                }
            }
        }

        public static Texture2D MuzzleFlashSprite() => Sprite("MuzzleFlash", 256, (x, y, n) =>
        {
            float dx = x - 0.5f, dy = y - 0.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f; // 0 centre, 1 edge
            float angle = Mathf.Atan2(dy, dx);
            float ragged = n.Fbm(Frac(angle / (2f * Mathf.PI) + 0.5f), r * 0.5f, 8, 4, 3);
            float spikes = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(angle * 4f + ragged * 1.5f)), 6f) * Mathf.Exp(-r * 2.2f);
            float corona = Mathf.Exp(-r * r * 6f) * (0.7f + 0.3f * ragged);
            float core = Mathf.Exp(-r * r * 40f);
            float i = Mathf.Clamp01(core * 1.2f + corona * 0.9f + spikes * 0.8f);
            // White core, orange mid, red rim.
            Color c = Color.Lerp(new Color(1f, 0.35f, 0.1f), new Color(1f, 0.85f, 0.5f), Mathf.Clamp01(i * 1.4f - 0.2f));
            c = Color.Lerp(c, Color.white, core);
            return (c, i * Mathf.Clamp01(1.4f - r));
        });

        /// <summary>A soft grey puff with a little structure, for gun smoke and dust.</summary>
        public static Texture2D SmokeSprite() => Sprite("Smoke", 128, (x, y, n) =>
        {
            float dx = x - 0.5f, dy = y - 0.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
            float body = n.Fbm(x, y, 4, 4, 3);
            float a = Mathf.Clamp01((1f - r) * 1.3f) * Mathf.Clamp01(0.35f + body * 0.9f);
            a = a * a;
            float g = 0.55f + body * 0.25f;
            return (new Color(g, g, g), a);
        });

        /// <summary>A hot dot with a soft halo, stretched by the particle renderer into a streak.</summary>
        public static Texture2D SparkSprite() => Sprite("Spark", 64, (x, y, n) =>
        {
            float dx = x - 0.5f, dy = y - 0.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
            float a = Mathf.Exp(-r * r * 12f) + Mathf.Exp(-r * r * 60f);
            return (Color.Lerp(new Color(1f, 0.6f, 0.2f), Color.white, Mathf.Exp(-r * r * 60f)), Mathf.Clamp01(a));
        });

        private static Texture2D Sprite(string name, int size, Func<float, float, Noise, (Color color, float alpha)> recipe)
        {
            var noise = new Noise(name.GetHashCode());
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    (Color c, float a) = recipe((x + 0.5f) / size, (y + 0.5f) / size, noise);
                    Color s = Saturate(c);
                    s.a = Mathf.Clamp01(a);
                    pixels[y * size + x] = s;
                }
            }

            string path = $"{Paths.Textures}/T_{name}.png";
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            string full = Path.Combine(Directory.GetCurrentDirectory(), path);
            if (!File.Exists(full) || !BytesEqual(File.ReadAllBytes(full), png))
            {
                File.WriteAllBytes(full, png);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool dirty = false;
            if (importer.wrapMode != TextureWrapMode.Clamp) { importer.wrapMode = TextureWrapMode.Clamp; dirty = true; }
            if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; dirty = true; }
            if (!importer.sRGBTexture) { importer.sRGBTexture = true; dirty = true; }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
            if (dirty) importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ machinery

        private static TextureSet Make(string name, float metersPerTile, float normalStrength, Func<float, float, Noise, (Color color, float height)> recipe)
        {
            var noise = new Noise(name.GetHashCode());
            var albedo = new Color[Size * Size];
            var height = new float[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    (Color c, float h) = recipe((float)x / Size, (float)y / Size, noise);
                    albedo[y * Size + x] = Saturate(c);
                    height[y * Size + x] = h;
                }
            }

            var normal = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    // Tileable central differences; strength scales the slope.
                    float dx = (height[y * Size + (x + 1) % Size] - height[y * Size + (x + Size - 1) % Size]) * normalStrength * 4f;
                    float dy = (height[((y + 1) % Size) * Size + x] - height[((y + Size - 1) % Size) * Size + x]) * normalStrength * 4f;
                    Vector3 nrm = new Vector3(-dx, -dy, 1f).normalized;
                    normal[y * Size + x] = new Color(nrm.x * 0.5f + 0.5f, nrm.y * 0.5f + 0.5f, nrm.z * 0.5f + 0.5f, 1f);
                }
            }

            return new TextureSet
            {
                Albedo = Write($"T_{name}", albedo, isNormal: false),
                Normal = Write($"T_{name}_N", normal, isNormal: true),
                MetersPerTile = metersPerTile,
                NormalStrength = 1f,
            };
        }

        private static Texture2D Write(string name, Color[] pixels, bool isNormal)
            => WriteImage(name, pixels, Size, isNormal, hasAlpha: false, clamp: false);

        /// <summary>
        /// Encode <paramref name="pixels"/> as a PNG at <c>Textures/name.png</c> (only rewriting it when the bytes
        /// change, so a regeneration that produces the same art does not churn the repository) and set its import
        /// settings. Cutout art passes a <paramref name="coverageCutoff"/>: the importer then rescales every
        /// mip's alpha so the same fraction of texels survives the alpha test at any distance, which is what
        /// keeps a tree's canopy from thinning to twigs a hundred metres away.
        /// </summary>
        /// <param name="height">Rows, when the image is not square (the whiteboard's face is 1.8 x 1.1 m); -1 means square.</param>
        public static Texture2D WriteImage(string name, Color[] pixels, int size, bool isNormal, bool hasAlpha, bool clamp, float coverageCutoff = -1f, int height = -1)
        {
            int rows = height > 0 ? height : size;
            string path = $"{Paths.Textures}/{name}.png";
            var tex = new Texture2D(size, rows, hasAlpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false, linear: isNormal);
            tex.SetPixels(pixels);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            string full = Path.Combine(Directory.GetCurrentDirectory(), path);
            bool changed = !File.Exists(full) || !BytesEqual(File.ReadAllBytes(full), png);
            if (changed)
            {
                File.WriteAllBytes(full, png);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool dirty = false;
            TextureImporterType type = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            TextureWrapMode wrap = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            bool coverage = coverageCutoff >= 0f;
            if (importer.textureType != type) { importer.textureType = type; dirty = true; }
            if (importer.wrapMode != wrap) { importer.wrapMode = wrap; dirty = true; }
            if (importer.anisoLevel != 8) { importer.anisoLevel = 8; dirty = true; }
            if (importer.sRGBTexture != !isNormal) { importer.sRGBTexture = !isNormal; dirty = true; }
            if (!importer.mipmapEnabled) { importer.mipmapEnabled = true; dirty = true; }
            if (importer.alphaIsTransparency != hasAlpha) { importer.alphaIsTransparency = hasAlpha; dirty = true; }
            if (importer.mipMapsPreserveCoverage != coverage) { importer.mipMapsPreserveCoverage = coverage; dirty = true; }
            if (coverage && Mathf.Abs(importer.alphaTestReferenceValue - coverageCutoff) > 0.001f) { importer.alphaTestReferenceValue = coverageCutoff; dirty = true; }
            if (importer.maxTextureSize < Mathf.Max(size, rows)) { importer.maxTextureSize = Mathf.Max(size, rows); dirty = true; }
            if (dirty)
            {
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static float Frac(float v) => v - Mathf.Floor(v);

        private static Color Saturate(Color c) => new(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);

        /// <summary>Tileable value noise on the unit square: integer frequencies wrap, so every texture repeats seamlessly.</summary>
        public sealed class Noise
        {
            private readonly int seed;

            public Noise(int seed) => this.seed = seed;

            public float Hash(int x, int y)
            {
                unchecked
                {
                    uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
                    h = (h ^ (h >> 13)) * 1274126177u;
                    h ^= h >> 16;
                    return (h & 0xFFFFFF) / (float)0x1000000;
                }
            }

            /// <summary>Smoothed value noise at integer frequencies (tiles on [0,1)).</summary>
            public float Value(float u, float v, int freqU, int freqV)
            {
                float fx = u * freqU, fy = v * freqV;
                int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
                float tx = fx - x0, ty = fy - y0;
                tx = tx * tx * (3f - 2f * tx);
                ty = ty * ty * (3f - 2f * ty);
                int x1 = (x0 + 1) % freqU, y1 = (y0 + 1) % freqV;
                x0 %= freqU; y0 %= freqV;
                float a = Hash(x0, y0), b = Hash(x1, y0), c = Hash(x0, y1), d = Hash(x1, y1);
                return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
            }

            /// <summary>Fractal noise in [0,1], <paramref name="octaves"/> doublings from the base frequencies.</summary>
            public float Fbm(float u, float v, int freqU, int freqV, int octaves)
            {
                float sum = 0f, amp = 0.5f, total = 0f;
                for (int o = 0; o < octaves; o++)
                {
                    sum += Value(u, v, freqU, freqV) * amp;
                    total += amp;
                    amp *= 0.5f;
                    freqU *= 2;
                    freqV *= 2;
                }

                return sum / total;
            }
        }
    }
}
