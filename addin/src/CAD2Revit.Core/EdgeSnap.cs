using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CAD2Revit.Core
{
    /// <summary>Where an edge for a vertical plane came from (in search order).</summary>
    public enum EdgeSource { Wall, Column, Dwg }

    /// <summary>The nearest edge found for a block: the segment (plan, feet) and its payload.</summary>
    public class EdgeHit<T>
    {
        public double X1, Y1, X2, Y2;
        public T Payload;
        public double DistanceFt;     // plan distance from the block point to the segment
    }

    /// <summary>
    /// Plan segments (wall faces, column faces, DWG lines) in a uniform grid, built once per run,
    /// so each block only looks at the few segments in the cells around it - no full scans.
    /// </summary>
    public class EdgeIndex<T>
    {
        readonly double _cell;
        readonly List<(double x1, double y1, double x2, double y2, T payload)> _segs = new List<(double, double, double, double, T)>();
        readonly Dictionary<(long, long), List<int>> _grid = new Dictionary<(long, long), List<int>>();

        /// <param name="cellFt">Grid cell size (feet), e.g. the search radius.</param>
        public EdgeIndex(double cellFt) { _cell = Math.Max(cellFt, 0.1); }

        public int Count => _segs.Count;

        long C(double v) => (long)Math.Floor(v / _cell);

        public void Add(double x1, double y1, double x2, double y2, T payload)
        {
            int i = _segs.Count;
            _segs.Add((x1, y1, x2, y2, payload));
            long cx0 = C(Math.Min(x1, x2)), cx1 = C(Math.Max(x1, x2));
            long cy0 = C(Math.Min(y1, y2)), cy1 = C(Math.Max(y1, y2));
            // Very long segments (whole building walls) cover many cells; cap the work by walking
            // the cells along the segment instead of its whole bounding box.
            if ((cx1 - cx0 + 1) * (cy1 - cy0 + 1) > 64)
            {
                double len = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
                int steps = (int)Math.Ceiling(len / (_cell * 0.5)) + 1;
                var seen = new HashSet<(long, long)>();
                for (int s = 0; s <= steps; s++)
                {
                    double t = (double)s / steps;
                    long cx = C(x1 + (x2 - x1) * t), cy = C(y1 + (y2 - y1) * t);
                    for (long dx = -1; dx <= 1; dx++)
                        for (long dy = -1; dy <= 1; dy++)
                            if (seen.Add((cx + dx, cy + dy))) Cell(cx + dx, cy + dy).Add(i);
                }
                return;
            }
            for (long cx = cx0; cx <= cx1; cx++)
                for (long cy = cy0; cy <= cy1; cy++)
                    Cell(cx, cy).Add(i);
        }

        List<int> Cell(long cx, long cy)
        {
            if (!_grid.TryGetValue((cx, cy), out var l)) _grid[(cx, cy)] = l = new List<int>();
            return l;
        }

        /// <summary>Plan distance from (px, py) to the segment.</summary>
        public static double SegmentDistance(double px, double py, double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1, dy = y2 - y1, l2 = dx * dx + dy * dy;
            double t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((px - x1) * dx + (py - y1) * dy) / l2));
            double qx = x1 + dx * t - px, qy = y1 + dy * t - py;
            return Math.Sqrt(qx * qx + qy * qy);
        }

        /// <summary>Nearest segment within <paramref name="radiusFt"/> whose payload passes
        /// <paramref name="accept"/>, or null.</summary>
        public EdgeHit<T> Nearest(double x, double y, double radiusFt, Func<T, bool> accept = null)
        {
            EdgeHit<T> best = null;
            var done = new HashSet<int>();
            for (long cx = C(x - radiusFt); cx <= C(x + radiusFt); cx++)
                for (long cy = C(y - radiusFt); cy <= C(y + radiusFt); cy++)
                {
                    if (!_grid.TryGetValue((cx, cy), out var list)) continue;
                    foreach (var i in list)
                    {
                        if (!done.Add(i)) continue;
                        var s = _segs[i];
                        double d = SegmentDistance(x, y, s.x1, s.y1, s.x2, s.y2);
                        if (d > radiusFt || (best != null && d >= best.DistanceFt)) continue;
                        if (accept != null && !accept(s.payload)) continue;
                        best = new EdgeHit<T> { X1 = s.x1, Y1 = s.y1, X2 = s.x2, Y2 = s.y2, Payload = s.payload, DistanceFt = d };
                    }
                }
            return best;
        }
    }

    /// <summary>How the vertical plane is positioned once an edge is found.</summary>
    public enum PlanePosition { SnapToFace, ThroughBlockPoint }

    /// <summary>Result of <see cref="EdgeSnap.Plan"/>.</summary>
    public struct EdgePlan
    {
        public V3 Point;      // where the family goes (on the plane), at height z
        public V3 Facing;     // plane normal: away from the edge, toward the block side
        public V3 Along;      // plane direction = edge direction (Z x Facing)
        public double MovedFt; // plan distance from the block point to Point
    }

    /// <summary>
    /// Vertical reference plane orientation from the nearest wall/column face or DWG wall line:
    /// the plane is parallel to that edge, the family faces away from it toward the side the
    /// block is on, and (Snap to face) the device sits on the face, at the block point projected
    /// onto it - or (Through block point) the plane passes through the block point.
    /// </summary>
    public static class EdgeSnap
    {
        public const double MmPerFoot = 304.8;
        public const double DefaultSearchMm = 600;
        public const double SnapReviewMm = 200;
        public const double MinSegmentMm = 100;
        public const string DefaultLayers = "*WALL*, *COL*, *A-WALL*, *S-COLS*";

        public static string NoEdgeReason(double radiusMm) => $"No wall/column within {radiusMm:0} mm - used block rotation";
        public static string SnapMovedReason(double mm) => $"Moved {mm:0} mm to snap to the wall/column face";

        /// <summary>The plan for a block at <paramref name="block"/> and the edge a-b.
        /// <paramref name="blockFacing"/> decides the side only when the block lies on the edge.</summary>
        public static EdgePlan Plan(V3 block, V3 a, V3 b, V3 blockFacing, PlanePosition position, double z)
        {
            var d = new V3(b.X - a.X, b.Y - a.Y, 0);
            if (d.Length < 1e-9) d = new V3(-blockFacing.Y, blockFacing.X, 0);
            d = d.Normalize();
            var n = new V3(-d.Y, d.X, 0);                      // Z x d
            var rel = new V3(block.X - a.X, block.Y - a.Y, 0);
            double side = n.Dot(rel);
            if (Math.Abs(side) * MmPerFoot < 0.5) side = n.Dot(blockFacing);
            var facing = side >= 0 ? n : n * -1;
            V3 onPlan = position == PlanePosition.SnapToFace
                ? new V3(a.X, a.Y, 0) + d * d.Dot(rel)           // block point projected onto the face line
                : new V3(block.X, block.Y, 0);
            var point = new V3(onPlan.X, onPlan.Y, z);
            return new EdgePlan
            {
                Point = point,
                Facing = facing,
                Along = new V3(-facing.Y, facing.X, 0),
                MovedFt = new V3(block.X - onPlan.X, block.Y - onPlan.Y, 0).Length,
            };
        }

        public static bool NeedsSnapReview(EdgePlan p) => p.MovedFt * MmPerFoot > SnapReviewMm;
    }

    /// <summary>DWG layer filter: comma/semicolon separated wildcards (* and ?), case-insensitive.
    /// Hatch layers never match (hatch lines are not wall edges).</summary>
    public static class LayerFilter
    {
        public static List<Regex> Parse(string patterns) =>
            (patterns ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim()).Where(p => p.Length > 0)
                .Select(p => new Regex("^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                                       RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .ToList();

        public static bool IsHatch(string layer) => (layer ?? "").IndexOf("HATCH", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool Matches(string layer, IList<Regex> patterns, bool allLayers)
        {
            if (IsHatch(layer)) return false;
            if (allLayers) return true;
            var name = layer ?? "";
            return patterns.Any(r => r.IsMatch(name));
        }

        public static bool Matches(string layer, string patterns, bool allLayers) => Matches(layer, Parse(patterns), allLayers);
    }
}
