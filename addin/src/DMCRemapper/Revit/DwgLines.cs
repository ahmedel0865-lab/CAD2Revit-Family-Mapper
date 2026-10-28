using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using DMCRemapper.Core;

namespace DMCRemapper.Revit
{
    /// <summary>
    /// The straight line work of a DWG, in plan (model coordinates, feet), read in ONE geometry pass:
    /// lines and the segments of polylines, on every layer. Left out: arcs and curves (door swings),
    /// lines inside the blocks <paramref name="skipBlock"/> names (device symbols), dimension blocks
    /// (*D...), and hatch and dimension layers. Text is not part of the DWG geometry Revit exposes.
    /// </summary>
    static class DwgLines
    {
        const double MinFt = 10 / 304.8;   // drop slivers under 10 mm

        public static List<LineSeg> Read(Document doc, ImportInstance dwg, Func<string, bool> skipBlock = null)
        {
            var result = new List<LineSeg>();
            if (dwg == null) return result;
            skipBlock = skipBlock ?? (n => false);
            var opts = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false };
            if (dwg.ViewSpecific) opts.View = doc.GetElement(dwg.OwnerViewId) as View;
            else opts.DetailLevel = ViewDetailLevel.Coarse;
            var layers = new Dictionary<ElementId, string>();
            string Layer(GeometryObject g)
            {
                var id = g.GraphicsStyleId;
                if (id == null || id == ElementId.InvalidElementId) return "";
                if (layers.TryGetValue(id, out var name)) return name;
                name = "";
                try { name = (doc.GetElement(id) as GraphicsStyle)?.GraphicsStyleCategory?.Name ?? ""; } catch (Exception) { }
                layers[id] = name;
                return name;
            }
            int path = 0;
            void Add(XYZ a, XYZ b, string layer, int p)
            {
                if (new XYZ(b.X - a.X, b.Y - a.Y, 0).GetLength() < MinFt) return;
                result.Add(new LineSeg { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Layer = layer, Path = p });
            }
            void Walk(GeometryElement geo, Transform tf, int depth)
            {
                if (geo == null || depth > 6) return;
                foreach (var obj in geo)
                {
                    if (obj is GeometryInstance gi)
                    {
                        // depth 0: the DWG itself. Deeper: blocks, skipped when they are device symbols
                        // or dimensions; other blocks (walls drawn as blocks, xrefs) are read.
                        if (depth > 0)
                        {
                            var name = DwgReader.SymbolName(doc, gi) ?? "";
                            if (name.StartsWith("*D", StringComparison.OrdinalIgnoreCase) || skipBlock(name.Trim())) continue;
                        }
                        Walk(gi.GetSymbolGeometry(), tf.Multiply(gi.Transform), depth + 1);
                        continue;
                    }
                    if (!(obj is Line) && !(obj is PolyLine)) continue;   // arcs, splines: not wall lines
                    var layer = Layer(obj);
                    if (IsAnnotationLayer(layer)) continue;
                    if (obj is Line line)
                        Add(tf.OfPoint(line.GetEndPoint(0)), tf.OfPoint(line.GetEndPoint(1)), layer, path++);
                    else if (obj is PolyLine pl)
                    {
                        var pts = pl.GetCoordinates();
                        int p = path++;
                        for (int i = 0; i + 1 < pts.Count; i++) Add(tf.OfPoint(pts[i]), tf.OfPoint(pts[i + 1]), layer, p);
                    }
                }
            }
            try { Walk(dwg.get_Geometry(opts), Transform.Identity, 0); } catch (Exception) { }
            return result;
        }

        /// <summary>Hatch and dimension layers never hold wall lines.</summary>
        public static bool IsAnnotationLayer(string layer) =>
            (layer ?? "").IndexOf("HATCH", StringComparison.OrdinalIgnoreCase) >= 0 ||
            (layer ?? "").IndexOf("DIM", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
