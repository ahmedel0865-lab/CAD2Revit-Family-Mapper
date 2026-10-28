using System;
using System.Collections.Generic;
using System.Linq;

namespace DMCRemapper.Core
{
    /// <summary>Weights of the DWG wall-line score (settings.ini, LineScore... keys).</summary>
    public class ScoreWeights
    {
        /// <summary>Line touches the block outline, or passes within 20 mm of the insertion point.</summary>
        public double Touch = 100;
        /// <summary>Closer to the outline = more, up to this (at 0 mm) down to 0 (at the search radius).</summary>
        public double Distance = 50;
        /// <summary>Longer line = more, up to this at 3000 mm and longer.</summary>
        public double Length = 30;
        /// <summary>Per other block that touches the same line (same line within 5 mm and 0.5 deg).</summary>
        public double Continuity = 10;
        /// <summary>At most this many other blocks count for continuity.</summary>
        public int ContinuityMax = 5;
        /// <summary>Top two scores closer than this (percent of the best): "Ambiguous wall line".</summary>
        public double AmbiguousPercent = 10;
    }

    /// <summary>One straight DWG segment, in plan (feet).</summary>
    public class LineSeg
    {
        public double X1, Y1, X2, Y2;
        public string Layer = "";
        /// <summary>The line or polyline it belongs to (a polyline's segments share one id).</summary>
        public int Path;
        /// <summary>Colinear group: segments on the same line (within 5 mm and 0.5 deg) share one id.</summary>
        public int Group;

        public double LengthFt => Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1));
    }

    /// <summary>A block in plan: its outline (the symbol's bounding box, as 4 corners in model
    /// coordinates, so it follows the block rotation) and the point the family is placed at.</summary>
    public class BlockOutline
    {
        public double[] Xs = new double[4], Ys = new double[4];
        public double Px, Py;

        public double Cx => (Xs[0] + Xs[1] + Xs[2] + Xs[3]) / 4;
        public double Cy => (Ys[0] + Ys[1] + Ys[2] + Ys[3]) / 4;
        /// <summary>Distance from the centre to the farthest corner or the point.</summary>
        public double Radius
        {
            get
            {
                double cx = Cx, cy = Cy, r = Math.Sqrt((Px - cx) * (Px - cx) + (Py - cy) * (Py - cy));
                for (int i = 0; i < 4; i++) r = Math.Max(r, Math.Sqrt((Xs[i] - cx) * (Xs[i] - cx) + (Ys[i] - cy) * (Ys[i] - cy)));
                return r;
            }
        }

        /// <summary>Outline from 4 corners (any order around the box) and the placement point.</summary>
        public static BlockOutline FromCorners(IList<V3> corners, double px, double py)
        {
            var o = new BlockOutline { Px = px, Py = py };
            for (int i = 0; i < 4; i++)
            {
                var c = corners.Count == 4 ? corners[i] : new V3(px, py, 0);
                o.Xs[i] = c.X;
                o.Ys[i] = c.Y;
            }
            return o;
        }

        /// <summary>A block without line work: the outline is just the point.</summary>
        public static BlockOutline AtPoint(double px, double py) => FromCorners(new V3[0], px, py);
    }

    /// <summary>The line chosen for a block, with its score.</summary>
    public class ScoredLine
    {
        public LineSeg Seg;
        public double Score;
        public bool Touching;
        /// <summary>The next best line (on another line) scored within <see cref="ScoreWeights.AmbiguousPercent"/>.</summary>
        public bool Ambiguous;
        public double SecondScore;
        public double DistanceFt;     // from the block outline
        public int Continuity;        // other blocks touching the same line
    }

    /// <summary>
    /// Vertical planes with no Revit wall or column nearby: the best DWG line near each block, on
    /// any layer. Candidates are straight segments longer than 300 mm within the search radius of the
    /// block OUTLINE, except wires/tails (a line or polyline that starts or ends at the outline).
    /// Each gets a score: touching the outline or the insertion point (bonus), distance, length (capped
    /// at 3000 mm) and continuity (other blocks touching the same line). The highest score wins; if the
    /// next best line is within 10 %, the block goes to Needs Review as "Ambiguous wall line".
    /// The caller leaves out block geometry (device symbols), text, dimensions, hatches and arcs.
    /// Everything is indexed once; each block only looks at the segments in the cells around it.
    /// </summary>
    public class WallLineScorer
    {
        public const double MmPerFoot = 304.8;
        public const double DefaultRadiusMm = 300;
        public const double MinLengthMm = 300;
        public const double LengthCapMm = 3000;
        public const double TouchMm = 20;
        public const double SameLineMm = 5;
        public const double SameLineDeg = 0.5;

        public const string NoLineReason = "No wall line found";
        public const string AmbiguousReason = "Ambiguous wall line";

        readonly List<LineSeg> _segs;
        readonly EdgeIndex<int> _index;
        readonly Dictionary<int, (double x1, double y1, double x2, double y2, bool closed)> _paths =
            new Dictionary<int, (double, double, double, double, bool)>();
        readonly Dictionary<int, HashSet<int>> _touchers = new Dictionary<int, HashSet<int>>();
        readonly double _radiusFt;
        readonly ScoreWeights _w;
        static double Ft(double mm) => mm / MmPerFoot;

        public IReadOnlyList<LineSeg> Segments => _segs;
        public double RadiusFt => _radiusFt;

        public WallLineScorer(IEnumerable<LineSeg> segs, double radiusMm = DefaultRadiusMm, ScoreWeights weights = null)
        {
            _segs = segs.Where(s => s.LengthFt > 1e-6).ToList();
            _radiusFt = Ft(radiusMm > 0 ? radiusMm : DefaultRadiusMm);
            _w = weights ?? new ScoreWeights();
            _index = new EdgeIndex<int>(Math.Max(_radiusFt, Ft(500)));
            for (int i = 0; i < _segs.Count; i++)
            {
                var s = _segs[i];
                _index.Add(s.X1, s.Y1, s.X2, s.Y2, i);
            }
            // Path end points (first and last point of each line/polyline): wires and tails start or end there.
            foreach (var g in Enumerable.Range(0, _segs.Count).GroupBy(i => _segs[i].Path))
            {
                var first = _segs[g.First()];
                var last = _segs[g.Last()];
                bool closed = g.Count() > 1 && Math.Abs(first.X1 - last.X2) < 1e-6 && Math.Abs(first.Y1 - last.Y2) < 1e-6;
                _paths[g.Key] = (first.X1, first.Y1, last.X2, last.Y2, closed);
            }
            AssignGroups(_segs);
        }

        // ---- Same line (colinear groups) ----------------------------------------------------

        static double Theta(LineSeg s)
        {
            double t = Math.Atan2(s.Y2 - s.Y1, s.X2 - s.X1);
            if (t < 0) t += Math.PI;
            if (t >= Math.PI) t -= Math.PI;
            return t;
        }

        static double AngleDiffDeg(double t1, double t2)
        {
            double d = Math.Abs(t1 - t2) % Math.PI;
            return Math.Min(d, Math.PI - d) * 180 / Math.PI;
        }

        static double LineDistance(double px, double py, LineSeg s)
        {
            double dx = s.X2 - s.X1, dy = s.Y2 - s.Y1, l = Math.Sqrt(dx * dx + dy * dy);
            return l < 1e-12 ? Math.Sqrt((px - s.X1) * (px - s.X1) + (py - s.Y1) * (py - s.Y1)) : Math.Abs((px - s.X1) * dy - (py - s.Y1) * dx) / l;
        }

        /// <summary>True when both segments lie on the same line: within 0.5 deg and 5 mm.</summary>
        public static bool SameLine(LineSeg a, LineSeg b)
        {
            if (AngleDiffDeg(Theta(a), Theta(b)) > SameLineDeg) return false;
            double tol = Ft(SameLineMm);
            return LineDistance((b.X1 + b.X2) / 2, (b.Y1 + b.Y2) / 2, a) <= tol &&
                   LineDistance((a.X1 + a.X2) / 2, (a.Y1 + a.Y2) / 2, b) <= tol;
        }

        /// <summary>Union of colinear segments, binned by angle and offset (no pairwise scan).</summary>
        static void AssignGroups(List<LineSeg> segs)
        {
            const double binDeg = SameLineDeg;
            int nBins = (int)Math.Round(180 / binDeg);
            double binRad = binDeg * Math.PI / 180, rhoStep = Ft(SameLineMm);
            var parent = Enumerable.Range(0, segs.Count).ToArray();
            int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
            long Rho(LineSeg s, int bin)
            {
                double t = (bin + 0.5) * binRad, nx = -Math.Sin(t), ny = Math.Cos(t);
                return (long)Math.Floor(((s.X1 + s.X2) / 2 * nx + (s.Y1 + s.Y2) / 2 * ny) / rhoStep);
            }
            var bins = new Dictionary<(int, long), List<int>>();
            for (int i = 0; i < segs.Count; i++)
            {
                int bin = Math.Min(nBins - 1, (int)(Theta(segs[i]) / binRad));
                for (int db = -1; db <= 1; db++)
                {
                    int b = ((bin + db) % nBins + nBins) % nBins;
                    long r = Rho(segs[i], b);
                    for (long dr = -1; dr <= 1; dr++)
                        if (bins.TryGetValue((b, r + dr), out var list))
                            foreach (var j in list)
                                if (SameLine(segs[i], segs[j])) parent[Find(i)] = Find(j);
                }
                var key = (bin, Rho(segs[i], bin));
                if (!bins.TryGetValue(key, out var own)) bins[key] = own = new List<int>();
                own.Add(i);
            }
            for (int i = 0; i < segs.Count; i++) segs[i].Group = Find(i);
        }

        // ---- Geometry ------------------------------------------------------------------------

        static bool Inside(BlockOutline o, double x, double y)
        {
            bool inside = false;
            for (int i = 0, j = 3; i < 4; j = i++)
                if ((o.Ys[i] > y) != (o.Ys[j] > y) &&
                    x < (o.Xs[j] - o.Xs[i]) * (y - o.Ys[i]) / (o.Ys[j] - o.Ys[i]) + o.Xs[i])
                    inside = !inside;
            return inside;
        }

        static double Cross(double ax, double ay, double bx, double by, double cx, double cy) =>
            (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);

        static bool Intersects(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
        {
            double d1 = Cross(cx, cy, dx, dy, ax, ay), d2 = Cross(cx, cy, dx, dy, bx, by);
            double d3 = Cross(ax, ay, bx, by, cx, cy), d4 = Cross(ax, ay, bx, by, dx, dy);
            return ((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0));
        }

        /// <summary>Plan distance (feet) from a point to the block outline (0 inside it).</summary>
        public static double PointToOutline(BlockOutline o, double x, double y)
        {
            if (Inside(o, x, y)) return 0;
            double best = double.MaxValue;
            for (int i = 0, j = 3; i < 4; j = i++)
                best = Math.Min(best, EdgeIndex<int>.SegmentDistance(x, y, o.Xs[j], o.Ys[j], o.Xs[i], o.Ys[i]));
            return best;
        }

        /// <summary>Plan distance (feet) from a segment to the block outline (0 when it crosses or enters it).</summary>
        public static double SegmentToOutline(BlockOutline o, LineSeg s)
        {
            if (Inside(o, s.X1, s.Y1) || Inside(o, s.X2, s.Y2)) return 0;
            double best = double.MaxValue;
            for (int i = 0, j = 3; i < 4; j = i++)
            {
                if (Intersects(s.X1, s.Y1, s.X2, s.Y2, o.Xs[j], o.Ys[j], o.Xs[i], o.Ys[i])) return 0;
                best = Math.Min(best, EdgeIndex<int>.SegmentDistance(o.Xs[i], o.Ys[i], s.X1, s.Y1, s.X2, s.Y2));
                best = Math.Min(best, EdgeIndex<int>.SegmentDistance(s.X1, s.Y1, o.Xs[j], o.Ys[j], o.Xs[i], o.Ys[i]));
                best = Math.Min(best, EdgeIndex<int>.SegmentDistance(s.X2, s.Y2, o.Xs[j], o.Ys[j], o.Xs[i], o.Ys[i]));
            }
            return best;
        }

        /// <summary>A wire or tail of this block: its line/polyline starts or ends at the outline.
        /// Closed polylines (rooms, columns) have no ends.</summary>
        public bool IsTail(BlockOutline o, LineSeg s)
        {
            if (!_paths.TryGetValue(s.Path, out var p) || p.closed) return false;
            double tol = Ft(TouchMm);
            return PointToOutline(o, p.x1, p.y1) <= tol || PointToOutline(o, p.x2, p.y2) <= tol;
        }

        bool Touches(BlockOutline o, LineSeg s) =>
            SegmentToOutline(o, s) <= Ft(TouchMm) ||
            EdgeIndex<int>.SegmentDistance(o.Px, o.Py, s.X1, s.Y1, s.X2, s.Y2) <= Ft(TouchMm);

        // ---- Continuity and choice -----------------------------------------------------------

        /// <summary>Registers every block of the run, so a line touched by several blocks scores
        /// higher (continuity). Call once before <see cref="Choose"/>.</summary>
        public void AddBlocks(IList<BlockOutline> blocks)
        {
            double tol = Ft(TouchMm);
            for (int k = 0; k < blocks.Count; k++)
            {
                var o = blocks[k];
                if (o == null) continue;
                foreach (var h in _index.Within(o.Cx, o.Cy, o.Radius + tol))
                {
                    var s = _segs[h.Payload];
                    if (IsTail(o, s) || !Touches(o, s)) continue;
                    if (!_touchers.TryGetValue(s.Group, out var set)) _touchers[s.Group] = set = new HashSet<int>();
                    set.Add(k);
                }
            }
        }

        /// <summary>Score of one candidate for block <paramref name="o"/> (index <paramref name="self"/>
        /// in <see cref="AddBlocks"/>, or -1), or null if it is not a candidate.</summary>
        public ScoredLine Score(BlockOutline o, LineSeg s, int self = -1)
        {
            double len = s.LengthFt;
            if (len * MmPerFoot <= MinLengthMm) return null;
            double d = SegmentToOutline(o, s);
            if (d > _radiusFt || IsTail(o, s)) return null;
            bool touch = Touches(o, s);
            int others = 0;
            if (_touchers.TryGetValue(s.Group, out var set)) others = set.Count - (set.Contains(self) ? 1 : 0);
            double score = (touch ? _w.Touch : 0)
                         + _w.Distance * (1 - d / _radiusFt)
                         + _w.Length * Math.Min(len, Ft(LengthCapMm)) / Ft(LengthCapMm)
                         + _w.Continuity * Math.Min(others, Math.Max(0, _w.ContinuityMax));
            return new ScoredLine { Seg = s, Score = score, Touching = touch, DistanceFt = d, Continuity = others };
        }

        /// <summary>The highest-scoring line for the block, or null when there is no candidate.
        /// Ambiguous when the best line on another line scores within AmbiguousPercent.</summary>
        public ScoredLine Choose(BlockOutline o, int self = -1)
        {
            var scored = new List<ScoredLine>();
            foreach (var h in _index.Within(o.Cx, o.Cy, o.Radius + _radiusFt))
            {
                var c = Score(o, _segs[h.Payload], self);
                if (c != null) scored.Add(c);
            }
            if (scored.Count == 0) return null;
            var best = scored.OrderByDescending(c => c.Score).ThenBy(c => c.DistanceFt).First();
            var second = scored.Where(c => c.Seg.Group != best.Seg.Group).OrderByDescending(c => c.Score).FirstOrDefault();
            if (second != null)
            {
                best.SecondScore = second.Score;
                best.Ambiguous = best.Score - second.Score < best.Score * _w.AmbiguousPercent / 100.0;
            }
            return best;
        }

        /// <summary>The segment nearest to a picked point (Fix Host Line), within maxFt, or null.</summary>
        public LineSeg Nearest(double x, double y, double maxFt) =>
            _index.Nearest(x, y, maxFt) is EdgeHit<int> h ? _segs[h.Payload] : null;

        /// <summary>Preview text: "Touching line (layer: A-WALL)" or "Nearest line (score 74)".</summary>
        public static string SourceText(ScoredLine c) =>
            c.Touching ? $"Touching line (layer: {(string.IsNullOrEmpty(c.Seg.Layer) ? "0" : c.Seg.Layer)})"
                       : $"Nearest line (score {Math.Round(c.Score):0})";
    }
}
