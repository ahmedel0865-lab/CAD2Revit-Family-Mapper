using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DMCRemapper.Core;

namespace DMCRemapper.Revit
{
    /// <summary>What an indexed edge belongs to (for messages and the level-height check).</summary>
    class EdgeInfo
    {
        public EdgeSource Source;
        public string Label = "";
        public bool Linked;
        public double ZMin = double.MinValue, ZMax = double.MaxValue;

        /// <summary>Preview text: "Revit wall", "Revit column (linked)".</summary>
        public string SourceText => (Source == EdgeSource.Column ? "Revit column" : "Revit wall") + (Linked ? " (linked)" : "");
    }

    /// <summary>
    /// Edges for vertical reference planes (Host Type "Vertical plane"):
    /// 1. the plan lines of wall and column side faces, in this model and in Revit links, within the
    ///    wall/column search distance (600 mm by default) - the closest one the device sits along;
    /// 2. otherwise the best-scoring DWG line near the block outline, on any layer (Core.WallLineScorer).
    /// Both are collected ONCE per run into grid indexes; each block only looks at the few segments
    /// in the cells around it.
    /// </summary>
    class EdgeFinder
    {
        readonly Document _doc;
        readonly bool _searchLinks;
        readonly ImportInstance _dwg;
        readonly SlabOptions _opts;
        readonly ScoreWeights _weights;
        readonly Func<string, bool> _skipBlock;
        readonly double _radiusFt;
        EdgeIndex<EdgeInfo> _revit;
        WallLineScorer _lines;

        public int RevitEdges => _revit?.Count ?? 0;

        /// <param name="skipBlock">Raw DWG block names whose geometry is not wall line work (the device
        /// symbols being placed, electrical blocks).</param>
        public EdgeFinder(Document doc, bool searchLinks, ImportInstance dwg, SlabOptions opts, ScoreWeights weights = null,
                          Func<string, bool> skipBlock = null)
        {
            _doc = doc;
            _searchLinks = searchLinks;
            _dwg = dwg;
            _opts = opts ?? new SlabOptions();
            _weights = weights ?? new ScoreWeights();
            _skipBlock = skipBlock ?? (n => false);
            _radiusFt = Math.Max(_opts.EdgeSearchMm, 1) / EdgeSnap.MmPerFoot;
        }

        /// <summary>Step 1: the closest Revit wall/column face within the search distance that reaches
        /// the device height and that the device sits along, or null.</summary>
        public EdgeHit<EdgeInfo> FindRevit(double x, double y, double z)
        {
            if (_revit == null) BuildRevit();
            const double tol = 0.5;   // ft: walls/columns that reach the device height (with a margin)
            return EdgeSnap.Choose(_revit.Within(x, y, _radiusFt, e => z >= e.ZMin - tol && z <= e.ZMax + tol), new V3(x, y, 0));
        }

        /// <summary>Step 2: the DWG line scorer (built on first use). Register the run's blocks with
        /// AddBlocks for continuity before choosing.</summary>
        public WallLineScorer Lines
        {
            get
            {
                if (_lines == null)
                    _lines = new WallLineScorer(_dwg == null ? new List<LineSeg>() : DwgLines.Read(_doc, _dwg, _skipBlock),
                                                _opts.LineSearchMm, _weights);
                return _lines;
            }
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
                        var info = new EdgeInfo { Source = EdgeSource.Wall, Label = "wall " + w.Name + suffix, Linked = link != null, ZMin = z0, ZMax = z1 };
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
                        var info = new EdgeInfo { Source = EdgeSource.Column, Label = "column " + e.Name + suffix, Linked = link != null, ZMin = z0, ZMax = z1 };
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
    }
}
