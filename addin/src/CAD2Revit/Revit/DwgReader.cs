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

        public static Dictionary<string, int> CountByName(IEnumerable<BlockRef> blocks)
        {
            var counts = new Dictionary<string, int>();
            foreach (var b in blocks)
                counts[b.Name] = (counts.TryGetValue(b.Name, out var n) ? n : 0) + 1;
            return counts;
        }
    }
}
