using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DMCRemapper.Core
{
    /// <summary>
    /// The normalized 2D symbol of a CAD block or of a Revit family (Auto-match by symbol geometry):
    /// points sampled evenly along the line work after moving it to the origin, removing its rotation
    /// (main axis along X) and scaling its larger side to 1; plus element counts, aspect ratio and a
    /// 64 x 64 raster. Plain data, so it can be cached on disk (see <see cref="SignatureCache"/>).
    /// </summary>
    public class SymbolSignature
    {
        /// <summary>Bump when the normalization changes: cached signatures of older versions are recomputed.</summary>
        public const int Version = 3;
        public const int SampleCount = 128;
        public const int RasterSize = 64;

        /// <summary>x0, y0, x1, y1, ... in the normalized frame (bounding box centred on 0, larger side 1).</summary>
        public double[] Points = new double[0];
        public int Lines, Arcs, Circles;
        /// <summary>Short side / long side of the symbol after removing its rotation (0..1).</summary>
        public double Aspect;
        /// <summary>Share (0..1) of the line work's length that is curved (arcs, circles), told from the
        /// geometry itself, so a circle drawn as a many-sided polyline still counts as curved.</summary>
        public double Curved;
        /// <summary>64 rows of 64 bits; cell (0, 0) at the bottom left of the [-0.5, 0.5] square.</summary>
        public ulong[] Raster = new ulong[RasterSize];
        /// <summary>The family view the symbol was read from (Ref. Level, Front, Back); "" for CAD blocks.</summary>
        public string View = "";

        public int Elements => Lines + Arcs + Circles;
        public bool IsEmpty => Points == null || Points.Length < 4;
        /// <summary>The outline of 3D geometry (a family with no 2D symbol): a weak signature.</summary>
        public bool IsOutline3D => (View ?? "").EndsWith(Outline3DSuffix, StringComparison.Ordinal);
        public const string Outline3DSuffix = " (3D)";
        public int PointCount => Points == null ? 0 : Points.Length / 2;

        // ---- building ------------------------------------------------------------------------

        /// <summary>Symbols larger than this (mm, either side) are not device symbols (a whole plan or
        /// xref drawn as a block) and get no signature: they are matched by name only.</summary>
        public const double MaxSizeMm = 6000;

        /// <summary>True when the symbol was left out for being larger than <see cref="MaxSizeMm"/>.</summary>
        public bool TooBig;

        public static SymbolSignature From(BlockSymbol symbol, string view = "") =>
            symbol == null ? new SymbolSignature { View = view ?? "" }
                           : From(symbol.Paths, symbol.Lines, symbol.Arcs, symbol.Circles, view);

        /// <summary>Signature of a CAD block symbol (geometry in feet); empty and <see cref="TooBig"/>
        /// when it is larger than <see cref="MaxSizeMm"/>.</summary>
        public static SymbolSignature FromBlock(BlockSymbol symbol)
        {
            var b = symbol?.Bounds();
            if (b != null && Math.Max(b[2] - b[0], b[3] - b[1]) * 304.8 > MaxSizeMm)
                return new SymbolSignature { TooBig = true };
            return From(symbol);
        }

        /// <summary>Builds the signature of the given paths (flat x, y polylines in any units).</summary>
        public static SymbolSignature From(IList<double[]> paths, int lines, int arcs, int circles, string view = "")
        {
            var sig = new SymbolSignature { Lines = lines, Arcs = arcs, Circles = circles, View = view ?? "" };
            var segs = new List<double[]>();
            double curvedLen = 0, totalLen = 0;
            if (paths != null)
                foreach (var p in paths)
                {
                    var own = new List<double[]>();
                    for (int i = 0; i + 3 < p.Length; i += 2)
                        if (Math.Abs(p[i + 2] - p[i]) + Math.Abs(p[i + 3] - p[i + 1]) > 1e-12)
                            own.Add(new[] { p[i], p[i + 1], p[i + 2], p[i + 3] });
                    for (int i = 0; i < own.Count; i++)
                    {
                        double len = Length(own[i]);
                        totalLen += len;
                        if ((i > 0 && IsBend(own[i - 1], own[i])) || (i + 1 < own.Count && IsBend(own[i], own[i + 1]))) curvedLen += len;
                    }
                    segs.AddRange(own);
                }
            if (segs.Count == 0) return sig;
            sig.Curved = totalLen > 0 ? curvedLen / totalLen : 0;

            // 1. Rotation: the smallest rectangle around the line work (one side on an edge of its
            // convex hull) is turned square to the axes. Nearly equal choices (round symbols) take the
            // turn closest to the symbol's own axes; 90 deg steps and mirroring are tried when
            // comparing (see SymbolMatcher).
            var dense = Sample(segs, 512);
            double angle = MinAreaAngle(dense);
            double mx = 0, my = 0;
            int n = dense.Length / 2;
            for (int i = 0; i < dense.Length; i += 2) { mx += dense[i]; my += dense[i + 1]; }
            mx /= n; my /= n;
            double c = Math.Cos(-angle), s = Math.Sin(-angle);
            foreach (var g in segs)
                for (int k = 0; k < 4; k += 2)
                {
                    double x = g[k] - mx, y = g[k + 1] - my;
                    g[k] = x * c - y * s;
                    g[k + 1] = x * s + y * c;
                }

            // 2. Bounding box centred on the origin, larger side 1.
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var g in segs)
                for (int k = 0; k < 4; k += 2)
                {
                    x0 = Math.Min(x0, g[k]); x1 = Math.Max(x1, g[k]);
                    y0 = Math.Min(y0, g[k + 1]); y1 = Math.Max(y1, g[k + 1]);
                }
            double w = x1 - x0, h = y1 - y0, size = Math.Max(w, h);
            if (size <= 1e-12) return sig;
            double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
            foreach (var g in segs)
                for (int k = 0; k < 4; k += 2)
                {
                    g[k] = (g[k] - cx) / size;
                    g[k + 1] = (g[k + 1] - cy) / size;
                }
            sig.Aspect = Math.Min(w, h) / size;

            // 3. Points along the line work, and the raster.
            sig.Points = Sample(segs, SampleCount);
            foreach (var g in segs) DrawSegment(sig.Raster, g);
            return sig;
        }

        /// <summary>Turn (radians, 0..90 deg) that makes the bounding box of the points smallest.
        /// Within 2 % of the smallest area, the turn closest to 0 or 90 deg wins; within 2 deg of
        /// those it is exactly 0 (drawing noise).</summary>
        public static double MinAreaAngle(double[] pts)
        {
            var hull = Hull(pts);
            if (hull.Count < 3) return 0;
            var angles = new List<double> { 0 };
            for (int i = 0; i < hull.Count; i++)
            {
                var a = hull[i];
                var b = hull[(i + 1) % hull.Count];
                double ang = Math.Atan2(b[1] - a[1], b[0] - a[0]);
                ang %= Math.PI / 2;
                if (ang < 0) ang += Math.PI / 2;
                angles.Add(ang);
            }
            double Area(double ang)
            {
                double c = Math.Cos(-ang), s = Math.Sin(-ang);
                double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
                foreach (var p in hull)
                {
                    double x = p[0] * c - p[1] * s, y = p[0] * s + p[1] * c;
                    x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                }
                return (x1 - x0) * (y1 - y0);
            }
            var areas = angles.Select(Area).ToList();
            double min = areas.Min();
            double OffAxis(double ang) => Math.Min(ang, Math.PI / 2 - ang);
            double best = 0, bestOff = double.MaxValue;
            for (int i = 0; i < angles.Count; i++)
                if (areas[i] <= min * 1.02 + 1e-18 && OffAxis(angles[i]) < bestOff)
                {
                    best = angles[i];
                    bestOff = OffAxis(angles[i]);
                }
            return bestOff < 2 * Math.PI / 180 ? 0 : best;
        }

        /// <summary>Convex hull (monotone chain) of flat x, y points.</summary>
        static List<double[]> Hull(double[] pts)
        {
            var p = new List<double[]>();
            for (int i = 0; i + 1 < pts.Length; i += 2) p.Add(new[] { pts[i], pts[i + 1] });
            p.Sort((a, b) => a[0] != b[0] ? a[0].CompareTo(b[0]) : a[1].CompareTo(b[1]));
            if (p.Count < 3) return p;
            double Cross(double[] o, double[] a, double[] b) => (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
            var h = new List<double[]>();
            foreach (var q in p)
            {
                while (h.Count >= 2 && Cross(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1);
                h.Add(q);
            }
            int lower = h.Count + 1;
            for (int i = p.Count - 2; i >= 0; i--)
            {
                var q = p[i];
                while (h.Count >= lower && Cross(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1);
                h.Add(q);
            }
            h.RemoveAt(h.Count - 1);
            return h;
        }

        static double Length(double[] g) => Math.Sqrt((g[2] - g[0]) * (g[2] - g[0]) + (g[3] - g[1]) * (g[3] - g[1]));

        /// <summary>Two joined segments turning by a small angle (0.5-35 deg): part of a curve.</summary>
        static bool IsBend(double[] a, double[] b)
        {
            double ax = a[2] - a[0], ay = a[3] - a[1], bx = b[2] - b[0], by = b[3] - b[1];
            double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
            if (la <= 0 || lb <= 0) return false;
            double cos = Math.Max(-1, Math.Min(1, (ax * bx + ay * by) / (la * lb)));
            double deg = Math.Acos(cos) * 180 / Math.PI;
            return deg > 0.5 && deg < 35;
        }

        /// <summary><paramref name="count"/> points spread evenly (by length) along the segments.</summary>
        static double[] Sample(List<double[]> segs, int count)
        {
            var lengths = segs.Select(g => Math.Sqrt((g[2] - g[0]) * (g[2] - g[0]) + (g[3] - g[1]) * (g[3] - g[1]))).ToArray();
            double total = lengths.Sum();
            var pts = new double[count * 2];
            double step = total / count, t = step / 2;
            int si = 0;
            double start = 0;
            for (int k = 0; k < count; k++, t += step)
            {
                while (si < segs.Count - 1 && start + lengths[si] < t) start += lengths[si++];
                var g = segs[si];
                double f = lengths[si] > 0 ? Math.Max(0, Math.Min(1, (t - start) / lengths[si])) : 0;
                pts[2 * k] = g[0] + (g[2] - g[0]) * f;
                pts[2 * k + 1] = g[1] + (g[3] - g[1]) * f;
            }
            return pts;
        }

        static void DrawSegment(ulong[] raster, double[] g)
        {
            double len = Math.Sqrt((g[2] - g[0]) * (g[2] - g[0]) + (g[3] - g[1]) * (g[3] - g[1]));
            int steps = Math.Max(1, (int)Math.Ceiling(len * RasterSize * 2));
            for (int k = 0; k <= steps; k++)
            {
                double f = (double)k / steps;
                SetCell(raster, Cell(g[0] + (g[2] - g[0]) * f), Cell(g[1] + (g[3] - g[1]) * f));
            }
        }

        static int Cell(double v) => Math.Max(0, Math.Min(RasterSize - 1, (int)Math.Floor((v + 0.5) * RasterSize)));
        static void SetCell(ulong[] r, int i, int j) => r[j] |= 1UL << i;
        internal static bool GetCell(ulong[] r, int i, int j) => i >= 0 && j >= 0 && i < RasterSize && j < RasterSize && (r[j] >> i & 1UL) != 0;

        // ---- the eight orientations (4 x 90 deg, each also mirrored) ------------------------------

        /// <summary>Orientation <paramref name="t"/> (0..7): rotate by (t mod 4) x 90 deg, then mirror in X when t &gt;= 4.</summary>
        public static void Orient(int t, double x, double y, out double ox, out double oy)
        {
            switch (t & 3)
            {
                case 1: ox = -y; oy = x; break;
                case 2: ox = -x; oy = -y; break;
                case 3: ox = y; oy = -x; break;
                default: ox = x; oy = y; break;
            }
            if (t >= 4) ox = -ox;
        }

        /// <summary>The orientation that undoes <paramref name="t"/> (mirrored ones undo themselves).</summary>
        public static int Inverse(int t) => t >= 4 ? t : (4 - t) & 3;

        /// <summary>Raster cell (i, j) in orientation <paramref name="t"/> (the grid is symmetric about the centre).</summary>
        internal static void OrientCell(int t, int i, int j, out int oi, out int oj)
        {
            int m = RasterSize - 1;
            switch (t & 3)
            {
                case 1: oi = m - j; oj = i; break;
                case 2: oi = m - i; oj = m - j; break;
                case 3: oi = j; oj = m - i; break;
                default: oi = i; oj = j; break;
            }
            if (t >= 4) oi = m - oi;
        }

        // ---- distance field (fast chamfer distance) ---------------------------------------------

        const int FieldSize = 64;
        const double FieldHalf = 0.6;
        float[] _field;

        /// <summary>Distance from (x, y) (normalized frame) to the nearest sampled point.</summary>
        public double DistanceTo(double x, double y)
        {
            if (IsEmpty) return 1;
            if (_field == null) _field = BuildField();
            double cell = 2 * FieldHalf / FieldSize;
            double fx = (x + FieldHalf) / cell - 0.5, fy = (y + FieldHalf) / cell - 0.5;
            double cxp = Math.Max(0, Math.Min(FieldSize - 1, fx)), cyp = Math.Max(0, Math.Min(FieldSize - 1, fy));
            int i = (int)Math.Floor(cxp), j = (int)Math.Floor(cyp);
            int i1 = Math.Min(FieldSize - 1, i + 1), j1 = Math.Min(FieldSize - 1, j + 1);
            double u = cxp - i, v = cyp - j;
            double d = (1 - u) * (1 - v) * _field[j * FieldSize + i] + u * (1 - v) * _field[j * FieldSize + i1]
                     + (1 - u) * v * _field[j1 * FieldSize + i] + u * v * _field[j1 * FieldSize + i1];
            // Outside the field: add the distance to its edge.
            double ox = Math.Abs(fx - cxp) * cell, oy = Math.Abs(fy - cyp) * cell;
            return d + Math.Sqrt(ox * ox + oy * oy);
        }

        float[] BuildField()
        {
            var f = new float[FieldSize * FieldSize];
            double cell = 2 * FieldHalf / FieldSize;
            for (int j = 0; j < FieldSize; j++)
                for (int i = 0; i < FieldSize; i++)
                {
                    double x = -FieldHalf + (i + 0.5) * cell, y = -FieldHalf + (j + 0.5) * cell, best = double.MaxValue;
                    for (int k = 0; k + 1 < Points.Length; k += 2)
                    {
                        double dx = Points[k] - x, dy = Points[k + 1] - y, d = dx * dx + dy * dy;
                        if (d < best) best = d;
                    }
                    f[j * FieldSize + i] = (float)Math.Sqrt(best);
                }
            return f;
        }

        ulong[] _thick;
        /// <summary>The raster with every set cell grown by one cell (tolerant overlap test).</summary>
        internal ulong[] Thick()
        {
            if (_thick != null) return _thick;
            var t = new ulong[RasterSize];
            for (int j = 0; j < RasterSize; j++)
            {
                ulong row = Raster[j];
                if (j > 0) row |= Raster[j - 1];
                if (j < RasterSize - 1) row |= Raster[j + 1];
                t[j] = row | row << 1 | row >> 1;
            }
            return _thick = t;
        }

        // ---- identity (learned matches) -----------------------------------------------------------

        /// <summary>
        /// A short text that is the same for the same symbol drawn at another size, rotated by 90 deg
        /// steps or mirrored: element counts and a coarse 16 x 16 raster in the orientation whose text
        /// sorts first. "" for an empty symbol.
        /// </summary>
        public string Hash()
        {
            if (IsEmpty) return "";
            string best = null;
            for (int t = 0; t < 8; t++)
            {
                var coarse = new ulong[4];
                for (int j = 0; j < RasterSize; j++)
                    for (int i = 0; i < RasterSize; i++)
                    {
                        if (!GetCell(Raster, i, j)) continue;
                        OrientCell(t, i, j, out var oi, out var oj);
                        int bit = (oj / 4) * 16 + oi / 4;
                        coarse[bit / 64] |= 1UL << (bit % 64);
                    }
                var text = string.Concat(coarse.Select(v => v.ToString("x16")));
                if (best == null || string.CompareOrdinal(text, best) < 0) best = text;
            }
            return $"L{Lines}A{Arcs}C{Circles}-{best}";
        }

        // ---- text form (cache file) ---------------------------------------------------------------

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>One tab-separated line (no key): view, counts, aspect, points, raster.</summary>
        public string ToText()
        {
            var pts = new byte[Points.Length * 4];
            for (int i = 0; i < Points.Length; i++) BitConverter.GetBytes((float)Points[i]).CopyTo(pts, i * 4);
            var raster = new byte[RasterSize * 8];
            for (int j = 0; j < RasterSize; j++) BitConverter.GetBytes(Raster[j]).CopyTo(raster, j * 8);
            return string.Join("\t", (View ?? "").Replace('\t', ' '), Lines.ToString(Inv), Arcs.ToString(Inv), Circles.ToString(Inv),
                Aspect.ToString("0.#####", Inv), Convert.ToBase64String(pts), Convert.ToBase64String(raster), Curved.ToString("0.####", Inv));
        }

        /// <summary>Reads <see cref="ToText"/>; null when the text is damaged.</summary>
        public static SymbolSignature FromText(string text)
        {
            try
            {
                var f = (text ?? "").Split('\t');
                if (f.Length < 7) return null;
                var sig = new SymbolSignature
                {
                    View = f[0],
                    Lines = int.Parse(f[1], Inv),
                    Arcs = int.Parse(f[2], Inv),
                    Circles = int.Parse(f[3], Inv),
                    Aspect = double.Parse(f[4], NumberStyles.Float, Inv),
                };
                var pts = Convert.FromBase64String(f[5]);
                if (pts.Length % 8 != 0) return null;
                sig.Points = new double[pts.Length / 4];
                for (int i = 0; i < sig.Points.Length; i++) sig.Points[i] = BitConverter.ToSingle(pts, i * 4);
                var raster = Convert.FromBase64String(f[6]);
                if (raster.Length != RasterSize * 8) return null;
                for (int j = 0; j < RasterSize; j++) sig.Raster[j] = BitConverter.ToUInt64(raster, j * 8);
                if (f.Length > 7) sig.Curved = double.Parse(f[7], NumberStyles.Float, Inv);
                return sig;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public override string ToString() =>
            TooBig ? $"(larger than {MaxSizeMm:0} mm: not a device symbol)" : IsEmpty ? "(no symbol)" : string.Format(Inv, "{0} lines, {1} arcs, {2} circles, aspect {3:0.00}, curved {4:0%}", Lines, Arcs, Circles, Aspect, Curved);
    }
}
