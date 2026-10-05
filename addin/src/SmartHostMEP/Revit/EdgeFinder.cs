using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using SmartHostMEP.Core;

namespace SmartHostMEP.Revit
{
    /// <summary>What an indexed edge belongs to (for messages and the level-height check).</summary>
    class EdgeInfo
    {
        public EdgeSource Source;
        public string Label = "";
        public double ZMin = double.MinValue, ZMax = double.MaxValue;
    }

    /// <summary>
    /// Edges that orient vertical reference planes (Host Type "Vertical plane"): the plan lines
    /// of wall side faces and column side faces, in this model and in Revit links, and - when no
    /// Revit wall/column is near - the line work of the DWG on the wall/column layers.
    /// Everything is collected ONCE per run into grid indexes (Core.EdgeIndex); each block then
    /// only looks at the few segments in the cells around it.
    /// </summary>
    class EdgeFinder
    {
        readonly Document _doc;
        readonly bool _searchLinks;
        readonly ImportInstance _dwg;
        readonly SlabOptions _opts;
        readonly double _radiusFt;
        EdgeIndex<EdgeInfo> _revit, _cad;

        public int RevitEdges => _revit?.Count ?? 0;
        public int DwgEdges => _cad?.Count ?? 0;

        public EdgeFinder(Document doc, bool searchLinks, ImportInstance dwg, SlabOptions opts)
        {
            _doc = doc;
            _searchLinks = searchLinks;
            _dwg = dwg;
            _opts = opts ?? new SlabOptions();
            _radiusFt = Math.Max(_opts.EdgeSearchMm, 1) / EdgeSnap.MmPerFoot;
        }

        /// <summary>The Revit wall/column face for a device at (x, y, z), else the DWG wall/column
        /// line, else null - chosen by <see cref="EdgeSnap.Choose"/>: within the radius, the device
        /// must sit along it, and faces parallel to <paramref name="wallDir"/> (symbol back line or
        /// block X axis) win over closer perpendicular ones. <paramref name="angleOff"/> = the chosen
        /// face's angle to <paramref name="wallDir"/>.</summary>
        public EdgeHit<EdgeInfo> Find(double x, double y, double z, V3 wallDir, out double angleOff)
        {
            if (_revit == null) BuildRevit();
            const double tol = 0.5;   // ft: walls/columns that reach the device height (with a margin)
            var block = new V3(x, y, 0);
            var hit = EdgeSnap.Choose(_revit.Within(x, y, _radiusFt, e => z >= e.ZMin - tol && z <= e.ZMax + tol), block, wallDir, out angleOff);
            if (hit != null) return hit;
            if (_cad == null) BuildDwg();
            return EdgeSnap.Choose(_cad.Within(x, y, _radiusFt), block, wallDir, out angleOff);
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

        /// <summary>DWG line work on the wall/column layers (or all layers), segments of 100 mm and
        /// more, no hatch layers. With "All layers" only the drawing's own line work is used (not
        /// lines inside blocks, which are the device symbols); with a layer list, blocks are
        /// searched too (walls are sometimes drawn as blocks or xrefs).</summary>
        void BuildDwg()
        {
            _cad = new EdgeIndex<EdgeInfo>(_radiusFt);
            if (_dwg == null) return;
            var patterns = LayerFilter.Parse(_opts.DwgLayers);
            bool all = _opts.AllDwgLayers;
            if (!all && patterns.Count == 0) return;
            var opts = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false };
            if (_dwg.ViewSpecific) opts.View = _doc.GetElement(_dwg.OwnerViewId) as View;
            else opts.DetailLevel = ViewDetailLevel.Coarse;
            var layerOk = new Dictionary<ElementId, (bool ok, string name)>();
            (bool ok, string name) Layer(GeometryObject g)
            {
                var id = g.GraphicsStyleId;
                if (id == null || id == ElementId.InvalidElementId) return (all, "");
                if (layerOk.TryGetValue(id, out var r)) return r;
                string name = "";
                try { name = (_doc.GetElement(id) as GraphicsStyle)?.GraphicsStyleCategory?.Name ?? ""; } catch (Exception) { }
                r = (LayerFilter.Matches(name, patterns, all), name);
                layerOk[id] = r;
                return r;
            }
            double minFt = EdgeSnap.MinSegmentMm / EdgeSnap.MmPerFoot;
            void AddSeg(XYZ a, XYZ b, string layer)
            {
                if (new XYZ(b.X - a.X, b.Y - a.Y, 0).GetLength() < minFt) return;
                _cad.Add(a.X, a.Y, b.X, b.Y, new EdgeInfo { Source = EdgeSource.Dwg, Label = "DWG line on layer " + layer });
            }
            void Walk(GeometryElement geo, Transform tf, int depth)
            {
                if (geo == null || depth > 6) return;
                foreach (var obj in geo)
                {
                    if (obj is GeometryInstance gi)
                    {
                        // depth 0: the DWG itself; deeper: blocks (skipped with "All layers").
                        if (depth == 0 || !all) Walk(gi.GetSymbolGeometry(), tf.Multiply(gi.Transform), depth + 1);
                        continue;
                    }
                    if (!(obj is Curve) && !(obj is PolyLine)) continue;
                    var (ok, layer) = Layer(obj);
                    if (!ok) continue;
                    if (obj is Line line) AddSeg(tf.OfPoint(line.GetEndPoint(0)), tf.OfPoint(line.GetEndPoint(1)), layer);
                    else if (obj is PolyLine pl)
                    {
                        var pts = pl.GetCoordinates();
                        for (int i = 0; i + 1 < pts.Count; i++) AddSeg(tf.OfPoint(pts[i]), tf.OfPoint(pts[i + 1]), layer);
                    }
                    else if (obj is Curve c && c.IsBound)
                    {
                        var pts = c.Tessellate();
                        for (int i = 0; i + 1 < pts.Count; i++) AddSeg(tf.OfPoint(pts[i]), tf.OfPoint(pts[i + 1]), layer);
                    }
                }
            }
            try { Walk(_dwg.get_Geometry(opts), Transform.Identity, 0); } catch (Exception) { }
        }
    }
}
