using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Melange.Psychedelics
{
    /// <summary>
    /// One straight spray stroke on a blotter canvas, as the game's spray system keeps it (its SprayStroke): pixel coordinates
    /// from the canvas's bottom-left, a colour (the game's ESprayColor value) and a brush size in pixels.
    /// </summary>
    public struct Stroke
    {
        public ushort X0, Y0, X1, Y1;
        public byte Color, Size;

        public Stroke(int x0, int y0, int x1, int y1, int color, int size)
        {
            X0 = (ushort)x0; Y0 = (ushort)y0; X1 = (ushort)x1; Y1 = (ushort)y1;
            Color = (byte)color; Size = (byte)size;
        }

        /// <summary>The stroke's length in pixels (a dot counts as 1).</summary>
        public double Length => Math.Max(1.0, Math.Sqrt(Math.Pow(X1 - X0, 2) + Math.Pow(Y1 - Y0, 2)));
    }

    /// <summary>
    /// A painted design's strokes as text, for <see cref="Design.Drawing"/>. The game's own binary form
    /// (SprayStroke.Serialize) drops the brush size, so this keeps all six numbers: "s1:x0,y0,x1,y1,colour,size;...".
    /// Decoding is forgiving: a malformed or off-canvas stroke is skipped, never thrown on, so a damaged save loses strokes,
    /// not the design.
    /// </summary>
    public static class StrokeCodec
    {
        public const string Prefix = "s1:";
        /// <summary>The canvas a blotter design is painted on: the game's default spray surface (450 x 300, 3:2 like the sheet).</summary>
        public const int CanvasWidth = 450, CanvasHeight = 300;
        /// <summary>The game's brush sizes run 10..32 (SprayStroke.StrokeSize_Min/Max); colours 1..8 (ESprayColor Black..Brown).</summary>
        public const int MinSize = 10, MaxSize = 32, MinColor = 1, MaxColor = 8;
        /// <summary>A bound on what one design can keep (the game caps paint at 25,000 painted pixels; real designs are far below this).</summary>
        public const int MaxStrokes = 5000;

        public static string Encode(IEnumerable<Stroke> strokes)
        {
            var sb = new StringBuilder(Prefix);
            bool first = true;
            int n = 0;
            if (strokes != null)
                foreach (var s in strokes)
                {
                    if (!Valid(s, CanvasWidth, CanvasHeight)) continue;
                    if (++n > MaxStrokes) break;
                    if (!first) sb.Append(';');
                    first = false;
                    sb.Append(s.X0.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(s.Y0.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(s.X1.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(s.Y1.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(s.Color.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(s.Size.ToString(CultureInfo.InvariantCulture));
                }
            return sb.ToString();
        }

        public static List<Stroke> Decode(string text)
        {
            var list = new List<Stroke>();
            if (string.IsNullOrEmpty(text) || !text.StartsWith(Prefix, StringComparison.Ordinal)) return list;
            foreach (var part in text.Substring(Prefix.Length).Split(';'))
            {
                if (list.Count >= MaxStrokes) break;
                var f = part.Split(',');
                if (f.Length != 6) continue;
                var v = new int[6];
                bool ok = true;
                for (int i = 0; i < 6 && ok; i++) ok = int.TryParse(f[i], NumberStyles.None, CultureInfo.InvariantCulture, out v[i]);
                if (!ok) continue;
                if (v[0] > ushort.MaxValue || v[1] > ushort.MaxValue || v[2] > ushort.MaxValue || v[3] > ushort.MaxValue || v[4] > 255 || v[5] > 255) continue;
                var s = new Stroke(v[0], v[1], v[2], v[3], v[4], v[5]);
                if (Valid(s, CanvasWidth, CanvasHeight)) list.Add(s);
            }
            return list;
        }

        /// <summary>
        /// A stroke the game can draw on a canvas this size: a real colour and brush, and the whole brush on the canvas (the
        /// game's Drawing refuses a pixel whose brush square leaves the texture, and its painting keeps a brush's half-width
        /// from every edge).
        /// </summary>
        public static bool Valid(Stroke s, int width, int height)
        {
            if (s.Color < MinColor || s.Color > MaxColor || s.Size < MinSize || s.Size > MaxSize) return false;
            int pad = (s.Size + 1) / 2;
            return Inside(s.X0, pad, width) && Inside(s.X1, pad, width) && Inside(s.Y0, pad, height) && Inside(s.Y1, pad, height);
        }

        private static bool Inside(int v, int pad, int size) => v >= pad && v <= size - pad;

        /// <summary>True when the text holds at least one drawable stroke.</summary>
        public static bool HasStrokes(string text) => Decode(text).Count > 0;
    }

    /// <summary>Names a painted design after its main colour, e.g. "Red design 3", since there is no keyboard while painting.</summary>
    public static class DesignNamer
    {
        /// <summary>The game's spray colours by ESprayColor value.</summary>
        public static readonly string[] ColorNames = { "Plain", "Black", "White", "Red", "Green", "Blue", "Yellow", "Pink", "Brown" };

        /// <summary>The colour covering the most canvas (stroke length times brush size); 0 when there are no strokes.</summary>
        public static int MainColor(IEnumerable<Stroke> strokes)
        {
            var weight = new double[ColorNames.Length];
            if (strokes != null)
                foreach (var s in strokes)
                    if (s.Color < weight.Length) weight[s.Color] += s.Length * s.Size;
            int best = 0;
            for (int c = 1; c < weight.Length; c++) if (weight[c] > weight[best]) best = c;
            return best;
        }

        public static string Name(IEnumerable<Stroke> strokes, int number) => $"{ColorNames[MainColor(strokes)]} design {number}";
    }
}
