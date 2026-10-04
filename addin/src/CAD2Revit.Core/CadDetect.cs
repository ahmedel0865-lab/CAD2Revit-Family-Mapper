using System;
using System.Collections.Generic;
using System.Linq;

namespace CAD2Revit.Core
{
    /// <summary>What a detected CAD face belongs to.</summary>
    public enum CadEdgeKind { WallPair, RectColumn, PolygonColumn, CircleColumn }

    /// <summary>Editable detection ranges (mm). The fixed rules are constants.</summary>
    public class CadDetectOptions
    {
        public const double DefaultWallMinMm = 100, DefaultWallMaxMm = 600, DefaultColumnMinMm = 200, DefaultColumnMaxMm = 1500;
        public const double MinSegmentMm = 100;      // shorter lines are ignored
        public const double MinOverlapMm = 300;      // wall pair: lines overlap at least this much
        public const double ParallelDeg = 1;         // wall pair: angle difference below this
        public const double JoinMm = 5;              // closed shapes: end points within this
        public const double PreferColumnMm = 50;     // column wins over a wall face less than this closer
        public const double CellMm = 1000;           // spatial grid cell
        public const double DoorSweepMinDeg = 80, DoorSweepMaxDeg = 100, DoorRadiusMinMm = 600, DoorRadiusMaxMm = 1200;

        public double SearchMm = EdgeSnap.DefaultSearchMm;
        public double WallMinMm = DefaultWallMinMm, WallMaxMm = DefaultWallMaxMm;
        public double ColumnMinMm = DefaultColumnMinMm, ColumnMaxMm = DefaultColumnMaxMm;
    }

    /// <summary>The face chosen for a block: a wall face (one line of a parallel pair) or a column
    /// side (nearest side of a small closed shape, or the tangent of a circle). Plan, feet.</summary>
    public class CadFace
    {
        public CadEdgeKind Kind;
        public V3 A, B;             // the face line
        public V3 Outward;          // unit, away from the wall/column material
        public double DistanceFt;   // plan distance from the block point to the face
        public double ThicknessMm;  // wall pairs
        public double SizeAMm, SizeBMm;   // columns (diameter twice for circles)
        public string Label = "";
        public string Key = "";     // the same wall pair / column gives the same key
        /// <summary>The detected line work (both wall lines, or the column outline), for "Show detection".</summary>
        public List<(V3 a, V3 b)> Outline = new List<(V3, V3)>();
    }

    /// <summary>
    /// Finds walls and columns in plain DWG line work, from the shapes alone (no layer names):
    /// - wall = two straight lines less than 1 deg apart, 100-600 mm apart, overlapping 300 mm or more;
    /// - column = a small closed shape (closed polyline, or lines joined end to end within 5 mm, or
    ///   a circle) whose size is 200-1500 mm.
    /// All line work is put in a grid once; pairs and closed shapes are only worked out for the
    /// lines near the blocks that ask (and remembered), never for the whole drawing.
    /// Coordinates are plan feet (Revit internal units).
    /// </summary>
    public class CadDetector
    {
        const double MmPerFoot = 304.8;
        static double Ft(double mm) => mm / MmPerFoot;

        class Seg
        {
            public V3 A, B, Dir;
            public double Len;
            public int Shape = -1;          // part of a column found from loose lines
        }

        class Shape
        {
            public CadEdgeKind Kind;
            public List<V3> Pts;            // closed polygon, counter-clockwise (columns)
            public V3 Centre;
            public double Radius;           // circles
            public double SizeA, SizeB;     // mm
        }

        readonly CadDetectOptions _o;
        readonly List<Seg> _segs = new List<Seg>();
        readonly List<Shape> _shapes = new List<Shape>();
        readonly EdgeIndex<int> _segIndex, _shapeIndex;
        readonly Dictionary<int, int> _partner = new Dictionary<int, int>();
        readonly HashSet<int> _loopTried = new HashSet<int>();

        public int Segments => _segs.Count;
        public int Columns => _shapes.Count;
        public int IgnoredShort { get; private set; }
        public int IgnoredDoorArcs { get; private set; }

        public CadDetector(CadDetectOptions options = null)
        {
            _o = options ?? new CadDetectOptions();
            _segIndex = new EdgeIndex<int>(Ft(CadDetectOptions.CellMm));
            _shapeIndex = new EdgeIndex<int>(Ft(CadDetectOptions.CellMm));
        }

        // ---- input -------------------------------------------------------------------------

        public void AddLine(V3 a, V3 b)
        {
            a = new V3(a.X, a.Y, 0);
            b = new V3(b.X, b.Y, 0);
            double len = (b - a).Length;
            if (len < Ft(CadDetectOptions.MinSegmentMm)) { IgnoredShort++; return; }
            int i = _segs.Count;
            _segs.Add(new Seg { A = a, B = b, Len = len, Dir = (b - a) * (1 / len) });
            _segIndex.Add(a.X, a.Y, b.X, b.Y, i);
        }

        /// <summary>A polyline. A closed one (ends within 5 mm) of column size becomes a column;
        /// anything else is added as separate lines.</summary>
        public void AddPolyline(IList<V3> pts)
        {
            if (pts == null || pts.Count < 2) return;
            var p = Distinct(pts);
            bool closed = p.Count >= 4 && Flat(p[0] - p[p.Count - 1]).Length <= Ft(CadDetectOptions.JoinMm);
            if (closed)
            {
                p.RemoveAt(p.Count - 1);
                if (p.Count >= 3 && TryColumn(p, out var shape)) { AddShape(shape); return; }
                p.Add(p[0]);
            }
            for (int i = 0; i + 1 < p.Count; i++) AddLine(p[i], p[i + 1]);
        }

        /// <summary>A full circle: a column when its diameter is in the column range.</summary>
        public void AddCircle(V3 centre, double radiusFt)
        {
            double d = radiusFt * 2 * MmPerFoot;
            if (d < _o.ColumnMinMm || d > _o.ColumnMaxMm) return;
            AddShape(new Shape { Kind = CadEdgeKind.CircleColumn, Centre = Flat(centre), Radius = radiusFt, SizeA = d, SizeB = d });
        }

        /// <summary>An arc (tessellated points). Door swings are ignored; other arcs (curved walls)
        /// are added as chords of at least 100 mm.</summary>
        public void AddArc(IList<V3> pts, double radiusFt, double sweepRad)
        {
            if (IsDoorSwing(radiusFt, sweepRad)) { IgnoredDoorArcs++; return; }
            if (sweepRad >= 2 * Math.PI - 0.02 && pts != null && pts.Count > 2)
            {
                var c = new V3(pts.Average(q => q.X), pts.Average(q => q.Y), 0);
                AddCircle(c, radiusFt);
                return;
            }
            AddCurve(pts);
        }

        /// <summary>Any other curve (spline, ellipse): chords of at least 100 mm.</summary>
        public void AddCurve(IList<V3> pts)
        {
            if (pts == null || pts.Count < 2) return;
            double step = Ft(CadDetectOptions.MinSegmentMm);
            var last = pts[0];
            for (int i = 1; i < pts.Count; i++)
            {
                bool end = i == pts.Count - 1;
                if (Flat(pts[i] - last).Length >= step || (end && Flat(pts[i] - last).Length > 0))
                {
                    AddLine(last, pts[i]);
                    last = pts[i];
                }
            }
        }

        /// <summary>Door swing: an arc of 80-100 deg with a radius of 600-1200 mm.</summary>
        public static bool IsDoorSwing(double radiusFt, double sweepRad)
        {
            double deg = Math.Abs(sweepRad) * 180 / Math.PI, r = radiusFt * MmPerFoot;
            return deg >= CadDetectOptions.DoorSweepMinDeg && deg <= CadDetectOptions.DoorSweepMaxDeg &&
                   r >= CadDetectOptions.DoorRadiusMinMm && r <= CadDetectOptions.DoorRadiusMaxMm;
        }

        // ---- detection ---------------------------------------------------------------------

        /// <summary>The face for a block at (x, y): the closest wall face or column side within the
        /// search radius, on the block's side of the wall. A column side wins when it is less than
        /// 50 mm farther than the nearest wall face. Null when nothing is detected.</summary>
        public CadFace Find(double x, double y)
        {
            double r = Ft(Math.Max(_o.SearchMm, 1));
            var block = new V3(x, y, 0);
            var near = _segIndex.Within(x, y, r);

            // Columns drawn as separate lines: join the nearby lines into closed shapes first, so
            // their sides are not mistaken for wall pairs.
            foreach (var h in near) TryLoop(h.Payload);

            CadFace bestCol = null, bestWall = null;
            foreach (var id in _shapeIndex.Within(x, y, r).Select(h => h.Payload).Distinct())
            {
                var f = ColumnFace(id, block);
                if (f != null && f.DistanceFt <= r && (bestCol == null || f.DistanceFt < bestCol.DistanceFt)) bestCol = f;
            }
            foreach (var h in near)
            {
                var s = _segs[h.Payload];
                if (s.Shape >= 0) continue;
                int p = Partner(h.Payload);
                if (p < 0 || _segs[p].Shape >= 0) continue;
                var f = WallFace(h.Payload, p, block, h.DistanceFt);
                if (f != null && (bestWall == null || f.DistanceFt < bestWall.DistanceFt)) bestWall = f;
            }
            if (bestCol != null && (bestWall == null || bestCol.DistanceFt <= bestWall.DistanceFt + Ft(CadDetectOptions.PreferColumnMm)))
                return bestCol;
            return bestWall;
        }

        /// <summary>The other face of the wall whose face is segment i: the closest parallel line
        /// (&lt; 1 deg) 100-600 mm away that overlaps it by 300 mm or more; -1 if none. Cached.</summary>
        int Partner(int i)
        {
            if (_partner.TryGetValue(i, out var cached)) return cached;
            var s = _segs[i];
            var n = Normal(s.Dir);
            var mid = (s.A + s.B) * 0.5;
            double min = Ft(_o.WallMinMm) - 1e-6, max = Ft(_o.WallMaxMm) + 1e-6;
            double sinTol = Math.Sin(CadDetectOptions.ParallelDeg * Math.PI / 180);
            int best = -1;
            double bestD = double.MaxValue;
            foreach (var h in _segIndex.Within(mid.X, mid.Y, s.Len / 2 + max))
            {
                int j = h.Payload;
                if (j == i) continue;
                var o = _segs[j];
                if (Math.Abs(Cross(s.Dir, o.Dir)) >= sinTol) continue;
                TryLoop(j);                       // a column side is never a wall face
                if (o.Shape >= 0) continue;
                double d = Math.Abs(n.Dot((o.A + o.B) * 0.5 - s.A));
                if (d < min || d > max || d >= bestD) continue;
                double t1 = (o.A - s.A).Dot(s.Dir), t2 = (o.B - s.A).Dot(s.Dir);
                double overlap = Math.Min(s.Len, Math.Max(t1, t2)) - Math.Max(0, Math.Min(t1, t2));
                if (overlap < Ft(CadDetectOptions.MinOverlapMm)) continue;
                best = j;
                bestD = d;
            }
            _partner[i] = best;
            return best;
        }

        CadFace WallFace(int i, int p, V3 block, double distFt)
        {
            var s = _segs[i];
            var o = _segs[p];
            var n = Normal(s.Dir);
            double toOther = n.Dot((o.A + o.B) * 0.5 - s.A);
            var outward = toOther > 0 ? n * -1 : n;
            double thick = Math.Abs(toOther);
            // Do not jump across the wall: a block beyond the far face belongs to that face.
            double side = outward.Dot(block - s.A);
            if (side < -(thick + Ft(1))) return null;
            double mm = thick * MmPerFoot;
            return new CadFace
            {
                Kind = CadEdgeKind.WallPair, A = s.A, B = s.B, Outward = outward, DistanceFt = distFt,
                ThicknessMm = mm, Label = $"CAD wall pair (thk {mm:0} mm)",
                Key = "W" + Math.Min(i, p) + "_" + Math.Max(i, p),
                Outline = { (s.A, s.B), (o.A, o.B) },
            };
        }

        CadFace ColumnFace(int id, V3 block)
        {
            var sh = _shapes[id];
            var f = new CadFace { Kind = sh.Kind, SizeAMm = sh.SizeA, SizeBMm = sh.SizeB, Key = "C" + id, Label = ColumnLabel(sh) };
            if (sh.Kind == CadEdgeKind.CircleColumn)
            {
                var rel = block - sh.Centre;
                double len = rel.Length;
                var radial = len < 1e-9 ? new V3(1, 0, 0) : rel * (1 / len);
                var at = sh.Centre + radial * sh.Radius;
                var tangent = new V3(-radial.Y, radial.X, 0);
                f.A = at - tangent * (sh.Radius / 2);
                f.B = at + tangent * (sh.Radius / 2);
                f.Outward = radial;
                f.DistanceFt = Math.Abs(len - sh.Radius);
                const int n = 24;
                for (int k = 0; k < n; k++)
                {
                    double a0 = 2 * Math.PI * k / n, a1 = 2 * Math.PI * (k + 1) / n;
                    f.Outline.Add((sh.Centre + new V3(Math.Cos(a0), Math.Sin(a0), 0) * sh.Radius,
                                   sh.Centre + new V3(Math.Cos(a1), Math.Sin(a1), 0) * sh.Radius));
                }
                return f;
            }
            double best = double.MaxValue;
            for (int k = 0; k < sh.Pts.Count; k++)
            {
                var a = sh.Pts[k];
                var b = sh.Pts[(k + 1) % sh.Pts.Count];
                f.Outline.Add((a, b));
                double d = EdgeIndex<int>.SegmentDistance(block.X, block.Y, a.X, a.Y, b.X, b.Y);
                if (d >= best) continue;
                best = d;
                var dir = Flat(b - a).Normalize();
                f.A = a;
                f.B = b;
                f.Outward = new V3(dir.Y, -dir.X, 0);   // counter-clockwise polygon: right side is outside
            }
            f.DistanceFt = best;
            return f;
        }

        static string ColumnLabel(Shape sh) =>
            sh.Kind == CadEdgeKind.CircleColumn ? $"CAD circular column D={sh.SizeA:0}"
          : sh.Kind == CadEdgeKind.RectColumn ? $"CAD column {sh.SizeA:0}x{sh.SizeB:0}"
          : $"CAD column {sh.SizeA:0}x{sh.SizeB:0} (polygon)";

        /// <summary>Lines joined end to end (within 5 mm) into a closed shape of column size, starting
        /// from segment i. Only lines no longer than the largest column are followed (at most 8).</summary>
        void TryLoop(int i)
        {
            if (!_loopTried.Add(i)) return;
            var s = _segs[i];
            double maxLen = Ft(_o.ColumnMaxMm + CadDetectOptions.JoinMm);
            if (s.Shape >= 0 || s.Len > maxLen) return;
            double join = Ft(CadDetectOptions.JoinMm);
            var path = new List<int> { i };
            var pts = new List<V3> { s.A };
            bool Walk(V3 at)
            {
                if (path.Count >= 3 && Flat(at - s.A).Length <= join) return true;
                if (path.Count >= 8) return false;
                foreach (var h in _segIndex.Within(at.X, at.Y, join))
                {
                    int j = h.Payload;
                    if (path.Contains(j)) continue;
                    var o = _segs[j];
                    if (o.Shape >= 0 || o.Len > maxLen) continue;
                    V3 next;
                    if (Flat(o.A - at).Length <= join) next = o.B;
                    else if (Flat(o.B - at).Length <= join) next = o.A;
                    else continue;
                    path.Add(j);
                    pts.Add(at);
                    if (Walk(next)) return true;
                    path.RemoveAt(path.Count - 1);
                    pts.RemoveAt(pts.Count - 1);
                }
                return false;
            }
            if (!Walk(s.B)) return;
            if (!TryColumn(pts, out var shape)) return;
            int id = AddShape(shape);
            foreach (var j in path)
            {
                _segs[j].Shape = id;
                _loopTried.Add(j);
            }
        }

        /// <summary>A closed polygon is a column when its size (measured along its longest side) is
        /// within the column range on both axes. Four sides at right angles: rectangle.</summary>
        bool TryColumn(List<V3> pts, out Shape shape)
        {
            shape = null;
            var p = pts.Select(Flat).ToList();
            if (p.Count < 3) return false;
            // Counter-clockwise.
            double area = 0;
            for (int k = 0; k < p.Count; k++)
            {
                var a = p[k];
                var b = p[(k + 1) % p.Count];
                area += a.X * b.Y - b.X * a.Y;
            }
            if (Math.Abs(area) < 1e-9) return false;
            if (area < 0) p.Reverse();
            int longest = 0;
            for (int k = 1; k < p.Count; k++)
                if (Flat(p[(k + 1) % p.Count] - p[k]).Length > Flat(p[(longest + 1) % p.Count] - p[longest]).Length) longest = k;
            var u = Flat(p[(longest + 1) % p.Count] - p[longest]).Normalize();
            var v = new V3(-u.Y, u.X, 0);
            double uMin = p.Min(q => q.Dot(u)), uMax = p.Max(q => q.Dot(u));
            double vMin = p.Min(q => q.Dot(v)), vMax = p.Max(q => q.Dot(v));
            double sa = (uMax - uMin) * MmPerFoot, sb = (vMax - vMin) * MmPerFoot;
            if (sa < _o.ColumnMinMm - 1 || sa > _o.ColumnMaxMm + 1 || sb < _o.ColumnMinMm - 1 || sb > _o.ColumnMaxMm + 1) return false;
            bool rect = p.Count == 4 && Enumerable.Range(0, 4).All(k =>
                Math.Abs(Flat(p[(k + 1) % 4] - p[k]).Normalize().Dot(Flat(p[(k + 2) % 4] - p[(k + 1) % 4]).Normalize())) < 0.02);
            var centre = new V3(p.Average(q => q.X), p.Average(q => q.Y), 0);
            shape = new Shape { Kind = rect ? CadEdgeKind.RectColumn : CadEdgeKind.PolygonColumn, Pts = p, Centre = centre,
                                SizeA = Math.Max(sa, sb), SizeB = Math.Min(sa, sb) };
            return true;
        }

        int AddShape(Shape sh)
        {
            int id = _shapes.Count;
            _shapes.Add(sh);
            if (sh.Kind == CadEdgeKind.CircleColumn)
            {
                // Index the circle by its bounding square (candidates only; the distance is exact later).
                double r = sh.Radius;
                var c = sh.Centre;
                _shapeIndex.Add(c.X - r, c.Y - r, c.X + r, c.Y - r, id);
                _shapeIndex.Add(c.X + r, c.Y - r, c.X + r, c.Y + r, id);
                _shapeIndex.Add(c.X + r, c.Y + r, c.X - r, c.Y + r, id);
                _shapeIndex.Add(c.X - r, c.Y + r, c.X - r, c.Y - r, id);
            }
            else
                for (int k = 0; k < sh.Pts.Count; k++)
                {
                    var a = sh.Pts[k];
                    var b = sh.Pts[(k + 1) % sh.Pts.Count];
                    _shapeIndex.Add(a.X, a.Y, b.X, b.Y, id);
                }
            return id;
        }

        static V3 Flat(V3 p) => new V3(p.X, p.Y, 0);
        static V3 Normal(V3 d) => new V3(-d.Y, d.X, 0);
        static double Cross(V3 a, V3 b) => a.X * b.Y - a.Y * b.X;

        static List<V3> Distinct(IList<V3> pts)
        {
            var r = new List<V3>();
            foreach (var q in pts)
                if (r.Count == 0 || Flat(q - r[r.Count - 1]).Length > 1e-6) r.Add(Flat(q));
            return r;
        }
    }
}
