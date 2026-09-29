using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using CAD2Revit.Core;

namespace CAD2Revit.Revit
{
    /// <summary>A planar face of a host element (this model or a link), for the face index.</summary>
    class FaceSource
    {
        public Element Element;
        public PlanarFace Face;
        public RevitLinkInstance Link;      // null = this model
        public string LinkName = "";
        Reference _ref;

        /// <summary>Reference usable by NewFamilyInstance (a link reference for linked faces).</summary>
        public Reference Ref => _ref ?? (_ref = Link == null ? Face.Reference : Face.Reference.CreateLinkReference(Link));
    }

    /// <summary>A wall (this model or a link) for the wall index.</summary>
    class WallSource
    {
        public Wall Wall;
        public RevitLinkInstance Link;
        public Transform Tf = Transform.Identity;
        public string LinkName = "";
        List<(Reference reference, Face face)> _sides;

        /// <summary>Interior and exterior side faces (in the wall's own document).</summary>
        public List<(Reference reference, Face face)> Sides
        {
            get
            {
                if (_sides != null) return _sides;
                _sides = new List<(Reference, Face)>();
                foreach (var side in new[] { ShellLayerType.Interior, ShellLayerType.Exterior })
                    foreach (var r in HostObjectUtils.GetSideFaces(Wall, side))
                        if (Wall.GetGeometryObjectFromReference(r) is Face f) _sides.Add((r, f));
                return _sides;
            }
        }
    }

    public partial class HostFinder
    {
        const string Floors = "floors", Ceilings = "ceilings", AnyFace = "face";
        const double MaxWallHalfWidthFt = 2.0;   // wall location line to face, generous

        readonly Dictionary<string, FaceIndex<FaceSource>> _faceIndexes = new Dictionary<string, FaceIndex<FaceSource>>();
        SegmentIndex<WallSource> _walls;
        readonly List<double[]> _unindexedWalls = new List<double[]>();
        readonly Dictionary<(int, long, long, long, long), HostHit> _cache = new Dictionary<(int, long, long, long, long), HostHit>();

        /// <summary>Numbers for the timings table.</summary>
        public int IndexedFaces => _faceIndexes.Values.Sum(i => i.Count);
        public int IndexedWalls => _walls?.Count ?? 0;

        static string GroupFor(HostMode mode) =>
            mode == HostMode.SlabAbove || mode == HostMode.SlabBelow ? Floors
            : mode == HostMode.Ceiling ? Ceilings
            : mode == HostMode.Face ? AnyFace : null;

        static readonly Dictionary<string, BuiltInCategory[]> GroupCategories = new Dictionary<string, BuiltInCategory[]>
        {
            [Floors] = new[] { BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs },   // sloped slabs are often roofs
            [Ceilings] = new[] { BuiltInCategory.OST_Ceilings },
            [AnyFace] = new[] { BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Floors,
                                BuiltInCategory.OST_Roofs, BuiltInCategory.OST_StructuralFraming },
        };

        /// <summary>Builds the indexes needed for these host types (once per run).</summary>
        public void Prepare(IEnumerable<HostMode> modes)
        {
            using (_timer.Time(Phases.HostIndex))
            {
                foreach (var mode in modes.Distinct())
                {
                    if (mode == HostMode.Wall && _walls == null) BuildWallIndex();
                    var group = GroupFor(mode);
                    if (group != null && !_faceIndexes.ContainsKey(group)) _faceIndexes[group] = BuildFaceIndex(GroupCategories[group]);
                }
            }
        }

        FaceIndex<FaceSource> Faces(string group)
        {
            if (!_faceIndexes.TryGetValue(group, out var idx))
                using (_timer.Time(Phases.HostIndex))
                    _faceIndexes[group] = idx = BuildFaceIndex(GroupCategories[group]);
            return idx;
        }

        IEnumerable<(Document doc, RevitLinkInstance link, Transform tf, string name)> Sources()
        {
            yield return (_doc, null, Transform.Identity, "");
            if (!_searchLinks) yield break;
            foreach (var link in new FilteredElementCollector(_doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                Document linkDoc = null;
                try { linkDoc = link.GetLinkDocument(); } catch (Exception) { }
                if (linkDoc == null) continue;   // unloaded link
                yield return (linkDoc, link, link.GetTotalTransform(), LinkName(link.Id));
            }
        }

        static readonly Options GeoOptions = new Options
        {
            ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false,
        };

        FaceIndex<FaceSource> BuildFaceIndex(BuiltInCategory[] cats)
        {
            var idx = new FaceIndex<FaceSource>();
            ElementFilter filter = cats.Length == 1 ? (ElementFilter)new ElementCategoryFilter(cats[0])
                                                    : new ElementMulticategoryFilter(cats);
            foreach (var (doc, link, tf, name) in Sources())
                foreach (var e in new FilteredElementCollector(doc).WherePasses(filter).WhereElementIsNotElementType())
                    IndexElement(idx, e, link, tf, name);
            return idx;
        }

        static IEnumerable<Solid> Solids(GeometryElement geo, int depth = 0)
        {
            if (geo == null || depth > 4) yield break;
            foreach (var obj in geo)
            {
                if (obj is Solid s && s.Faces.Size > 0) yield return s;
                else if (obj is GeometryInstance gi)
                    foreach (var inner in Solids(gi.GetInstanceGeometry(), depth + 1)) yield return inner;
            }
        }

        static void IndexElement(FaceIndex<FaceSource> idx, Element e, RevitLinkInstance link, Transform tf, string linkName)
        {
            bool complete = true;
            try
            {
                foreach (var solid in Solids(e.get_Geometry(GeoOptions)))
                    foreach (Face face in solid.Faces)
                    {
                        if (face is PlanarFace pf)
                        {
                            var n = tf.OfVector(pf.FaceNormal);
                            if (Math.Abs(n.Z) < 0.5) continue;          // walls of the slab edge etc.
                            if (pf.Reference == null) { complete = false; continue; }
                            var loops = new List<double[]>();
                            foreach (var loop in pf.GetEdgesAsCurveLoops())
                            {
                                var xy = new List<double>();
                                foreach (var c in loop)
                                {
                                    var pts = c.Tessellate();
                                    for (int i = 0; i < pts.Count - 1; i++)
                                    {
                                        var q = tf.OfPoint(pts[i]);
                                        xy.Add(q.X);
                                        xy.Add(q.Y);
                                    }
                                }
                                loops.Add(xy.ToArray());
                            }
                            var o = tf.OfPoint(pf.Origin);
                            idx.Add(loops, n.X, n.Y, n.Z, o.X, o.Y, o.Z,
                                    new FaceSource { Element = e, Face = pf, Link = link, LinkName = linkName });
                        }
                        else
                        {
                            // Curved face: only matters if it is roughly horizontal (curved roof, ...).
                            var bb = face.GetBoundingBox();
                            var uv = new UV((bb.Min.U + bb.Max.U) / 2, (bb.Min.V + bb.Max.V) / 2);
                            if (Math.Abs(tf.OfVector(face.ComputeNormal(uv)).Z) > 0.3) complete = false;
                        }
                    }
            }
            catch (Exception)
            {
                complete = false;
            }
            if (!complete) AddUnindexed(e, tf, (a, b, c, d) => idx.AddUnindexed(a, b, c, d));
        }

        static void AddUnindexed(Element e, Transform tf, Action<double, double, double, double> add)
        {
            BoundingBoxXYZ bb;
            try { bb = e.get_BoundingBox(null); } catch (Exception) { return; }
            if (bb == null) return;
            var t = bb.Transform != null ? tf.Multiply(bb.Transform) : tf;
            var pts = new[]
            {
                t.OfPoint(bb.Min), t.OfPoint(bb.Max),
                t.OfPoint(new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z)), t.OfPoint(new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z)),
            };
            add(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));
        }

        void BuildWallIndex()
        {
            _walls = new SegmentIndex<WallSource>();
            foreach (var (doc, link, tf, name) in Sources())
                foreach (var e in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType())
                {
                    bool ok = false;
                    try
                    {
                        if (e is Wall w && w.CurtainGrid == null && w.Location is LocationCurve lc)
                        {
                            var bb = w.get_BoundingBox(null);
                            if (bb != null)
                            {
                                double z0 = tf.OfPoint(bb.Min).Z, z1 = tf.OfPoint(bb.Max).Z;
                                var src = new WallSource { Wall = w, Link = link, Tf = tf, LinkName = name };
                                var pts = lc.Curve.Tessellate();
                                for (int i = 0; i + 1 < pts.Count; i++)
                                {
                                    XYZ a = tf.OfPoint(pts[i]), b = tf.OfPoint(pts[i + 1]);
                                    _walls.Add(a.X, a.Y, b.X, b.Y, Math.Min(z0, z1), Math.Max(z0, z1), src);
                                }
                                ok = true;
                            }
                        }
                    }
                    catch (Exception) { }
                    if (!ok) AddUnindexed(e, tf, (a, b, c, d) => _unindexedWalls.Add(new[] { a, b, c, d }));
                }
        }

        bool NearUnindexedWall(double x, double y, double margin) =>
            _unindexedWalls.Any(b => x >= b[0] - margin && x <= b[2] + margin && y >= b[1] - margin && y <= b[3] + margin);

        static long K(double v) => (long)Math.Round(v * 304.8);   // mm

        HostHit Cached(int kind, double x, double y, double z, double dist, Func<HostHit> find)
        {
            var key = (kind, K(x), K(y), K(z), K(dist));
            if (_cache.TryGetValue(key, out var hit)) return hit;
            hit = find();
            _cache[key] = hit;
            return hit;
        }

        HostHit FromFace(IndexedFace<FaceSource> f, double x, double y, double z, double startZ)
        {
            var src = f.Payload;
            var n = new XYZ(f.Nx, f.Ny, f.Nz);
            return new HostHit
            {
                Reference = src.Ref, Point = new XYZ(x, y, z), FaceNormal = n, RoomNormal = n,
                Element = src.Element, IsLinked = src.Link != null, LinkName = src.LinkName,
                Distance = Math.Abs(z - startZ), FromIndex = true,
            };
        }

        /// <summary>Nearest ceiling (Ceiling) or ceiling/slab/roof/beam underside (Face) straight
        /// above (x, y), from just above the level.</summary>
        public HostHit FindAbove(HostMode mode, double x, double y, double levelZ, double maxDistFt) =>
            Cached((int)mode, x, y, levelZ, maxDistFt, () =>
            {
                var idx = Faces(GroupFor(mode) ?? AnyFace);
                double start = levelZ + 0.01;
                var f = idx.Nearest(x, y, start, true, maxDistFt, face => face.Nz < -0.5, out var z);
                if (f != null) return FromFace(f, x, y, z, start);
                return idx.IsUnindexedAt(x, y) ? FindAboveRay(mode, x, y, levelZ, maxDistFt) : null;
            });

        /// <summary>
        /// Slab (above): the underside of the first floor slab straight above (x, y), searching
        /// up from the level - i.e. the slab of the level above. Slab (below): the top face of the
        /// slab at the level, searching down from just above it. Only Floors and Roofs count (beams,
        /// ceilings, ducts are ignored), in this model and in Revit links. Faces pointing the wrong
        /// way (e.g. the top of this level's own slab) are skipped. Null = no slab within maxDistFt
        /// (no slab, or the point is under an opening).
        /// </summary>
        public HostHit FindSlab(bool above, double x, double y, double levelZ, double maxDistFt, double slopedMaxDistFt = 0) =>
            Cached(above ? 100 : 101, x, y, levelZ, maxDistFt, () =>
            {
                var idx = Faces(Floors);
                double start = levelZ + (above ? 0.01 : SlabSearch.BelowStartMm / SlabSearch.MmPerFoot);
                var f = SlabSearch.Nearest(idx, x, y, start, above, maxDistFt, slopedMaxDistFt, out var z);
                if (f != null) return FromFace(f, x, y, z, start);
                return idx.IsUnindexedAt(x, y) ? FindSlabRay(above, x, y, levelZ, maxDistFt, slopedMaxDistFt) : null;
            });

        /// <summary>Nearest wall side face to (x, y) at height z, within maxDistFt: candidate walls
        /// from the wall index (by location line), then the exact point on their side faces.</summary>
        public HostHit FindWall(double x, double y, double z, double maxDistFt, double startAngle) =>
            Cached(200, x, y, z, maxDistFt, () =>
            {
                if (_walls == null)
                    using (_timer.Time(Phases.HostIndex)) BuildWallIndex();
                var origin = new XYZ(x, y, z);
                HostHit best = null;
                foreach (var src in _walls.Near(x, y, z, maxDistFt + MaxWallHalfWidthFt))
                {
                    List<(Reference reference, Face face)> sides;
                    try { sides = src.Sides; } catch (Exception) { continue; }
                    var local = src.Tf.Inverse.OfPoint(origin);
                    foreach (var (reference, face) in sides)
                    {
                        IntersectionResult ir;
                        try { ir = face.Project(local); } catch (Exception) { continue; }
                        if (ir == null || ir.Distance > maxDistFt || (best != null && ir.Distance >= best.Distance)) continue;
                        var pt = src.Tf.OfPoint(ir.XYZPoint);
                        var n = src.Tf.OfVector(face.ComputeNormal(ir.UVPoint));
                        n = new XYZ(n.X, n.Y, 0);
                        if (n.GetLength() < 1e-6) continue;
                        n = n.Normalize();
                        var toFace = new XYZ(pt.X - x, pt.Y - y, 0);
                        best = new HostHit
                        {
                            Reference = src.Link == null ? reference : reference.CreateLinkReference(src.Link),
                            Point = pt, FaceNormal = n,
                            // Room side = back towards the CAD point.
                            RoomNormal = toFace.GetLength() > 1e-6 && n.DotProduct(toFace) > 0 ? n.Negate() : n,
                            Element = src.Wall, IsLinked = src.Link != null, LinkName = src.LinkName,
                            Distance = ir.Distance, FromIndex = true,
                        };
                    }
                }
                if (best != null) return best;
                return NearUnindexedWall(x, y, maxDistFt) ? FindWallRay(x, y, z, maxDistFt, startAngle) : null;
            });

        /// <summary>Ray-cast version of a lookup, used when a face from the index cannot host.</summary>
        public HostHit Recast(HostMode mode, double x, double y, double levelZ, double z, double maxDistFt, double startAngle) =>
            mode == HostMode.Wall ? FindWallRay(x, y, z, maxDistFt, startAngle)
            : mode == HostMode.SlabAbove || mode == HostMode.SlabBelow ? FindSlabRay(mode == HostMode.SlabAbove, x, y, levelZ, maxDistFt)
            : FindAboveRay(mode, x, y, levelZ, maxDistFt);
    }
}
