using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using CAD2Revit.Core;

namespace CAD2Revit.Revit
{
    /// <summary>What an indexed edge belongs to (for messages and the level-height check).</summary>
    class EdgeInfo
    {
        public EdgeSource Source;
        public string Label = "";
        public double ZMin = double.MinValue, ZMax = double.MaxValue;
    }

    /// <summary>
    /// Edges that orient vertical reference planes (Host Type "Vertical plane"):
    /// 1. the side faces of Revit walls and columns, in this model and in Revit links;
    /// 2. else walls and columns DETECTED in the DWG line work from their shapes (parallel line
    ///    pairs, small closed shapes, circles - see Core.CadDetector), not from layer names.
    /// The Revit elements and the DWG curves are each read ONCE per run into grid indexes; each
    /// block then only looks at the few lines in the cells around it.
    /// </summary>
    class EdgeFinder
    {
        readonly Document _doc;
        readonly bool _searchLinks;
        readonly ImportInstance _dwg;
        readonly SlabOptions _opts;
        readonly HashSet<string> _exclude;
        readonly double _radiusFt;
        EdgeIndex<EdgeInfo> _revit;
        CadDetector _cad;

        /// <param name="excludeBlocks">DWG block names (as Revit reports them) whose line work is
        /// ignored: the blocks being converted.</param>
        public EdgeFinder(Document doc, bool searchLinks, ImportInstance dwg, SlabOptions opts, IEnumerable<string> excludeBlocks)
        {
            _doc = doc;
            _searchLinks = searchLinks;
            _dwg = dwg;
            _opts = opts ?? new SlabOptions();
            _exclude = new HashSet<string>(excludeBlocks ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            _radiusFt = Math.Max(_opts.EdgeSearchMm, 1) / EdgeSnap.MmPerFoot;
        }

        /// <summary>DWG statistics for the log.</summary>
        public string DwgSummary => _cad == null ? "" :
            $"DWG detection: {_cad.Segments} lines, {_cad.Columns} columns, {_cad.IgnoredShort} short lines and {_cad.IgnoredDoorArcs} door swings ignored";

        /// <summary>Nearest Revit wall/column face within the radius that exists at height z (the
        /// family faces the block's side of it), else the face of a wall/column detected in the
        /// DWG, else null.</summary>
        public CadFace Find(double x, double y, double z, V3 blockFacing)
        {
            if (_revit == null) BuildRevit();
            const double tol = 0.5;   // ft: walls/columns that reach the device height (with a margin)
            var hit = _revit.Nearest(x, y, _radiusFt, e => z >= e.ZMin - tol && z <= e.ZMax + tol);
            if (hit != null)
            {
                var a = new V3(hit.X1, hit.Y1, 0);
                var b = new V3(hit.X2, hit.Y2, 0);
                var d = (b - a).Normalize();
                var n = new V3(-d.Y, d.X, 0);
                double side = n.Dot(new V3(x, y, 0) - a);
                if (Math.Abs(side) * EdgeSnap.MmPerFoot < 0.5) side = n.Dot(blockFacing);
                return new CadFace
                {
                    A = a, B = b, Outward = side >= 0 ? n : n * -1, DistanceFt = hit.DistanceFt, Label = hit.Payload.Label,
                    Key = $"R{hit.X1:0.###}_{hit.Y1:0.###}_{hit.X2:0.###}_{hit.Y2:0.###}", Outline = { (a, b) },
                };
            }
            if (_cad == null) BuildDwg();
            return _cad.Find(x, y);
        }

        IEnumerable<(Document doc, RevitLinkInstance link, Transform tf, string name)> Sources()
        {
            yield return (_doc, null, Transform.Identity, "");
            if (!_searchLinks) yield break;
            foreach (var link in new FilteredElementCollector(_doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                Document linkDoc = null;
                try { linkDoc = link.GetLinkDocument(); } catch (Exception) { }
                if (linkDoc == null) continue;
                string name = "";
                try { name = _doc.GetElement(link.GetTypeId())?.Name ?? ""; } catch (Exception) { }
                yield return (linkDoc, link, link.GetTotalTransform(), name);
            }
        }

        static (double, double) ZRange(Element e, Transform tf)
        {
            var bb = e.get_BoundingBox(null);
            if (bb == null) return (double.MinValue, double.MaxValue);
            double a = tf.OfPoint(bb.Min).Z, b = tf.OfPoint(bb.Max).Z;
            return (Math.Min(a, b), Math.Max(a, b));
        }

        void Add(EdgeIndex<EdgeInfo> idx, XYZ a, XYZ b, EdgeInfo info)
        {
            if (new XYZ(b.X - a.X, b.Y - a.Y, 0).GetLength() * EdgeSnap.MmPerFoot < 1) return;
            idx.Add(a.X, a.Y, b.X, b.Y, info);
        }

        void BuildRevit()
        {
            _revit = new EdgeIndex<EdgeInfo>(_radiusFt);
            foreach (var (doc, link, tf, linkName) in Sources())
            {
                string suffix = link != null ? $" (linked: {linkName})" : "";
                // Walls: both side faces, as plan lines (location line offset by each face's offset).
                foreach (var e in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType())
                {
                    try
                    {
                        if (!(e is Wall w) || !(w.Location is LocationCurve lc)) continue;
                        var (z0, z1) = ZRange(w, tf);
                        var info = new EdgeInfo { Source = EdgeSource.Wall, Label = "wall " + w.Name + suffix, ZMin = z0, ZMax = z1 };
                        var src = new WallSource { Wall = w, Curve = lc.Curve };
                        List<double> offsets;
                        try { offsets = src.Faces.Select(f => f.offset).Distinct().ToList(); }
                        catch (Exception) { offsets = new List<double>(); }
                        if (offsets.Count == 0) offsets.Add(0);   // curtain walls etc.: the location line
                        var pts = lc.Curve is Line ? new List<XYZ> { lc.Curve.GetEndPoint(0), lc.Curve.GetEndPoint(1) } : lc.Curve.Tessellate().ToList();
                        foreach (var off in offsets)
                            for (int i = 0; i + 1 < pts.Count; i++)
                            {
                                var st0 = src.Station(pts[i]);
                                var st1 = src.Station(pts[i + 1]);
                                var n0 = WallPlacement.Left(st0.Tangent);
                                var n1 = WallPlacement.Left(st1.Tangent);
                                var a = new XYZ(pts[i].X + n0.X * off, pts[i].Y + n0.Y * off, pts[i].Z);
                                var b = new XYZ(pts[i + 1].X + n1.X * off, pts[i + 1].Y + n1.Y * off, pts[i + 1].Z);
                                Add(_revit, tf.OfPoint(a), tf.OfPoint(b), info);
                            }
                    }
                    catch (Exception) { }
                }
                // Columns (architectural and structural): the horizontal edges of their vertical faces.
                var cols = new ElementMulticategoryFilter(new List<BuiltInCategory> { BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralColumns });
                var opts = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Coarse, IncludeNonVisibleObjects = false };
                foreach (var e in new FilteredElementCollector(doc).WherePasses(cols).WhereElementIsNotElementType())
                {
                    try
                    {
                        var (z0, z1) = ZRange(e, tf);
                        var info = new EdgeInfo { Source = EdgeSource.Column, Label = "column " + e.Name + suffix, ZMin = z0, ZMax = z1 };
                        foreach (var solid in Solids(e.get_Geometry(opts), 0))
                            foreach (Face f in solid.Faces)
                            {
                                if (!(f is PlanarFace pf) || Math.Abs(pf.FaceNormal.Z) > 0.2) continue;
                                // One plan line per vertical face: its lowest horizontal edge.
                                Line best = null;
                                foreach (CurveLoop loop in pf.GetEdgesAsCurveLoops())
                                    foreach (var c in loop)
                                        if (c is Line l && Math.Abs(l.GetEndPoint(0).Z - l.GetEndPoint(1).Z) < 1e-3 &&
                                            (best == null || l.GetEndPoint(0).Z < best.GetEndPoint(0).Z))
                                            best = l;
                                if (best != null) Add(_revit, tf.OfPoint(best.GetEndPoint(0)), tf.OfPoint(best.GetEndPoint(1)), info);
                            }
                    }
                    catch (Exception) { }
                }
            }
        }

        static IEnumerable<Solid> Solids(GeometryElement geo, int depth)
        {
            if (geo == null || depth > 4) yield break;
            foreach (var obj in geo)
            {
                if (obj is Solid s && s.Faces.Size > 0) yield return s;
                else if (obj is GeometryInstance gi)
                    foreach (var inner in Solids(gi.GetInstanceGeometry(), depth + 1)) yield return inner;
            }
        }

        /// <summary>Every curve of the DWG, in model coordinates, read once: lines, polylines,
        /// arcs and circles, including those inside blocks and xrefs (architectural backgrounds are
        /// often blocks) - except inside the blocks being converted, dimension blocks, and on
        /// hatch / dimension / text layers. Door swings and lines under 100 mm are dropped by the
        /// detector.</summary>
        void BuildDwg()
        {
            _cad = new CadDetector(_opts.Detect());
            if (_dwg == null) return;
            var opts = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false };
            if (_dwg.ViewSpecific) opts.View = _doc.GetElement(_dwg.OwnerViewId) as View;
            else opts.DetailLevel = ViewDetailLevel.Coarse;
            var junk = new Dictionary<ElementId, bool>();
            bool Junk(GeometryObject g)
            {
                var id = g.GraphicsStyleId;
                if (id == null || id == ElementId.InvalidElementId) return false;
                if (junk.TryGetValue(id, out var r)) return r;
                string name = "";
                try { name = (_doc.GetElement(id) as GraphicsStyle)?.GraphicsStyleCategory?.Name ?? ""; } catch (Exception) { }
                return junk[id] = LayerFilter.IsJunk(name);
            }
            V3 P(Transform tf, XYZ p) { var q = tf.OfPoint(p); return new V3(q.X, q.Y, 0); }
            List<V3> Pts(Transform tf, IList<XYZ> pts) => pts.Select(p => P(tf, p)).ToList();
            void Walk(GeometryElement geo, Transform tf, int depth)
            {
                if (geo == null || depth > 8) return;
                foreach (var obj in geo)
                {
                    try
                    {
                        if (obj is GeometryInstance gi)
                        {
                            // depth 0: the DWG itself. Deeper: blocks/xrefs, except the devices being
                            // converted and dimension blocks (anonymous *D...).
                            if (depth > 0)
                            {
                                var name = (DwgReader.SymbolName(_doc, gi) ?? "").Trim();
                                if (_exclude.Contains(name) || name.StartsWith("*D", StringComparison.OrdinalIgnoreCase)) continue;
                            }
                            Walk(gi.GetSymbolGeometry(), tf.Multiply(gi.Transform), depth + 1);
                            continue;
                        }
                        if (!(obj is Curve) && !(obj is PolyLine)) continue;   // solids/meshes: fills, text
                        if (Junk(obj)) continue;
                        switch (obj)
                        {
                            case Line line:
                                _cad.AddLine(P(tf, line.GetEndPoint(0)), P(tf, line.GetEndPoint(1)));
                                break;
                            case PolyLine pl:
                                _cad.AddPolyline(Pts(tf, pl.GetCoordinates()));
                                break;
                            case Arc arc:
                            {
                                double scale = tf.BasisX.GetLength();
                                double r = arc.Radius * scale;
                                if (!arc.IsBound) { _cad.AddCircle(P(tf, arc.Center), r); break; }
                                double sweep = arc.Length / Math.Max(arc.Radius, 1e-9);
                                _cad.AddArc(Pts(tf, arc.Tessellate()), r, sweep);
                                break;
                            }
                            case Curve c when c.IsBound:
                                _cad.AddCurve(Pts(tf, c.Tessellate()));
                                break;
                        }
                    }
                    catch (Exception) { /* unreadable geometry: skip it */ }
                }
            }
            try { Walk(_dwg.get_Geometry(opts), Transform.Identity, 0); } catch (Exception) { }
        }
    }
}
