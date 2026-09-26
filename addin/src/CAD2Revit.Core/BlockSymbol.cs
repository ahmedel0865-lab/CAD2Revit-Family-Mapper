using System;
using System.Collections.Generic;

namespace CAD2Revit.Core
{
    /// <summary>
    /// The 2D line work of a CAD block definition (lines, arcs, polylines...), as flat
    /// polylines in the block's own coordinates. Used for the symbol preview in the
    /// mapping window. Plain doubles, so it outlives the Revit geometry it came from.
    /// </summary>
    public class BlockSymbol
    {
        /// <summary>Stop collecting after this many points (huge blocks stay responsive).</summary>
        public const int MaxPoints = 6000;

        /// <summary>Each path is x0, y0, x1, y1, ... (at least two points).</summary>
        public List<double[]> Paths { get; } = new List<double[]>();
        public int PointCount { get; private set; }
        public bool Truncated { get; private set; }
        public bool IsEmpty => Paths.Count == 0;

        /// <summary>Adds one polyline; returns false once the point budget is used up.</summary>
        public bool AddPath(IList<double> xy)
        {
            if (xy == null || xy.Count < 4) return true;
            if (PointCount + xy.Count / 2 > MaxPoints)
            {
                Truncated = true;
                return false;
            }
            var copy = new double[xy.Count - xy.Count % 2];
            for (int i = 0; i < copy.Length; i++) copy[i] = xy[i];
            Paths.Add(copy);
            PointCount += copy.Length / 2;
            return true;
        }

        /// <summary>minX, minY, maxX, maxY of all points (null if empty).</summary>
        public double[] Bounds()
        {
            if (IsEmpty) return null;
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var p in Paths)
                for (int i = 0; i + 1 < p.Length; i += 2)
                {
                    x0 = Math.Min(x0, p[i]); x1 = Math.Max(x1, p[i]);
                    y0 = Math.Min(y0, p[i + 1]); y1 = Math.Max(y1, p[i + 1]);
                }
            return new[] { x0, y0, x1, y1 };
        }

        /// <summary>Size text such as "600 x 600 mm" (block geometry is in feet).</summary>
        public string SizeText()
        {
            var b = Bounds();
            if (b == null) return "";
            return $"{Math.Round((b[2] - b[0]) * 304.8):0} x {Math.Round((b[3] - b[1]) * 304.8):0} mm";
        }

        /// <summary>The paths scaled into a box of the given size (y pointing down, as on
        /// screen), centred, with a margin. Degenerate (zero-size) symbols are centred.</summary>
        public List<double[]> FitTo(double width, double height, double margin)
        {
            var result = new List<double[]>();
            var b = Bounds();
            if (b == null) return result;
            double w = b[2] - b[0], h = b[3] - b[1];
            double availW = Math.Max(1e-9, width - 2 * margin), availH = Math.Max(1e-9, height - 2 * margin);
            double s = Math.Min(w > 1e-12 ? availW / w : double.MaxValue, h > 1e-12 ? availH / h : double.MaxValue);
            if (double.IsInfinity(s) || s == double.MaxValue) s = 1;
            double ox = (width - w * s) / 2, oy = (height - h * s) / 2;
            foreach (var p in Paths)
            {
                var q = new double[p.Length];
                for (int i = 0; i + 1 < p.Length; i += 2)
                {
                    q[i] = ox + (p[i] - b[0]) * s;
                    q[i + 1] = oy + (b[3] - p[i + 1]) * s;   // flip Y: CAD up = screen up
                }
                result.Add(q);
            }
            return result;
        }
    }
}
