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
        public Curve Curve;                 // location line, in the wall's own document
        List<(Reference reference, Face face)> _sides;
        List<(Reference reference, Face face, double offset)> _faces;

        static V3 V(XYZ p) => new V3(p.X, p.Y, p.Z);

        /// <summary>Nearest point of the location line to a point (wall document coordinates, plan).
        /// Lines and arcs exactly; other curves (ellipses, splines) through Curve.Project.</summary>
        public WallStation Station(XYZ local)
        {
            if (Curve is Line line)
                return WallPlacement.NearestOnLine(V(line.GetEndPoint(0)), V(line.GetEndPoint(1)), V(local));
            if (Curve is Arc arc && arc.IsBound)
            {
                var c = arc.Center;
                var p0 = arc.GetEndPoint(0);
                double start = Math.Atan2(p0.Y - c.Y, p0.X - c.X);
                double sweep = arc.Length / arc.Radius * (arc.Normal.Z >= 0 ? 1 : -1);
                return WallPlacement.NearestOnArc(V(c), arc.Radius, start, sweep, V(local));
            }
            var flat = new XYZ(local.X, local.Y, Curve.GetEndPoint(0).Z);
            var ir = Curve.Project(flat);
            var pt = ir.XYZPoint;
            var d = Curve.ComputeDerivatives(ir.Parameter, false).BasisX;
            var t = new V3(d.X, d.Y, 0).Normalize();
            double over = 0;
            if (pt.IsAlmostEqualTo(Curve.GetEndPoint(0)) || pt.IsAlmostEqualTo(Curve.GetEndPoint(1)))
                over = Math.Abs(t.Dot(new V3(flat.X - pt.X, flat.Y - pt.Y, 0)));
            return new WallStation { Point = new V3(pt.X, pt.Y, 0), Tangent = t, Overshoot = over };
        }

        /// <summary>Side faces with their plan offset from the location line, measured along
        /// Left(tangent) (the same axis WallPlacement uses). Offsets are found by projecting points
        /// of the location line (at mid height, several positions so openings do not matter) onto
        /// each face; if that fails, +/- half the wall width on the exterior/interior side.</summary>
        public List<(Reference reference, Face face, double offset)> Faces
        {
            get
            {
                if (_faces != null) return _faces;
                _faces = new List<(Reference, Face, double)>();
                var bb = Wall.get_BoundingBox(null);
                double zMid = bb != null ? (bb.Min.Z + bb.Max.Z) / 2 : Curve.GetEndPoint(0).Z;
                double half = Wall.Width / 2;
                foreach (var side in new[] { ShellLayerType.Interior, ShellLayerType.Exterior })
                    foreach (var r in HostObjectUtils.GetSideFaces(Wall, side))
                    {
                        if (!(Wall.GetGeometryObjectFromReference(r) is Face f)) continue;
                        double? offset = null;
                        foreach (var u in new[] { 0.5, 0.25, 0.75, 0.1, 0.9, 0.4, 0.6 })
                        {
                            try
                            {
                                var loc = Curve.Evaluate(u, true);
                                var probe = new XYZ(loc.X, loc.Y, zMid);
                                var ir = f.Project(probe);
                                if (ir == null) continue;
                                var st = Station(probe);
                                offset = WallPlacement.Offset(st, V(ir.XYZPoint));
                                break;
                            }
                            catch (Exception) { }
                        }
                        if (!offset.HasValue)
                        {
                            // Exterior face is on Wall.Orientation's side of the location line.
                            var st = Station(Curve.Evaluate(0.5, true));
                            var ori = Wall.Orientation;
                            double sign = WallPlacement.Left(st.Tangent).Dot(new V3(ori.X, ori.Y, 0)) >= 0 ? 1 : -1;
                            offset = (side == ShellLayerType.Exterior ? sign : -sign) * half;
                        }
                        _faces.Add((r, f, offset.Value));
                    }
                return _faces;
            }
        }

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
        const string Floors = "floors", Ceilings = "ceilings", AnyFace = "face", SlabsAndBeams = "slabsbeams";
        const double MaxWallHalfWidthFt = 2.0;   // wall location line to face, generous

        readonly Dictionary<string, FaceIndex<FaceSource>> _faceIndexes = new Dictionary<string, FaceIndex<FaceSource>>();
        SegmentIndex<WallSource> _walls;
        readonly List<double[]> _unindexedWalls = new List<double[]>();
        readonly Dictionary<(int, long, long, long, long), HostHit> _cache = new Dictionary<(int, long, long, long, long), HostHit>();

        /// <summary>Numbers for the timings table.</summary>
        public int IndexedFaces => _faceIndexes.Values.Sum(i => i.Count);
        public int IndexedWalls => _walls?.Count ?? 0;

        static string GroupFor(HostMode mode) =>
            mode == HostMode.SlabAbove ? SlabsAndBeams
            : mode == HostMode.SlabBelow ? Floors
            : mode == HostMode.Ceiling ? Ceilings
            : mode == HostMode.Face ? AnyFace : null;

        static readonly Dictionary<string, BuiltInCategory[]> GroupCategories = new Dictionary<string, BuiltInCategory[]>
        {
            [Floors] = new[] { BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs },   // sloped slabs are often roofs
            [Ceilings] = new[] { BuiltInCategory.OST_Ceilings },
            // Slab (above): nearest bottom face of a slab or a beam (a drop beam under the slab wins).
            [SlabsAndBeams] = new[] { BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs, BuiltInCategory.OST_StructuralFraming },
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
                                var src = new WallSource { Wall = w, Link = link, Tf = tf, LinkName = name, Curve = lc.Curve };
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
        /// slab at the level, searching down from just above it. Floors and Roofs count, plus beams
        /// (Structural Framing) for Slab (above): the nearest bottom face straight above the point wins,
        /// so a beam is only used where the point is under its bottom face. In this model and in Revit links. Faces pointing the wrong
        /// way (e.g. the top of this level's own slab) are skipped. Null = no slab within maxDistFt
        /// (no slab, or the point is under an opening).
        /// </summary>
        public HostHit FindSlab(bool above, double x, double y, double levelZ, double maxDistFt) =>
            above ? FindUnderside(HostMode.SlabAbove, x, y, levelZ, maxDistFt) :
            Cached(101, x, y, levelZ, maxDistFt, () =>
            {
                var idx = Faces(Floors);
                double start = levelZ + (above ? 0.01 : SlabSearch.BelowStartMm / SlabSearch.MmPerFoot);
                var f = idx.Nearest(x, y, start, above, maxDistFt, face => above ? face.Nz < -0.5 : face.Nz > 0.5, out var z);
                if (f != null) return FromFace(f, x, y, z, start);
                return idx.IsUnindexedAt(x, y) ? FindSlabRay(above, x, y, levelZ, maxDistFt) : null;
            });

        /// <summary>
        /// Shared search for Slab (above) and Ceiling: the nearest bottom face (normal pointing down)
        /// straight above (x, y), from just above the level up to maxDistFt, among the categories of
        /// the host type (Ceiling: ceilings; Slab (above): floors, roofs and beams), in this model and
        /// in Revit links. From the face index (built once); a ray only where faces could not be indexed.
        /// </summary>
        public HostHit FindUnderside(HostMode mode, double x, double y, double levelZ, double maxDistFt) =>
            Cached(300 + (int)mode, x, y, levelZ, maxDistFt, () =>
            {
                var idx = Faces(GroupFor(mode));
                double start = levelZ + 0.01;
                var f = idx.Nearest(x, y, start, true, maxDistFt, face => face.Nz < -0.5, out var z);
                if (f != null) return FromFace(f, x, y, z, start);
                return idx.IsUnindexedAt(x, y) ? FindUndersideRay(mode, x, y, levelZ, maxDistFt) : null;
            });

        /// <summary>
        /// Wall hosting (see Core.WallPlacement): the nearest wall to (x, y) - by plan distance to the
        /// wall body, within maxDistFt - among walls in this model and links that exist at height
        /// <paramref name="searchZ"/>. Its side face on the CAD point's side is used (inside the wall
        /// thickness: the side the block symbol is drawn on, from <paramref name="symbolCentre"/>),
        /// and the point is projected perpendicularly onto that face at height <paramref name="z"/>.
        /// The reference direction is the wall direction there (tangent, for curved walls); the CAD
        /// rotation is not used. Walls whose location line could not be indexed fall back to rays.
        /// </summary>
        public HostHit FindWall(double x, double y, double searchZ, double z, double maxDistFt, double startAngle,
                                Func<XYZ> symbolCentre = null)
        {
            if (_walls == null)
                using (_timer.Time(Phases.HostIndex)) BuildWallIndex();
            var cad = new XYZ(x, y, z);
            HostHit best = null;
            double bestDist = double.MaxValue, bestMoved = double.MaxValue;
            XYZ centre = null;
            bool centreRead = false;
            foreach (var src in _walls.Near(x, y, searchZ, maxDistFt + MaxWallHalfWidthFt))
            {
                try
                {
                    var inv = src.Tf.Inverse;
                    var local = inv.OfPoint(cad);
                    var faces = src.Faces;
                    if (faces.Count == 0) continue;
                    double low = faces.Min(f => f.offset), high = faces.Max(f => f.offset);
                    var st = src.Station(local);
                    var plan = WallPlacement.Plan(st, low, high, V(local), null, local.Z);
                    if (plan.InsideWall && symbolCentre != null)
                    {
                        if (!centreRead) { centreRead = true; try { centre = symbolCentre(); } catch (Exception) { } }
                        if (centre != null) plan = WallPlacement.Plan(st, low, high, V(local), V(inv.OfPoint(centre)), local.Z);
                    }
                    if (plan.DistanceFt > maxDistFt) continue;
                    if (plan.DistanceFt > bestDist + 1e-6 || (Math.Abs(plan.DistanceFt - bestDist) <= 1e-6 && plan.MovedFt >= bestMoved)) continue;

                    // The face on that side: the one the projected point lies on (walls can have
                    // several faces per side), else the first.
                    double faceOffset = plan.Side > 0 ? high : low;
                    var target = new XYZ(plan.Point.X, plan.Point.Y, plan.Point.Z);
                    (Reference reference, Face face, double offset) pick = default;
                    double pickDist = double.MaxValue;
                    foreach (var f in faces.Where(f => Math.Abs(f.offset - faceOffset) < 5 / 304.8))
                    {
                        double d = 1e9;
                        try { var ir = f.face.Project(target); if (ir != null) d = ir.Distance; } catch (Exception) { }
                        if (pick.reference == null || d < pickDist) { pick = f; pickDist = d; }
                    }
                    if (pick.reference == null) continue;

                    var n = src.Tf.OfVector(new XYZ(plan.Normal.X, plan.Normal.Y, 0)).Normalize();
                    best = new HostHit
                    {
                        Reference = src.Link == null ? pick.reference : pick.reference.CreateLinkReference(src.Link),
                        Point = src.Tf.OfPoint(target),
                        FaceNormal = n, RoomNormal = n,
                        RefDir = src.Tf.OfVector(new XYZ(plan.ReferenceDirection.X, plan.ReferenceDirection.Y, 0)).Normalize(),
                        MovedFt = plan.MovedFt, InsideWall = plan.InsideWall,
                        Element = src.Wall, IsLinked = src.Link != null, LinkName = src.LinkName,
                        Distance = plan.DistanceFt, FromIndex = true,
                    };
                    bestDist = plan.DistanceFt;
                    bestMoved = plan.MovedFt;
                }
                catch (Exception) { }
            }
            if (best != null) return best;
            if (!NearUnindexedWall(x, y, maxDistFt)) return null;
            return WallRay(x, y, z, maxDistFt, startAngle);
        }

        static V3 V(XYZ p) => new V3(p.X, p.Y, p.Z);

        HostHit WallRay(double x, double y, double z, double maxDistFt, double startAngle)
        {
            var ray = FindWallRay(x, y, z, maxDistFt, startAngle);
            if (ray != null)
            {
                ray.FaceNormal = ray.RoomNormal;
                ray.RefDir = XYZ.BasisZ.CrossProduct(ray.RoomNormal).Normalize();
                ray.MovedFt = new XYZ(ray.Point.X - x, ray.Point.Y - y, 0).GetLength();
            }
            return ray;
        }

        /// <summary>Ray-cast version of a lookup, used when a face from the index cannot host.</summary>
        public HostHit Recast(HostMode mode, double x, double y, double levelZ, double z, double maxDistFt, double startAngle) =>
            mode == HostMode.Wall ? WallRay(x, y, z, maxDistFt, startAngle)
            : mode == HostMode.SlabAbove || mode == HostMode.Ceiling ? FindUndersideRay(mode, x, y, levelZ, maxDistFt)
            : mode == HostMode.SlabBelow ? FindSlabRay(false, x, y, levelZ, maxDistFt)
            : FindAboveRay(mode, x, y, levelZ, maxDistFt);
    }
}
