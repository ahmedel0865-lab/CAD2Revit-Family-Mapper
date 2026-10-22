using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using SmartHostMEP.Core;

namespace SmartHostMEP.Revit
{
    /// <summary>Turns Revit geometry (DWG blocks, family view line work) into a 2D <see cref="BlockSymbol"/>.</summary>
    static class SymbolGeometry
    {
        /// <summary>Plan projection: X and Y.</summary>
        public static readonly Func<XYZ, double[]> Plan = p => new[] { p.X, p.Y };

        /// <summary>Projection onto a view's plane (its right and up directions).</summary>
        public static Func<XYZ, double[]> OnView(View view)
        {
            XYZ r = view.RightDirection, u = view.UpDirection;
            return p => new[] { p.DotProduct(r), p.DotProduct(u) };
        }

        /// <summary>Layers that never hold symbol line work: hatch, wipeout, text, attributes, dimensions.</summary>
        public static bool IsIgnoredLayer(string layer)
        {
            var l = (layer ?? "").ToUpperInvariant();
            if (l.Contains("HATCH") || l.Contains("WIPEOUT")) return true;
            // Whole words only, so e.g. "E-DIMMER" stays.
            foreach (var t in l.Split(new[] { '-', '_', ' ', '.', '$', '|' }, StringSplitOptions.RemoveEmptyEntries))
                if (t == "TEXT" || t == "TXT" || t.StartsWith("ATTR") || t == "DIM" || t == "DIMS" || t.StartsWith("DIMENSION")) return true;
            return false;
        }

        /// <summary>Skips geometry on ignored layers / line styles (names looked up once per style).</summary>
        public static Func<GeometryObject, bool> LayerFilter(Document doc)
        {
            var names = new Dictionary<ElementId, bool>();
            return g =>
            {
                var id = g.GraphicsStyleId;
                if (id == null || id == ElementId.InvalidElementId) return false;
                if (names.TryGetValue(id, out var skip)) return skip;
                string name = "";
                try { name = (doc.GetElement(id) as GraphicsStyle)?.GraphicsStyleCategory?.Name ?? ""; } catch (Exception) { }
                skip = IsIgnoredLayer(name) || name.StartsWith("<Invisible", StringComparison.OrdinalIgnoreCase);
                names[id] = skip;
                return skip;
            };
        }

        /// <summary>
        /// Adds the line work of <paramref name="geom"/> to <paramref name="symbol"/>: polylines, bound
        /// curves and nested instances; with <paramref name="circles"/> also full circles and ellipses
        /// (unbound curves); with <paramref name="solids"/> also the edges of solids (3D geometry).
        /// Returns false once the symbol's point budget is used up.
        /// </summary>
        public static bool Collect(GeometryElement geom, Transform tf, BlockSymbol symbol, Func<XYZ, double[]> project,
                                   Func<GeometryObject, bool> skip, bool circles, int depth = 0, bool solids = false)
        {
            if (geom == null || depth > 6) return true;
            foreach (var obj in geom)
            {
                if (obj == null) continue;
                bool more = true;
                if (obj is GeometryInstance nested)   // blocks inside the block, nested families
                    more = Collect(nested.GetSymbolGeometry(), tf.Multiply(nested.Transform), symbol, project, skip, circles, depth + 1, solids);
                else if (solids && obj is Solid solid)
                {
                    // 3D geometry seen in the view: the edges of its solids, flattened.
                    foreach (Edge edge in solid.Edges)
                    {
                        if (!(more = symbol.AddPath(Flatten(edge.Tessellate(), tf, project), SymbolKind.Line))) break;
                    }
                }
                else if (skip != null && skip(obj))
                    continue;
                else if (obj is PolyLine pl)
                    more = symbol.AddPath(Flatten(pl.GetCoordinates(), tf, project), SymbolKind.Polyline);
                else if (obj is Curve c)
                    more = CollectCurve(c, tf, symbol, project, circles);
                if (!more) return false;
            }
            return true;
        }

        /// <summary>One curve: bound curves always, full circles/ellipses with <paramref name="circles"/>.</summary>
        public static bool CollectCurve(Curve c, Transform tf, BlockSymbol symbol, Func<XYZ, double[]> project, bool circles)
        {
            if (c.IsBound) return symbol.AddPath(Flatten(c.Tessellate(), tf, project), KindOf(c));
            if (!circles) return true;
            var ring = Ring(c);
            return ring == null || symbol.AddPath(Flatten(ring, tf, project), SymbolKind.Circle);
        }

        static SymbolKind KindOf(Curve c)
        {
            if (c is Line) return SymbolKind.Line;
            if (c is Arc || c is Ellipse)
            {
                // A bound arc going (almost) all the way round is a circle.
                if (c.GetEndPoint(0).DistanceTo(c.GetEndPoint(1)) < 1e-6) return SymbolKind.Circle;
                return SymbolKind.Arc;
            }
            return SymbolKind.Arc;   // splines and other curves
        }

        /// <summary>Points around a full (unbound) circle or ellipse, or null.</summary>
        static IList<XYZ> Ring(Curve c)
        {
            XYZ centre, xd, yd;
            double rx, ry;
            if (c is Arc a) { centre = a.Center; xd = a.XDirection; yd = a.YDirection; rx = ry = a.Radius; }
            else if (c is Ellipse e) { centre = e.Center; xd = e.XDirection; yd = e.YDirection; rx = e.RadiusX; ry = e.RadiusY; }
            else return null;
            const int n = 48;
            var pts = new List<XYZ>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                double t = 2 * Math.PI * i / n;
                pts.Add(centre + xd * (rx * Math.Cos(t)) + yd * (ry * Math.Sin(t)));
            }
            return pts;
        }

        static List<double> Flatten(IList<XYZ> points, Transform tf, Func<XYZ, double[]> project)
        {
            var xy = new List<double>(points.Count * 2);
            foreach (var p in points)
            {
                var q = project(tf.OfPoint(p));
                xy.Add(q[0]);
                xy.Add(q[1]);
            }
            return xy;
        }
    }
}
