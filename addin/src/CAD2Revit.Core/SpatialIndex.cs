using System;
using System.Collections.Generic;
using System.Linq;

namespace CAD2Revit.Core
{
    /// <summary>
    /// Spatial hash of points (cell = tolerance) for the duplicate check. Built once per run
    /// from the existing instances; each query only looks at the 3x3 neighbouring cells of
    /// its group (family or type), instead of comparing against every element.
    /// </summary>
    public class PointGrid
    {
        readonly double _tol;
        readonly Dictionary<(long, long, long), List<double[]>> _cells = new Dictionary<(long, long, long), List<double[]>>();

        public PointGrid(double tolerance)
        {
            _tol = tolerance > 1e-9 ? tolerance : 1e-9;
        }

        public int Count { get; private set; }

        long Cell(double v) => (long)Math.Floor(v / _tol);

        public void Add(long group, double x, double y, double z)
        {
            var k = (group, Cell(x), Cell(y));
            if (!_cells.TryGetValue(k, out var list)) _cells[k] = list = new List<double[]>();
            list.Add(new[] { x, y, z });
            Count++;
        }

        /// <summary>A point of this group within the tolerance in plan, with zMin &lt;= z &lt; zMax.</summary>
        public bool Contains(long group, double x, double y, double zMin, double zMax)
        {
            long cx = Cell(x), cy = Cell(y);
            double tol2 = _tol * _tol;
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    if (_cells.TryGetValue((group, cx + dx, cy + dy), out var list))
                        foreach (var p in list)
                            if (p[2] >= zMin && p[2] < zMax &&
                                (p[0] - x) * (p[0] - x) + (p[1] - y) * (p[1] - y) <= tol2)
                                return true;
            return false;
        }
    }

    /// <summary>A planar face, projected to plan: boundary loops (outer + openings) and its plane.</summary>
    public sealed class IndexedFace<T>
    {
        public T Payload;
        public double Nx, Ny, Nz;          // unit normal (model coordinates)
        public double Ox, Oy, Oz;          // a point on the plane
        public double MinX, MinY, MaxX, MaxY;
        public List<double[]> Loops;       // x0, y0, x1, y1, ... (closed implicitly)

        /// <summary>Height of the plane at (x, y).</summary>
        public double ZAt(double x, double y) => Oz - (Nx * (x - Ox) + Ny * (y - Oy)) / Nz;

        /// <summary>Point-in-face test: bounding-box prefilter, then even-odd over all loops,
        /// so points inside an opening (inner loop) are outside the face.</summary>
        public bool Contains(double x, double y)
        {
            if (x < MinX || x > MaxX || y < MinY || y > MaxY) return false;
            bool inside = false;
            foreach (var loop in Loops)
            {
                int n = loop.Length / 2;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    double xi = loop[2 * i], yi = loop[2 * i + 1], xj = loop[2 * j], yj = loop[2 * j + 1];
                    if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                        inside = !inside;
                }
            }
            return inside;
        }
    }

    /// <summary>
    /// Plan index of (near-)horizontal planar faces, e.g. slab soffits and tops, ceiling
    /// undersides. Replaces one ray cast per block: a query finds the faces whose outline
    /// contains the point and takes the nearest one above/below a start height.
    /// Areas that could not be indexed (non-planar faces) are remembered, so the caller can
    /// fall back to ray casting there.
    /// </summary>
    public class FaceIndex<T>
    {
        const int MaxCellsPerFace = 20000;
        readonly double _cell;
        readonly List<IndexedFace<T>> _faces = new List<IndexedFace<T>>();
        readonly List<int> _big = new List<int>();
        readonly Dictionary<(long, long), List<int>> _grid = new Dictionary<(long, long), List<int>>();
        readonly List<double[]> _unindexed = new List<double[]>();

        public FaceIndex(double cellSize = 16.0)
        {
            _cell = cellSize > 1e-6 ? cellSize : 16.0;
        }

        public int Count => _faces.Count;
        public int UnindexedCount => _unindexed.Count;

        long C(double v) => (long)Math.Floor(v / _cell);

        /// <summary>Adds a face. Returns false for vertical faces or faces without loops.</summary>
        public bool Add(IList<double[]> loops, double nx, double ny, double nz, double ox, double oy, double oz, T payload)
        {
            if (Math.Abs(nz) < 1e-6 || loops == null || loops.Count == 0) return false;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            var kept = new List<double[]>();
            foreach (var l in loops)
            {
                if (l == null || l.Length < 6) continue;
                kept.Add(l);
                for (int i = 0; i + 1 < l.Length; i += 2)
                {
                    minX = Math.Min(minX, l[i]); maxX = Math.Max(maxX, l[i]);
                    minY = Math.Min(minY, l[i + 1]); maxY = Math.Max(maxY, l[i + 1]);
                }
            }
            if (kept.Count == 0) return false;
            var f = new IndexedFace<T>
            {
                Payload = payload, Nx = nx, Ny = ny, Nz = nz, Ox = ox, Oy = oy, Oz = oz,
                MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY, Loops = kept,
            };
            int id = _faces.Count;
            _faces.Add(f);
            long x0 = C(minX), x1 = C(maxX), y0 = C(minY), y1 = C(maxY);
            if ((x1 - x0 + 1) * (y1 - y0 + 1) > MaxCellsPerFace)
            {
                _big.Add(id);
                return true;
            }
            for (long cx = x0; cx <= x1; cx++)
                for (long cy = y0; cy <= y1; cy++)
                {
                    if (!_grid.TryGetValue((cx, cy), out var list)) _grid[(cx, cy)] = list = new List<int>();
                    list.Add(id);
                }
            return true;
        }

        /// <summary>Remember a plan area whose faces could not be indexed.</summary>
        public void AddUnindexed(double minX, double minY, double maxX, double maxY) =>
            _unindexed.Add(new[] { minX, minY, maxX, maxY });

        public bool IsUnindexedAt(double x, double y)
        {
            foreach (var b in _unindexed)
                if (x >= b[0] && x <= b[2] && y >= b[1] && y <= b[3]) return true;
            return false;
        }

        IEnumerable<int> Candidates(double x, double y)
        {
            if (_grid.TryGetValue((C(x), C(y)), out var list))
                foreach (var i in list) yield return i;
            foreach (var i in _big) yield return i;
        }

        /// <summary>
        /// Nearest accepted face along a vertical line through (x, y), starting at zStart and
        /// going up (or down), no further than maxDist. Returns null if there is none.
        /// </summary>
        public IndexedFace<T> Nearest(double x, double y, double zStart, bool up, double maxDist,
                                      Func<IndexedFace<T>, bool> accept, out double z)
        {
            IndexedFace<T> best = null;
            double bestDist = double.MaxValue;
            z = 0;
            foreach (var i in Candidates(x, y))
            {
                var f = _faces[i];
                if (accept != null && !accept(f)) continue;
                if (!f.Contains(x, y)) continue;
                double fz = f.ZAt(x, y);
                double d = up ? fz - zStart : zStart - fz;
                if (d < 0 || d > maxDist || d >= bestDist) continue;
                best = f;
                bestDist = d;
                z = fz;
            }
            return best;
        }
    }

    /// <summary>
    /// Plan index of line segments with a height range (walls by their location lines).
    /// Returns the payloads of segments within a distance of a point, nearest first.
    /// </summary>
    public class SegmentIndex<T>
    {
        readonly double _cell;
        readonly List<(double x1, double y1, double x2, double y2, double zMin, double zMax, T payload)> _segs =
            new List<(double, double, double, double, double, double, T)>();
        readonly Dictionary<(long, long), List<int>> _grid = new Dictionary<(long, long), List<int>>();

        public SegmentIndex(double cellSize = 8.0)
        {
            _cell = cellSize > 1e-6 ? cellSize : 8.0;
        }

        public int Count => _segs.Count;
        long C(double v) => (long)Math.Floor(v / _cell);

        public void Add(double x1, double y1, double x2, double y2, double zMin, double zMax, T payload)
        {
            int id = _segs.Count;
            _segs.Add((x1, y1, x2, y2, zMin, zMax, payload));
            long cx0 = C(Math.Min(x1, x2)), cx1 = C(Math.Max(x1, x2)), cy0 = C(Math.Min(y1, y2)), cy1 = C(Math.Max(y1, y2));
            for (long cx = cx0; cx <= cx1; cx++)
                for (long cy = cy0; cy <= cy1; cy++)
                {
                    if (!_grid.TryGetValue((cx, cy), out var list)) _grid[(cx, cy)] = list = new List<int>();
                    list.Add(id);
                }
        }

        public static double Distance(double px, double py, double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1, dy = y2 - y1, len2 = dx * dx + dy * dy;
            double t = len2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((px - x1) * dx + (py - y1) * dy) / len2));
            double qx = x1 + t * dx - px, qy = y1 + t * dy - py;
            return Math.Sqrt(qx * qx + qy * qy);
        }

        /// <summary>Distinct payloads of segments within maxDist of (x, y) whose height range
        /// contains z, nearest first.</summary>
        public List<T> Near(double x, double y, double z, double maxDist)
        {
            var best = new Dictionary<int, double>();
            long cx0 = C(x - maxDist), cx1 = C(x + maxDist), cy0 = C(y - maxDist), cy1 = C(y + maxDist);
            for (long cx = cx0; cx <= cx1; cx++)
                for (long cy = cy0; cy <= cy1; cy++)
                    if (_grid.TryGetValue((cx, cy), out var list))
                        foreach (var i in list)
                        {
                            if (best.ContainsKey(i)) continue;
                            var s = _segs[i];
                            if (z < s.zMin || z > s.zMax) continue;
                            double d = Distance(x, y, s.x1, s.y1, s.x2, s.y2);
                            if (d <= maxDist) best[i] = d;
                        }
            var result = new List<T>();
            var seen = new HashSet<T>();
            foreach (var kv in best.OrderBy(k => k.Value))
                if (seen.Add(_segs[kv.Key].payload)) result.Add(_segs[kv.Key].payload);
            return result;
        }
    }
}
