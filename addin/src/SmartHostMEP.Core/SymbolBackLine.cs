using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartHostMEP.Core
{
    /// <summary>The flat back of a wall-device symbol, in the block's own coordinates.</summary>
    public class BackLine
    {
        public V3 Dir;      // along the back line (unit)
        public V3 Inward;   // from the back line toward the rest of the symbol (unit): the facing
        public double LengthRatio;   // back line length / symbol size
    }

    /// <summary>
    /// Many wall-device symbols have a straight back that touches the wall: the flat side of a
    /// socket's half circle, the base line of a switch. That line tells the wall direction, and the
    /// rest of the symbol lies on the room side of it - independent of how the block was rotated
    /// or which layer it is on.
    /// A line counts as the back when it is long (at least half the symbol size), all the other line
    /// work is on one side of it, and no equally long parallel line sits on the other side (a plain
    /// rectangle has no single back). Symbols without such a line (circles, crosses) give null.
    /// </summary>
    public static class SymbolBackLine
    {
        public const double MinLengthRatio = 0.5;
        const double SideTol = 0.05;      // fraction of the symbol size
        const double ParallelDeg = 5;

        /// <summary>Paths as in <see cref="BlockSymbol.Paths"/> (x0, y0, x1, y1, ...).</summary>
        public static BackLine Find(IEnumerable<double[]> paths)
        {
            var pts = new List<V3>();
            var segs = new List<(V3 a, V3 b, double len)>();
            foreach (var p in paths ?? Enumerable.Empty<double[]>())
                for (int i = 0; i + 1 < p.Length; i += 2)
                {
                    var q = new V3(p[i], p[i + 1], 0);
                    pts.Add(q);
                    if (i + 3 < p.Length)
                    {
                        var r = new V3(p[i + 2], p[i + 3], 0);
                        double len = (r - q).Length;
                        if (len > 1e-9) segs.Add((q, r, len));
                    }
                }
            if (segs.Count == 0) return null;
            double size = Math.Max(pts.Max(q => q.X) - pts.Min(q => q.X), pts.Max(q => q.Y) - pts.Min(q => q.Y));
            if (size < 1e-9) return null;
            double tol = SideTol * size;
            double cosPar = Math.Cos(ParallelDeg * Math.PI / 180);

            // Try the longest straight lines first (tessellated arcs give only short pieces).
            foreach (var s in segs.OrderByDescending(x => x.len).Take(4))
            {
                if (s.len < MinLengthRatio * size) break;
                var dir = (s.b - s.a) * (1 / s.len);
                var n = new V3(-dir.Y, dir.X, 0);
                int pos = 0, neg = 0;
                foreach (var q in pts)
                {
                    double d = n.Dot(q - s.a);
                    if (d > tol) pos++;
                    else if (d < -tol) neg++;
                }
                if ((pos > 0) == (neg > 0)) continue;          // geometry on both sides, or none off the line
                var inward = pos > 0 ? n : n * -1;
                // A rectangle-like symbol: an equally long parallel line on the far side - no single back.
                bool twin = segs.Any(o =>
                    o.len >= 0.8 * s.len &&
                    Math.Abs(((o.b - o.a) * (1 / o.len)).Dot(dir)) >= cosPar &&
                    inward.Dot((o.a + o.b) * 0.5 - s.a) > tol);
                if (twin) continue;
                return new BackLine { Dir = dir, Inward = inward, LengthRatio = s.len / size };
            }
            return null;
        }
    }
}
