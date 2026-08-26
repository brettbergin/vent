using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vent.Editor
{
    /// <summary>
    /// Draws the leaf atlas every plant in the game is cut from: a 4×4 grid of 512 px cells, each a
    /// leaf, a frond, a cluster of leaves, a grass tuft or a bark patch, painted from analytic leaf
    /// profiles (midrib, veins, lobes, serration, holes) rather than photographs, so the repository
    /// still holds no hand-made art. Alongside the RGBA albedo it writes a normal map derived from
    /// each leaf's domed cross-section and vein ridges. Colours are dilated into the transparent
    /// pixels so mipmaps never bleed black into the cutout edge.
    /// </summary>
    public static class FoliageTextureFactory
    {
        public const int CellPixels = 512;
        public const int CellsPerSide = 4;
        public const int AtlasSize = CellPixels * CellsPerSide;

        /// <summary>One cell of the atlas. The order is the layout: cell 0 is bottom-left, cell 4 starts the second row.</summary>
        public enum Cell
        {
            Broad, Fig, Frond, Sword,
            Heart, Cluster, Bloom, Needles,
            Grass, Weeds, Litter, Ivy,
            Boxwood, Palm, Bark, Stem,
        }

        /// <summary>UV rectangle of a cell, inset so bilinear filtering never reads the neighbour.</summary>
        public static Rect CellUv(Cell cell, float padding = 0.004f)
        {
            int i = (int)cell;
            float s = 1f / CellsPerSide;
            return new Rect(i % CellsPerSide * s + padding, i / CellsPerSide * s + padding, s - 2f * padding, s - 2f * padding);
        }

        public sealed class Result
        {
            public Texture2D Albedo;
            public Texture2D Normal;
        }

        public static Result Atlas()
        {
            var canvas = new Canvas(AtlasSize);
            foreach (Cell cell in Enum.GetValues(typeof(Cell)))
            {
                int cx = (int)cell % CellsPerSide, cy = (int)cell / CellsPerSide;
                var painter = new Painter(canvas, cx * CellPixels, cy * CellPixels, CellPixels, new System.Random(1000 + (int)cell));
                Draw(cell, painter);
            }

            canvas.Dilate();
            Color[] normal = canvas.NormalMap(strength: 2.2f);
            return new Result
            {
                Albedo = TextureFactory.WriteImage("T_Foliage", canvas.Rgba(), AtlasSize, isNormal: false, hasAlpha: true, clamp: true, coverageCutoff: 0.45f),
                Normal = TextureFactory.WriteImage("T_Foliage_N", normal, AtlasSize, isNormal: true, hasAlpha: false, clamp: true),
            };
        }

        // ------------------------------------------------------------------ the cells

        private static void Draw(Cell cell, Painter p)
        {
            switch (cell)
            {
                case Cell.Broad:
                    // Monstera: one huge lobed leaf with the holes, glossy, base at the bottom centre.
                    p.Leaf(new Vector2(0.5f, 0.03f), 0.94f, 0.86f, 0f, new LeafSpec
                    {
                        Style = LeafStyle.Lobed, Color = new Color(0.12f, 0.34f, 0.12f), Vein = new Color(0.42f, 0.60f, 0.30f),
                        VeinCount = 7, VeinSlope = 0.45f, Rib = 0.025f, Holes = true, Mottle = 0.12f,
                    });
                    break;

                case Cell.Fig:
                    // Fiddle-leaf fig: a broad, deeply veined oval with a wavy edge.
                    p.Leaf(new Vector2(0.5f, 0.04f), 0.92f, 0.74f, 0f, new LeafSpec
                    {
                        Style = LeafStyle.Pointed, Color = new Color(0.13f, 0.35f, 0.13f), Vein = new Color(0.52f, 0.62f, 0.34f),
                        VeinCount = 6, VeinSlope = 0.55f, Rib = 0.03f, Wave = 0.035f, VeinStrength = 0.75f, Mottle = 0.08f,
                    });
                    break;

                case Cell.Frond:
                    // Fern frond: a rachis with serrated pinnae either side, shorter toward the tip.
                    p.Pinnate(new Vector2(0.5f, 0.02f), 0.95f, pinnaePerSide: 15, pinnaLength: 0.24f, pinnaWidth: 0.075f, angle: 62f,
                        new LeafSpec { Style = LeafStyle.Serrated, Color = new Color(0.26f, 0.48f, 0.15f), Vein = new Color(0.50f, 0.66f, 0.30f), VeinCount = 3, Rib = 0.02f, Mottle = 0.10f },
                        rachisColor: new Color(0.32f, 0.36f, 0.16f), rachisWidth: 0.014f, taper: 0.35f);
                    break;

                case Cell.Sword:
                    // Snake plant blade: banded green with a yellow margin.
                    p.Leaf(new Vector2(0.5f, 0.02f), 0.96f, 0.30f, 0f, new LeafSpec
                    {
                        Style = LeafStyle.Sword, Color = new Color(0.17f, 0.40f, 0.17f), Vein = new Color(0.17f, 0.40f, 0.17f),
                        VeinCount = 0, Rib = 0.0f, Bands = true, BandColor = new Color(0.09f, 0.25f, 0.10f), Margin = new Color(0.78f, 0.74f, 0.30f), MarginWidth = 0.16f, Mottle = 0.05f,
                    });
                    break;

                case Cell.Heart:
                    // Pothos: a heart-shaped leaf splashed with yellow variegation.
                    p.Leaf(new Vector2(0.5f, 0.05f), 0.90f, 0.82f, 0f, new LeafSpec
                    {
                        Style = LeafStyle.Heart, Color = new Color(0.15f, 0.40f, 0.14f), Vein = new Color(0.50f, 0.64f, 0.34f),
                        VeinCount = 5, VeinSlope = 0.5f, Rib = 0.025f, Variegation = new Color(0.74f, 0.76f, 0.32f), VariegationAmount = 0.22f, Mottle = 0.08f,
                    });
                    break;

                case Cell.Cluster:
                    // A tree's canopy card: twigs, then a hundred and fifty small leaves, the last ones lightest.
                    p.Twigs(new Vector2(0.5f, 0.5f), 5, 0.42f, new Color(0.30f, 0.22f, 0.13f));
                    p.Scatter(150, 0.46f, 0.11f, 0.18f, 0.5f, new Color(0.17f, 0.40f, 0.12f), new Color(0.34f, 0.56f, 0.20f), LeafStyle.Pointed, falloff: 1.6f);
                    break;

                case Cell.Bloom:
                    // A flowering shrub card: small leaves and pink and white blossoms on top.
                    p.Twigs(new Vector2(0.5f, 0.5f), 4, 0.4f, new Color(0.28f, 0.22f, 0.14f));
                    p.Scatter(170, 0.45f, 0.07f, 0.12f, 0.55f, new Color(0.20f, 0.44f, 0.16f), new Color(0.32f, 0.56f, 0.22f), LeafStyle.Oval, falloff: 1.6f);
                    p.Flowers(26, 0.40f, 0.028f, 0.045f, new Color(0.96f, 0.55f, 0.72f), new Color(0.97f, 0.94f, 0.88f), new Color(0.95f, 0.80f, 0.25f));
                    break;

                case Cell.Needles:
                    // A conifer spray: a twig with a side twig each way, needles along all three.
                    p.Spray(new Vector2(0.5f, 0.03f), 0.94f, new Color(0.09f, 0.27f, 0.19f), new Color(0.16f, 0.34f, 0.24f), new Color(0.30f, 0.24f, 0.16f));
                    break;

                case Cell.Grass:
                    // A tuft: a dozen blades fanning from the base, darker where they meet.
                    p.Grass(new Vector2(0.5f, 0.02f), 15, 0.55f, 0.96f, new Color(0.20f, 0.36f, 0.10f), new Color(0.36f, 0.58f, 0.18f), new Color(0.50f, 0.64f, 0.24f));
                    break;

                case Cell.Weeds:
                    // Dandelion-ish: a rosette of jagged leaves and two stalks with yellow heads.
                    for (int i = 0; i < 9; i++)
                    {
                        float ang = -70f + i * 140f / 8f + p.Rand(-6f, 6f);
                        p.Leaf(new Vector2(0.5f, 0.03f), p.Rand(0.45f, 0.70f), p.Rand(0.16f, 0.22f), ang, new LeafSpec
                        {
                            Style = LeafStyle.Jagged, Color = new Color(0.28f, 0.44f, 0.17f), Vein = new Color(0.48f, 0.60f, 0.30f), VeinCount = 4, Rib = 0.02f, Mottle = 0.1f,
                        });
                    }

                    foreach (float ang in new[] { -18f, 22f })
                    {
                        Vector2 top = new Vector2(0.5f, 0.03f) + Painter.Dir(ang) * 0.8f;
                        p.Stroke(new Vector2(0.5f, 0.03f), top, 0.014f, 0.008f, new Color(0.36f, 0.44f, 0.20f), 0.6f);
                        p.Flower(top, 0.055f, new Color(0.95f, 0.78f, 0.15f), new Color(0.85f, 0.60f, 0.10f), petals: 12);
                    }

                    break;

                case Cell.Litter:
                    // Fallen leaves lying flat: every one wholly inside the card, browns and ochres.
                    {
                        Color[] autumn =
                        {
                            new(0.48f, 0.28f, 0.11f), new(0.64f, 0.38f, 0.14f), new(0.58f, 0.46f, 0.18f), new(0.36f, 0.22f, 0.10f), new(0.70f, 0.50f, 0.22f),
                        };
                        for (int i = 0; i < 16; i++)
                        {
                            Vector2 c = new Vector2(0.5f, 0.5f) + Painter.Dir(p.Rand(0f, 360f)) * p.Rand(0f, 0.30f);
                            float len = p.Rand(0.16f, 0.24f);
                            float ang = p.Rand(0f, 360f);
                            Vector2 basePos = c - Painter.Dir(ang) * (len / 2f);
                            Color col = autumn[p.Rng.Next(autumn.Length)];
                            p.Leaf(basePos, len, len * p.Rand(0.55f, 0.8f), ang, new LeafSpec
                            {
                                Style = i % 3 == 0 ? LeafStyle.Lobed : LeafStyle.Pointed, Color = col, Vein = col * 0.7f, VeinCount = 4, Rib = 0.02f, Mottle = 0.18f, Flat = true,
                            });
                        }

                        break;
                    }

                case Cell.Ivy:
                    // An ivy patch: lobed dark leaves hanging every way, denser in the middle.
                    p.Twigs(new Vector2(0.5f, 0.5f), 3, 0.4f, new Color(0.25f, 0.22f, 0.14f));
                    p.Scatter(48, 0.44f, 0.14f, 0.22f, 0.75f, new Color(0.09f, 0.27f, 0.10f), new Color(0.18f, 0.38f, 0.14f), LeafStyle.IvyLobed, falloff: 1.3f, veined: true, downward: true);
                    break;

                case Cell.Boxwood:
                    // Clipped hedge: a wall of tiny oval leaves with almost no gaps.
                    p.Twigs(new Vector2(0.5f, 0.5f), 6, 0.45f, new Color(0.26f, 0.20f, 0.12f));
                    p.Scatter(260, 0.47f, 0.045f, 0.075f, 0.6f, new Color(0.20f, 0.42f, 0.14f), new Color(0.36f, 0.58f, 0.22f), LeafStyle.Oval, falloff: 2.2f);
                    break;

                case Cell.Palm:
                    // Parlour palm frond: a long rachis with narrow leaflets.
                    p.Pinnate(new Vector2(0.5f, 0.02f), 0.96f, pinnaePerSide: 24, pinnaLength: 0.26f, pinnaWidth: 0.032f, angle: 42f,
                        new LeafSpec { Style = LeafStyle.Needle, Color = new Color(0.20f, 0.44f, 0.15f), Vein = new Color(0.36f, 0.56f, 0.24f), VeinCount = 0, Rib = 0.012f, Mottle = 0.06f },
                        rachisColor: new Color(0.36f, 0.38f, 0.18f), rachisWidth: 0.012f, taper: 0.55f);
                    break;

                case Cell.Bark:
                    p.BarkPatch(new Color(0.34f, 0.28f, 0.22f), new Color(0.16f, 0.12f, 0.09f));
                    break;

                case Cell.Stem:
                    p.StemPatch(new Color(0.30f, 0.40f, 0.19f), new Color(0.20f, 0.28f, 0.12f));
                    break;
            }
        }

        // ------------------------------------------------------------------ painting

        public enum LeafStyle { Oval, Pointed, Heart, Sword, Lobed, Blade, Needle, Serrated, Jagged, IvyLobed }

        public struct LeafSpec
        {
            public LeafStyle Style;
            public Color Color, Vein;
            public int VeinCount;
            public float VeinSlope, VeinStrength, Rib, Wave, Mottle;
            public bool Holes, Bands, Flat;
            public Color BandColor, Margin, Variegation;
            public float MarginWidth, VariegationAmount;
        }

        /// <summary>RGBA + height over the whole atlas; cells paint into windows of it.</summary>
        private sealed class Canvas
        {
            public readonly int Size;
            public readonly Color[] Rgb;
            public readonly float[] Alpha;
            public readonly float[] Height;

            public Canvas(int size)
            {
                Size = size;
                Rgb = new Color[size * size];
                Alpha = new float[size * size];
                Height = new float[size * size];
            }

            public Color[] Rgba()
            {
                var px = new Color[Size * Size];
                for (int i = 0; i < px.Length; i++)
                {
                    Color c = Rgb[i];
                    px[i] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), Mathf.Clamp01(Alpha[i]));
                }

                return px;
            }

            /// <summary>Copy each transparent pixel's colour from its nearest painted one (two-pass chamfer distance transform).</summary>
            public void Dilate()
            {
                int n = Size * Size;
                var nearest = new int[n];
                var dist = new int[n];
                const int far = int.MaxValue / 4;
                for (int i = 0; i < n; i++)
                {
                    bool painted = Alpha[i] > 0.02f;
                    nearest[i] = painted ? i : -1;
                    dist[i] = painted ? 0 : far;
                }

                void Relax(int i, int j, int cost)
                {
                    if (j < 0 || j >= n) return;
                    if (dist[j] + cost < dist[i])
                    {
                        dist[i] = dist[j] + cost;
                        nearest[i] = nearest[j];
                    }
                }

                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        int i = y * Size + x;
                        if (dist[i] == 0) continue;
                        if (x > 0) Relax(i, i - 1, 3);
                        if (y > 0)
                        {
                            Relax(i, i - Size, 3);
                            if (x > 0) Relax(i, i - Size - 1, 4);
                            if (x < Size - 1) Relax(i, i - Size + 1, 4);
                        }
                    }
                }

                for (int y = Size - 1; y >= 0; y--)
                {
                    for (int x = Size - 1; x >= 0; x--)
                    {
                        int i = y * Size + x;
                        if (dist[i] == 0) continue;
                        if (x < Size - 1) Relax(i, i + 1, 3);
                        if (y < Size - 1)
                        {
                            Relax(i, i + Size, 3);
                            if (x < Size - 1) Relax(i, i + Size + 1, 4);
                            if (x > 0) Relax(i, i + Size - 1, 4);
                        }
                    }
                }

                for (int i = 0; i < n; i++)
                {
                    if (dist[i] != 0 && nearest[i] >= 0)
                    {
                        Rgb[i] = Rgb[nearest[i]];
                    }
                }
            }

            public Color[] NormalMap(float strength)
            {
                var normal = new Color[Size * Size];
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        int xl = Mathf.Max(x - 1, 0), xr = Mathf.Min(x + 1, Size - 1), yd = Mathf.Max(y - 1, 0), yu = Mathf.Min(y + 1, Size - 1);
                        float dx = (Height[y * Size + xr] - Height[y * Size + xl]) * strength * 4f;
                        float dy = (Height[yu * Size + x] - Height[yd * Size + x]) * strength * 4f;
                        Vector3 nrm = new Vector3(-dx, -dy, 1f).normalized;
                        normal[y * Size + x] = new Color(nrm.x * 0.5f + 0.5f, nrm.y * 0.5f + 0.5f, nrm.z * 0.5f + 0.5f, 1f);
                    }
                }

                return normal;
            }
        }

        /// <summary>Paints one cell in unit coordinates (origin bottom-left, y up).</summary>
        private sealed class Painter
        {
            private readonly Canvas canvas;
            private readonly int ox, oy, size;
            private readonly TextureFactory.Noise noise;
            public readonly System.Random Rng;

            public Painter(Canvas canvas, int ox, int oy, int size, System.Random rng)
            {
                this.canvas = canvas;
                this.ox = ox;
                this.oy = oy;
                this.size = size;
                Rng = rng;
                noise = new TextureFactory.Noise(rng.Next());
            }

            public float Rand(float min, float max) => (float)(min + Rng.NextDouble() * (max - min));

            /// <summary>Unit vector at <paramref name="angleDeg"/> clockwise from straight up.</summary>
            public static Vector2 Dir(float angleDeg)
            {
                float a = angleDeg * Mathf.Deg2Rad;
                return new Vector2(Mathf.Sin(a), Mathf.Cos(a));
            }

            private void Blend(int x, int y, Color color, float coverage, float height)
            {
                if (x < 0 || y < 0 || x >= size || y >= size || coverage <= 0f) return;
                int i = (oy + y) * canvas.Size + ox + x;
                float a = Mathf.Clamp01(coverage);
                canvas.Rgb[i] = Color.Lerp(canvas.Rgb[i], color, a);
                canvas.Alpha[i] = Mathf.Max(canvas.Alpha[i], a);
                canvas.Height[i] = Mathf.Lerp(canvas.Height[i], height, a);
            }

            // ---- leaves

            private static float Profile(LeafStyle style, float t)
            {
                float s = Mathf.Sin(Mathf.PI * t);
                switch (style)
                {
                    case LeafStyle.Oval: return Mathf.Pow(s, 0.55f);
                    case LeafStyle.Pointed: return Mathf.Pow(s, 0.42f) * (1f - 0.35f * Mathf.Pow(t, 5f));
                    case LeafStyle.Heart: return Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 0.9f + 0.1f)), 0.5f) * (1f - 0.15f * t);
                    case LeafStyle.Sword: return Mathf.Min(1f, 0.55f + 0.6f * s) * (1f - Mathf.Pow(t, 8f));
                    case LeafStyle.Lobed: return Mathf.Pow(s, 0.4f) * (0.48f + 0.52f * Mathf.Pow(Mathf.Abs(Mathf.Cos(Mathf.PI * t * 4.5f + 0.4f)), 0.35f));
                    case LeafStyle.Blade: return 1f - t * t;
                    case LeafStyle.Needle: return Mathf.Min(1f, 4f * t) * (1f - Mathf.Pow(t, 3f));
                    case LeafStyle.Serrated: return Mathf.Pow(s, 0.5f) * (0.84f + 0.16f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 11f)));
                    case LeafStyle.Jagged: return Mathf.Pow(s, 0.55f) * (0.55f + 0.45f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 7f)));
                    case LeafStyle.IvyLobed: return Mathf.Pow(s, 0.45f) * (0.6f + 0.4f * Mathf.Pow(Mathf.Abs(Mathf.Cos(Mathf.PI * t * 1.5f + 0.5f)), 0.5f));
                    default: return s;
                }
            }

            /// <summary>True where the style cuts the shape away (a heart's notch, a monstera's holes).</summary>
            private static bool Cut(in LeafSpec spec, float t, float wn)
            {
                if (spec.Style == LeafStyle.Heart && t < 0.16f * (1f - Mathf.Abs(wn) * 1.7f))
                {
                    return true;
                }

                if (spec.Holes)
                {
                    for (int k = 0; k < 5; k++)
                    {
                        float tc = 0.28f + k * 0.13f, wc = 0.38f + (k % 2) * 0.12f;
                        float dt = (t - tc) / 0.045f, dw = (Mathf.Abs(wn) - wc) / 0.16f;
                        if (dt * dt + dw * dw < 1f) return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// One leaf: base at <paramref name="basePos"/>, <paramref name="length"/> along <paramref name="angleDeg"/>,
            /// <paramref name="width"/> across at the widest. Shaded lighter along the midrib and veins, darker at the edge.
            /// </summary>
            public void Leaf(Vector2 basePos, float length, float width, float angleDeg, LeafSpec spec)
            {
                Vector2 d = Dir(angleDeg);
                Vector2 s = new Vector2(d.y, -d.x);
                float hw = width / 2f;
                // Bounding box in pixels.
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (float t in new[] { 0f, 1f })
                {
                    foreach (float w in new[] { -1f, 1f })
                    {
                        Vector2 c = basePos + d * (t * length) + s * (w * hw);
                        minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
                        minY = Mathf.Min(minY, c.y); maxY = Mathf.Max(maxY, c.y);
                    }
                }

                int x0 = Mathf.Max(0, Mathf.FloorToInt(minX * size) - 2), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(maxX * size) + 2);
                int y0 = Mathf.Max(0, Mathf.FloorToInt(minY * size) - 2), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(maxY * size) + 2);
                float veinStrength = spec.VeinStrength > 0f ? spec.VeinStrength : 0.55f;
                float veinSlope = spec.VeinSlope > 0f ? spec.VeinSlope : 0.45f;
                int seed = Rng.Next(1000);
                for (int py = y0; py <= y1; py++)
                {
                    for (int px = x0; px <= x1; px++)
                    {
                        var p = new Vector2((px + 0.5f) / size, (py + 0.5f) / size);
                        Vector2 rel = p - basePos;
                        float t = Vector2.Dot(rel, d) / length;
                        if (t < 0f || t > 1f) continue;
                        float w = Vector2.Dot(rel, s) / hw; // -1..1 at full width
                        float profile = Profile(spec.Style, t);
                        if (spec.Wave > 0f)
                        {
                            profile *= 1f - spec.Wave + spec.Wave * Mathf.Sin(t * Mathf.PI * 9f + w * 2f);
                        }

                        float wn = profile > 1e-4f ? w / profile : 10f; // -1..1 across this slice of the leaf
                        float edgePx = (profile - Mathf.Abs(w)) * hw * size;
                        float coverage = Mathf.Clamp01(edgePx + 0.5f);
                        if (coverage <= 0f || Cut(spec, t, wn)) continue;

                        // Shading: lighter along the rib, darker toward the edge, a hint of mottling.
                        float mottle = 1f + (noise.Fbm(t + seed * 0.01f, w * 0.5f + 0.5f, 6, 3, 3) - 0.5f) * 2f * spec.Mottle;
                        float shade = (0.78f + 0.22f * (1f - wn * wn)) * (0.92f + 0.12f * t) * mottle;
                        Color col = spec.Color * shade;
                        float height = spec.Flat ? 0.55f : 0.42f + 0.38f * (1f - wn * wn);

                        if (spec.Bands)
                        {
                            float band = noise.Fbm(t * 1.3f, w * 0.15f, 12, 2, 2);
                            col = Color.Lerp(col, spec.BandColor * shade, Mathf.SmoothStep(0.4f, 0.6f, band));
                            if (Mathf.Abs(wn) > 1f - spec.MarginWidth) col = spec.Margin * shade;
                        }

                        if (spec.VariegationAmount > 0f)
                        {
                            float v = noise.Fbm(t, w * 0.5f + 0.5f, 5, 4, 3);
                            col = Color.Lerp(col, spec.Variegation * shade, Mathf.SmoothStep(0.62f - spec.VariegationAmount * 0.3f, 0.7f, v));
                        }

                        float ribPx = spec.Rib * size;
                        float ribDist = Mathf.Abs(w) * hw * size;
                        if (ribPx > 0f)
                        {
                            float rib = Mathf.Clamp01(1f - ribDist / ribPx) * (1f - t * 0.6f);
                            col = Color.Lerp(col, spec.Vein, rib * 0.8f);
                            height -= rib * 0.22f;
                        }

                        if (spec.VeinCount > 0)
                        {
                            float q = t - Mathf.Abs(wn) * veinSlope;
                            float f = q * spec.VeinCount - Mathf.Floor(q * spec.VeinCount);
                            float vein = Mathf.Clamp01(1f - Mathf.Abs(f - 0.5f) / 0.06f) * (1f - Mathf.Abs(wn)) * (q > 0f ? 1f : 0f);
                            col = Color.Lerp(col, spec.Vein, vein * veinStrength);
                            height += vein * 0.08f;
                        }

                        col.a = 1f;
                        Blend(px, py, col, coverage, height);
                    }
                }
            }

            /// <summary>A capsule stroke between two points with a width that tapers from <paramref name="w0"/> to <paramref name="w1"/>.</summary>
            public void Stroke(Vector2 a, Vector2 b, float w0, float w1, Color color, float height)
            {
                float pad = Mathf.Max(w0, w1);
                int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - pad) * size) - 1), x1 = Mathf.Min(size - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + pad) * size) + 1);
                int y0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - pad) * size) - 1), y1 = Mathf.Min(size - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + pad) * size) + 1);
                Vector2 ab = b - a;
                float len2 = Mathf.Max(1e-6f, ab.sqrMagnitude);
                for (int py = y0; py <= y1; py++)
                {
                    for (int px = x0; px <= x1; px++)
                    {
                        var p = new Vector2((px + 0.5f) / size, (py + 0.5f) / size);
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                        float w = Mathf.Lerp(w0, w1, t) / 2f;
                        float dist = (p - (a + ab * t)).magnitude;
                        float coverage = Mathf.Clamp01((w - dist) * size + 0.5f);
                        if (coverage <= 0f) continue;
                        float shade = 0.8f + 0.2f * (1f - dist / Mathf.Max(w, 1e-4f));
                        Blend(px, py, color * shade, coverage, height * (0.7f + 0.3f * (1f - dist / Mathf.Max(w, 1e-4f))));
                    }
                }
            }

            public void Disc(Vector2 c, float r, Color color, float height)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt((c.x - r) * size) - 1), x1 = Mathf.Min(size - 1, Mathf.CeilToInt((c.x + r) * size) + 1);
                int y0 = Mathf.Max(0, Mathf.FloorToInt((c.y - r) * size) - 1), y1 = Mathf.Min(size - 1, Mathf.CeilToInt((c.y + r) * size) + 1);
                for (int py = y0; py <= y1; py++)
                {
                    for (int px = x0; px <= x1; px++)
                    {
                        var p = new Vector2((px + 0.5f) / size, (py + 0.5f) / size);
                        float dist = (p - c).magnitude;
                        Blend(px, py, color, Mathf.Clamp01((r - dist) * size + 0.5f), height);
                    }
                }
            }

            /// <summary>A rachis with leaflets on both sides, shrinking toward the tip.</summary>
            public void Pinnate(Vector2 basePos, float length, int pinnaePerSide, float pinnaLength, float pinnaWidth, float angle, LeafSpec pinna, Color rachisColor, float rachisWidth, float taper)
            {
                // The rachis curves slightly; leaflets follow its local direction.
                Vector2 tip = basePos + new Vector2(0.06f, length);
                Vector2 control = basePos + new Vector2(-0.05f, length * 0.55f);
                Vector2 At(float u) => (1f - u) * (1f - u) * basePos + 2f * (1f - u) * u * control + u * u * tip;
                for (int i = 0; i < 24; i++)
                {
                    float u0 = i / 24f, u1 = (i + 1) / 24f;
                    Stroke(At(u0), At(u1), rachisWidth * (1f - u0 * 0.7f), rachisWidth * (1f - u1 * 0.7f), rachisColor, 0.5f);
                }

                for (int i = 0; i < pinnaePerSide; i++)
                {
                    float u = 0.06f + (i + 0.5f) / pinnaePerSide * 0.9f;
                    Vector2 at = At(u);
                    Vector2 tangent = (At(u + 0.01f) - At(u - 0.01f)).normalized;
                    float baseAngle = Mathf.Atan2(tangent.x, tangent.y) * Mathf.Rad2Deg;
                    float scale = 1f - taper * Mathf.Pow(u, 2f);
                    foreach (int side in new[] { -1, 1 })
                    {
                        float offset = side * (i % 2 == 0 ? 0f : 0.012f);
                        Vector2 origin = at + tangent * offset;
                        float a = baseAngle + side * angle + Rand(-5f, 5f);
                        Leaf(origin, pinnaLength * scale * Rand(0.9f, 1.05f), pinnaWidth * scale, a, pinna);
                    }
                }

                // A last leaflet straight off the tip.
                Leaf(tip - new Vector2(0f, 0.02f), pinnaLength * (1f - taper) * 0.9f, pinnaWidth * (1f - taper), 4f, pinna);
            }

            /// <summary>Thin brown lines radiating from the centre: the twigs a cluster of leaves hangs on.</summary>
            public void Twigs(Vector2 centre, int count, float reach, Color color)
            {
                for (int i = 0; i < count; i++)
                {
                    float ang = i * 360f / count + Rand(-20f, 20f);
                    Vector2 end = centre + Dir(ang) * reach * Rand(0.7f, 1f);
                    Stroke(centre, end, 0.014f, 0.004f, color, 0.35f);
                    Vector2 mid = Vector2.Lerp(centre, end, 0.55f);
                    Stroke(mid, mid + Dir(ang + Rand(25f, 50f) * (i % 2 == 0 ? 1f : -1f)) * reach * 0.4f, 0.008f, 0.003f, color, 0.35f);
                }
            }

            /// <summary>Many small leaves around the centre, fewer toward the rim; later (upper) leaves are lighter.</summary>
            public void Scatter(int count, float radius, float minLen, float maxLen, float aspect, Color dark, Color light, LeafStyle style, float falloff, bool veined = false, bool downward = false)
            {
                for (int i = 0; i < count; i++)
                {
                    float u = (float)Rng.NextDouble();
                    float r = radius * Mathf.Pow(u, 1f / falloff);
                    Vector2 c = new Vector2(0.5f, 0.5f) + Dir(Rand(0f, 360f)) * r;
                    float len = Rand(minLen, maxLen);
                    float ang = downward ? 180f + Rand(-70f, 70f) : Rand(0f, 360f);
                    Vector2 basePos = c - Dir(ang) * (len / 2f);
                    float layer = (float)i / count;
                    Color col = Color.Lerp(dark, light, Mathf.Clamp01(layer * 1.2f - 0.1f) * Rand(0.7f, 1f));
                    Leaf(basePos, len, len * aspect, ang, new LeafSpec
                    {
                        Style = style, Color = col, Vein = Color.Lerp(col, light, 0.5f), VeinCount = veined ? 3 : 0, Rib = veined ? 0.012f : 0.006f, Mottle = 0.08f,
                    });
                }
            }

            public void Flowers(int count, float radius, float minR, float maxR, Color pink, Color white, Color centre)
            {
                for (int i = 0; i < count; i++)
                {
                    Vector2 c = new Vector2(0.5f, 0.5f) + Dir(Rand(0f, 360f)) * radius * Mathf.Sqrt((float)Rng.NextDouble());
                    Flower(c, Rand(minR, maxR), Rng.NextDouble() < 0.6 ? pink : white, centre);
                }
            }

            public void Flower(Vector2 c, float r, Color petal, Color centre, int petals = 5)
            {
                float start = Rand(0f, 360f);
                for (int k = 0; k < petals; k++)
                {
                    Leaf(c, r, r * (petals > 8 ? 0.35f : 0.8f), start + k * 360f / petals, new LeafSpec { Style = LeafStyle.Oval, Color = petal, Vein = petal, Rib = 0f, Mottle = 0.05f });
                }

                Disc(c, r * 0.28f, centre, 0.8f);
            }

            /// <summary>A conifer spray: a main twig and two side twigs with needles.</summary>
            public void Spray(Vector2 basePos, float length, Color dark, Color light, Color twig)
            {
                void Branch(Vector2 from, float ang, float len, float needleLen)
                {
                    Vector2 to = from + Dir(ang) * len;
                    Stroke(from, to, 0.012f, 0.005f, twig, 0.4f);
                    int needles = Mathf.RoundToInt(len / 0.018f);
                    for (int i = 0; i < needles; i++)
                    {
                        float u = (i + 0.5f) / needles;
                        Vector2 at = Vector2.Lerp(from, to, u);
                        float scale = 1f - 0.5f * Mathf.Pow(u, 2f);
                        foreach (int side in new[] { -1, 1 })
                        {
                            float a = ang + side * Rand(38f, 55f);
                            Color col = Color.Lerp(dark, light, (float)Rng.NextDouble());
                            Leaf(at, needleLen * scale * Rand(0.85f, 1.1f), 0.012f, a, new LeafSpec { Style = LeafStyle.Needle, Color = col, Vein = col, Rib = 0f, Mottle = 0.05f });
                        }
                    }
                }

                Branch(basePos, 0f, length, 0.13f);
                Branch(basePos + Dir(0f) * length * 0.3f, -38f, length * 0.45f, 0.11f);
                Branch(basePos + Dir(0f) * length * 0.5f, 36f, length * 0.4f, 0.10f);
            }

            /// <summary>Grass blades fanning up from one point, each a tapering curve.</summary>
            public void Grass(Vector2 basePos, int blades, float spread, float height, Color baseColor, Color mid, Color tip)
            {
                for (int b = 0; b < blades; b++)
                {
                    float lean = Rand(-spread, spread) * 55f;
                    float len = height * Rand(0.55f, 1f);
                    float curl = Rand(-25f, 25f);
                    Vector2 origin = basePos + new Vector2(Rand(-0.04f, 0.04f), 0f);
                    const int segs = 10;
                    Vector2 prev = origin;
                    for (int i = 0; i < segs; i++)
                    {
                        float u0 = i / (float)segs, u1 = (i + 1) / (float)segs;
                        Vector2 next = origin + Dir(lean + curl * u1 * u1) * (len * u1);
                        float w0 = Mathf.Lerp(0.03f, 0.003f, u0), w1 = Mathf.Lerp(0.03f, 0.003f, u1);
                        Color col = u0 < 0.5f ? Color.Lerp(baseColor, mid, u0 * 2f) : Color.Lerp(mid, tip, (u0 - 0.5f) * 2f);
                        Stroke(prev, next, w0, w1, col, 0.55f);
                        prev = next;
                    }
                }
            }

            public void BarkPatch(Color light, Color dark)
            {
                for (int py = 0; py < size; py++)
                {
                    for (int px = 0; px < size; px++)
                    {
                        float u = (px + 0.5f) / size, v = (py + 0.5f) / size;
                        float ridges = noise.Fbm(u, v, 14, 3, 3);
                        float fissure = noise.Fbm(u + 0.3f, v, 9, 2, 2);
                        float f = Mathf.SmoothStep(0.55f, 0.75f, fissure);
                        Color col = Color.Lerp(Color.Lerp(light, dark, ridges * 0.6f), dark, f);
                        Blend(px, py, col, 1f, 0.5f + ridges * 0.3f - f * 0.35f);
                    }
                }
            }

            public void StemPatch(Color light, Color dark)
            {
                for (int py = 0; py < size; py++)
                {
                    for (int px = 0; px < size; px++)
                    {
                        float u = (px + 0.5f) / size, v = (py + 0.5f) / size;
                        float streak = noise.Fbm(u, v, 10, 2, 2);
                        Blend(px, py, Color.Lerp(light, dark, streak * 0.5f), 1f, 0.5f + streak * 0.1f);
                    }
                }
            }
        }
    }
}
