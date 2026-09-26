using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace CAD2Revit.Revit
{
    /// <summary>One block reference (INSERT) found in the DWG.</summary>
    public class BlockRef
    {
        public string Name;
        public Transform Transform;
        public int Depth;          // 1 = top-level block in the DWG
        public XYZ Point;          // insertion point, model internal coordinates (feet)
        public double Rotation;    // radians, in plan
        public bool Mirrored;
        public double ScaleX = 1, ScaleY = 1;
        /// <summary>The DWG block reference itself (for reading the symbol's line work).</summary>
        public GeometryInstance Instance;

        public bool IsAnonymous => string.IsNullOrEmpty(Name) || Name.StartsWith("*");
    }

    /// <summary>
    /// Reads block references from a DWG ImportInstance (linked or imported CAD).
    ///
    /// Coordinates: Revit converts DWG units to feet when the file is linked, and
    /// the top-level GeometryInstance transform is the link's placement in this
    /// model (position, rotation, and the shared-coordinates offset when placed
    /// "By Shared Coordinates"). Multiplying it with each block's own transform
    /// gives the insertion point directly in model coordinates - no manual unit or
    /// coordinate conversion is needed.
    ///
    /// The Revit API does not expose AutoCAD block ATTRIBUTES or dynamic-block
    /// properties, only the block name and geometry.
    /// </summary>
    public static class DwgReader
    {
        public static List<BlockRef> Read(Document doc, ImportInstance import, bool includeNested)
        {
            var opts = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false };
            if (import.ViewSpecific)
                opts.View = doc.GetElement(import.OwnerViewId) as View;   // "Current view only" links
            else
                opts.DetailLevel = ViewDetailLevel.Fine;

            var result = new List<BlockRef>();
            var geo = import.get_Geometry(opts);
            if (geo == null) return result;
            foreach (var obj in geo)
            {
                // Top-level instance = the whole DWG; its transform = link placement.
                if (obj is GeometryInstance gi)
                    Walk(doc, gi.GetSymbolGeometry(), gi.Transform, gi.Transform, 1, includeNested, result);
            }
            return result;
        }

        static void Walk(Document doc, GeometryElement geom, Transform parent, Transform link, int depth,
                         bool includeNested, List<BlockRef> result)
        {
            if (geom == null) return;
            foreach (var obj in geom)
            {
                if (!(obj is GeometryInstance gi)) continue;
                var tf = parent.Multiply(gi.Transform);
                var bx = tf.BasisX;
                double baseLen = link.BasisX.GetLength();
                if (baseLen < 1e-12) baseLen = 1;
                result.Add(new BlockRef
                {
                    Name = (SymbolName(doc, gi) ?? "").Trim(),
                    Transform = tf,
                    Depth = depth,
                    Point = tf.Origin,
                    Rotation = Math.Atan2(bx.Y, bx.X),
                    Mirrored = tf.HasReflection,
                    ScaleX = bx.GetLength() / baseLen,
                    ScaleY = tf.BasisY.GetLength() / baseLen,
                    Instance = gi,
                });
                if (includeNested)
                    Walk(doc, gi.GetSymbolGeometry(), tf, link, depth + 1, includeNested, result);
            }
        }

        static string SymbolName(Document doc, GeometryInstance gi)
        {
            try
            {
#if REVIT2023_OR_GREATER
                // GetSymbolGeometryId() exists from Revit 2023; GeometryInstance.Symbol was removed in 2024.
                return doc.GetElement(gi.GetSymbolGeometryId().SymbolId)?.Name ?? "";
#else
                return gi.Symbol?.Name ?? "";
#endif
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>Replace each block's Revit symbol name with its simplified name
        /// (see Core.BlockNames), so every instance of a block shares one name.</summary>
        public static void SimplifyNames(List<BlockRef> blocks)
        {
            var map = Core.BlockNames.Simplify(blocks.Select(b => b.Name));
            foreach (var b in blocks) b.Name = map[b.Name];
        }

        /// <summary>The 2D symbol (line work of the block definition) for every block name,
        /// read from the first instance of each name. Unscaled, unrotated, in feet.</summary>
        public static Dictionary<string, Core.BlockSymbol> ExtractSymbols(IEnumerable<BlockRef> blocks)
        {
            var result = new Dictionary<string, Core.BlockSymbol>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in blocks)
            {
                if (b.Instance == null || result.ContainsKey(b.Name)) continue;
                var symbol = new Core.BlockSymbol();
                try
                {
                    // Symbol geometry is in the block's own coordinates; the DWG link scale is
                    // removed so sizes read in real units.
                    double unit = 1.0 / Math.Max(1e-12, b.Transform.BasisX.GetLength() / Math.Max(1e-12, b.ScaleX));
                    CollectLines(b.Instance.GetSymbolGeometry(), Transform.Identity.ScaleBasis(unit), symbol, 0);
                }
                catch (Exception) { /* preview only: ignore unreadable geometry */ }
                result[b.Name] = symbol;
            }
            return result;
        }

        static bool CollectLines(GeometryElement geom, Transform tf, Core.BlockSymbol symbol, int depth)
        {
            if (geom == null || depth > 6) return true;
            foreach (var obj in geom)
            {
                bool more = true;
                switch (obj)
                {
                    case PolyLine pl:
                        more = symbol.AddPath(Flatten(pl.GetCoordinates(), tf));
                        break;
                    case Curve c when c.IsBound:
                        more = symbol.AddPath(Flatten(c.Tessellate(), tf));
                        break;
                    case GeometryInstance nested:   // blocks inside the block
                        more = CollectLines(nested.GetSymbolGeometry(), tf.Multiply(nested.Transform), symbol, depth + 1);
                        break;
                }
                if (!more) return false;
            }
            return true;
        }

        static List<double> Flatten(IList<XYZ> points, Transform tf)
        {
            var xy = new List<double>(points.Count * 2);
            foreach (var p in points)
            {
                var q = tf.OfPoint(p);
                xy.Add(q.X);
                xy.Add(q.Y);
            }
            return xy;
        }

        public static Dictionary<string, int> CountByName(IEnumerable<BlockRef> blocks)
        {
            var counts = new Dictionary<string, int>();
            foreach (var b in blocks)
                counts[b.Name] = (counts.TryGetValue(b.Name, out var n) ? n : 0) + 1;
            return counts;
        }
    }
}
