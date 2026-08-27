using System.Collections.Generic;
using UnityEngine;

namespace Vent.Editor
{
    /// <summary>
    /// A 5x7 bitmap font, defined in code like everything else in this project: there is no font
    /// asset to import, and the whiteboard in the lobby has to carry words a player can actually
    /// read. Each glyph is seven rows of five characters, '1' where there is ink, so the shapes
    /// are reviewable in the diff rather than hidden in a blob of hex.
    ///
    /// Draws into a <see cref="Color"/> buffer laid out for <see cref="Texture2D.SetPixels"/> —
    /// row 0 at the bottom — but takes <paramref name="top"/> in screen order, counting down from
    /// the top of the image, because that is how you lay out a line of text.
    /// </summary>
    public static class GlyphFont
    {
        public const int GlyphWidth = 5;
        public const int GlyphHeight = 7;

        /// <summary>Columns one character advances, including the single-column gap after it.</summary>
        public const int Advance = GlyphWidth + 1;

        /// <summary>Width in source pixels of <paramref name="text"/> at scale 1 (no trailing gap).</summary>
        public static int Width(string text) => text.Length == 0 ? 0 : text.Length * Advance - 1;

        /// <summary>
        /// Stamp <paramref name="text"/> in <paramref name="ink"/>. <paramref name="left"/> and
        /// <paramref name="top"/> are the top-left corner in image pixels; each font pixel becomes
        /// a <paramref name="scale"/>-square block. Characters with no glyph are drawn as a space.
        /// </summary>
        public static void Draw(Color[] pixels, int width, int height, string text, int left, int top, int scale, Color ink)
        {
            if (string.IsNullOrEmpty(text) || scale < 1)
            {
                return;
            }

            for (int i = 0; i < text.Length; i++)
            {
                if (!Glyphs.TryGetValue(char.ToUpperInvariant(text[i]), out string[] rows))
                {
                    continue;
                }

                int originX = left + i * Advance * scale;
                for (int gy = 0; gy < GlyphHeight; gy++)
                {
                    string row = rows[gy];
                    for (int gx = 0; gx < GlyphWidth; gx++)
                    {
                        if (row[gx] != '1')
                        {
                            continue;
                        }

                        Block(pixels, width, height, originX + gx * scale, top + gy * scale, scale, ink);
                    }
                }
            }
        }

        /// <summary>One font pixel: a scale-square block, clipped to the image.</summary>
        private static void Block(Color[] pixels, int width, int height, int left, int top, int scale, Color ink)
        {
            for (int dy = 0; dy < scale; dy++)
            {
                int y = top + dy;
                if (y < 0 || y >= height)
                {
                    continue;
                }

                // SetPixels counts rows from the bottom; the caller thinks in lines down the page.
                int rowStart = (height - 1 - y) * width;
                for (int dx = 0; dx < scale; dx++)
                {
                    int x = left + dx;
                    if (x >= 0 && x < width)
                    {
                        pixels[rowStart + x] = ink;
                    }
                }
            }
        }

        private static readonly Dictionary<char, string[]> Glyphs = new()
        {
            [' '] = new[] { "00000", "00000", "00000", "00000", "00000", "00000", "00000" },
            ['A'] = new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" },
            ['B'] = new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" },
            ['C'] = new[] { "01110", "10001", "10000", "10000", "10000", "10001", "01110" },
            ['D'] = new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" },
            ['E'] = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" },
            ['F'] = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "10000" },
            ['G'] = new[] { "01110", "10001", "10000", "10111", "10001", "10001", "01111" },
            ['H'] = new[] { "10001", "10001", "10001", "11111", "10001", "10001", "10001" },
            ['I'] = new[] { "11111", "00100", "00100", "00100", "00100", "00100", "11111" },
            ['J'] = new[] { "00111", "00010", "00010", "00010", "00010", "10010", "01100" },
            ['K'] = new[] { "10001", "10010", "10100", "11000", "10100", "10010", "10001" },
            ['L'] = new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" },
            ['M'] = new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" },
            ['N'] = new[] { "10001", "11001", "10101", "10011", "10001", "10001", "10001" },
            ['O'] = new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" },
            ['P'] = new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" },
            ['Q'] = new[] { "01110", "10001", "10001", "10001", "10101", "10010", "01101" },
            ['R'] = new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" },
            ['S'] = new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" },
            ['T'] = new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" },
            ['U'] = new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" },
            ['V'] = new[] { "10001", "10001", "10001", "10001", "10001", "01010", "00100" },
            ['W'] = new[] { "10001", "10001", "10001", "10101", "10101", "11011", "10001" },
            ['X'] = new[] { "10001", "10001", "01010", "00100", "01010", "10001", "10001" },
            ['Y'] = new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" },
            ['Z'] = new[] { "11111", "00001", "00010", "00100", "01000", "10000", "11111" },
            ['0'] = new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" },
            ['1'] = new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
            ['2'] = new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" },
            ['3'] = new[] { "11111", "00010", "00100", "00010", "00001", "10001", "01110" },
            ['4'] = new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" },
            ['5'] = new[] { "11111", "10000", "11110", "00001", "00001", "10001", "01110" },
            ['6'] = new[] { "00110", "01000", "10000", "11110", "10001", "10001", "01110" },
            ['7'] = new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
            ['8'] = new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
            ['9'] = new[] { "01110", "10001", "10001", "01111", "00001", "00010", "01100" },
            ['-'] = new[] { "00000", "00000", "00000", "11111", "00000", "00000", "00000" },
            ['+'] = new[] { "00000", "00100", "00100", "11111", "00100", "00100", "00000" },
            ['.'] = new[] { "00000", "00000", "00000", "00000", "00000", "01100", "01100" },
            [','] = new[] { "00000", "00000", "00000", "00000", "01100", "01100", "01000" },
            [':'] = new[] { "00000", "01100", "01100", "00000", "01100", "01100", "00000" },
            ['/'] = new[] { "00001", "00010", "00010", "00100", "01000", "01000", "10000" },
            ['>'] = new[] { "10000", "01000", "00100", "00010", "00100", "01000", "10000" },
            ['!'] = new[] { "00100", "00100", "00100", "00100", "00100", "00000", "00100" },
            ['?'] = new[] { "01110", "10001", "00001", "00010", "00100", "00000", "00100" },
            ['\''] = new[] { "00100", "00100", "01000", "00000", "00000", "00000", "00000" },
            ['('] = new[] { "00010", "00100", "01000", "01000", "01000", "00100", "00010" },
            [')'] = new[] { "01000", "00100", "00010", "00010", "00010", "00100", "01000" },
        };
    }
}
