using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using CAD2Revit.Core;

namespace CAD2Revit.Revit
{
    /// <summary>A face found for hosting.</summary>
    public class HostHit
    {
        public Reference Reference;   // usable by NewFamilyInstance (also for linked faces)
        public XYZ Point;             // hit point, model coordinates
        public XYZ FaceNormal;        // true face normal, model coordinates
        public XYZ RoomNormal;        // walls: horizontal, pointing back to the CAD point
        public Element Element;       // host element (in its own document)
        public bool IsLinked;         // host lives in a Revit link
        public string LinkName = "";  // e.g. "STR.rvt" when linked
        public double Distance;       // ray length, feet
        public bool FromIndex;        // found by the face/wall index (not a ray)

        /// <summary>e.g. "Floor: 250mm RC Slab - Third Floor (linked: STR.rvt)".</summary>
        public string Describe()
        {
            string cat = null, type = null, level = null;
            try { cat = Element?.Category?.Name; } catch (Exception) { }
            try { type = Element?.Name; } catch (Exception) { }
            try
            {
                var id = Element?.LevelId;
                if (id != null && id != ElementId.InvalidElementId) level = Element.Document.GetElement(id)?.Name;
            }
            catch (Exception) { }
            return HostLabels.Format(cat, type, level, IsLinked ? LinkName ?? "" : null);
        }
    }

    /// <summary>
    /// Finds host faces (ceilings, slab soffits and tops, walls), in this model and in
    /// linked Revit models.
    ///
    /// Fast path (HostIndexes.cs): candidate hosts are collected ONCE per run, their
    /// horizontal planar faces (and wall side faces) are put in plan indexes, and each
    /// block point is looked up there - no ray per block. Results are cached.
    /// Fallback: ray casting with a single ReferenceIntersector per host type, in a
    /// temporary clean 3D view (view templates, hidden categories or section boxes in the
    /// user's views cannot hide hosts), used only where the index could not be built
    /// (non-planar faces, curtain walls, ...) or when a face from the index cannot host.
    /// </summary>
    public partial class HostFinder
    {
        public const string TempViewName = "CAD2Revit - host search (temporary)";

        readonly Document _doc;
        readonly View3D _view;
        readonly bool _searchLinks;
        readonly Dictionary<HostMode, ReferenceIntersector> _intersectors = new Dictionary<HostMode, ReferenceIntersector>();

        readonly PhaseTimer _timer;

        public HostFinder(Document doc, View3D view, bool searchLinks, PhaseTimer timer = null)
        {
            _doc = doc;
            _view = view;
            _searchLinks = searchLinks;
            _timer = timer ?? new PhaseTimer();
        }

        /// <summary>Creates a plain isometric 3D view. Call inside a transaction.</summary>
        public static View3D CreateTempView(Document doc)
        {
            var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.ThreeDimensional);
            var view = View3D.CreateIsometric(doc, vft.Id);
            try { view.ViewTemplateId = ElementId.InvalidElementId; } catch (Exception) { }
            try { view.IsSectionBoxActive = false; } catch (Exception) { }
            try { view.DetailLevel = ViewDetailLevel.Fine; } catch (Exception) { }
            try { view.Name = TempViewName; } catch (Exception) { }   // name clash from a crashed run: harmless
            doc.Regenerate();
            return view;
        }

        ReferenceIntersector Intersector(HostMode mode)
        {
            if (_intersectors.TryGetValue(mode, out var ri)) return ri;
            var cats = mode == HostMode.Wall
                ? new List<BuiltInCategory> { BuiltInCategory.OST_Walls }
                : mode == HostMode.SlabAbove || mode == HostMode.SlabBelow
                    ? new List<BuiltInCategory> { BuiltInCategory.OST_Floors }   // structural + architectural slabs only
                : mode == HostMode.Ceiling
                    ? new List<BuiltInCategory> { BuiltInCategory.OST_Ceilings }
                    : new List<BuiltInCategory> { BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Floors,
                                                  BuiltInCategory.OST_Roofs, BuiltInCategory.OST_StructuralFraming };
            ElementFilter filter = cats.Count == 1
                ? (ElementFilter)new ElementCategoryFilter(cats[0])
                : new ElementMulticategoryFilter(cats);
            ri = new ReferenceIntersector(filter, FindReferenceTarget.Face, _view)
            {
                FindReferencesInRevitLinks = _searchLinks,
            };
            _intersectors[mode] = ri;
            return ri;
        }

        HostHit MakeHit(ReferenceWithContext ctx, XYZ direction)
        {
            var reference = ctx.GetReference();
            var pt = reference.GlobalPoint;
            var normal = FaceNormal(reference, pt) ?? direction.Negate();
            var (elem, linked) = HostElement(reference);
            return new HostHit
            {
                Reference = reference, Point = pt, FaceNormal = normal, RoomNormal = normal,
                Element = elem, IsLinked = linked, Distance = ctx.Proximity,
                LinkName = linked ? LinkName(reference.ElementId) : "",
            };
        }

        string LinkName(ElementId linkInstanceId)
        {
            try
            {
                var link = _doc.GetElement(linkInstanceId) as RevitLinkInstance;
                var name = link != null ? _doc.GetElement(link.GetTypeId())?.Name : null;   // RevitLinkType, e.g. "STR.rvt"
                if (string.IsNullOrWhiteSpace(name)) name = link?.GetLinkDocument()?.Title;
                return name ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>
        /// Slab (above): the underside of the first floor slab straight above (x, y), searching
        /// up from the level - i.e. the slab of the level above. Slab (below): the top face of the
        /// slab at the level, searching down from just above it. Only Floor elements count (beams,
        /// ceilings, ducts are ignored), in this model and in Revit links. Faces pointing the wrong
        /// way (e.g. the top of this level's own slab) are skipped. Null = no slab within maxDistFt
        /// (no slab, or the ray passes through an opening).
        /// </summary>
        public HostHit FindSlabRay(bool above, double x, double y, double levelZ, double maxDistFt)
        {
            using (_timer.Time(Phases.HostRay))
            {
            var dir = above ? XYZ.BasisZ : XYZ.BasisZ.Negate();
            var origin = new XYZ(x, y, levelZ + (above ? 0.01 : SlabSearch.BelowStartMm / SlabSearch.MmPerFoot));
            var hits = Intersector(HostMode.SlabAbove).Find(origin, dir);
            if (hits == null) return null;
            foreach (var ctx in hits.Where(h => h.Proximity <= maxDistFt).OrderBy(h => h.Proximity))
            {
                var hit = MakeHit(ctx, dir);
                double nz = hit.FaceNormal.Z;
                if (above ? nz < -0.5 : nz > 0.5) return hit;   // bottom face (faces down) / top face (faces up)
            }
            return null;
            }
        }

        readonly Dictionary<string, double?> _slabZ = new Dictionary<string, double?>();

        /// <summary>
        /// Elevation to use when a ray finds no slab (opening, or slab missing at that point):
        /// the underside (above) or top (below) of the largest floor slab in the search window
        /// of this level, from bounding boxes of floors in this model and in links. Null if none.
        /// </summary>
        public double? FallbackSlabZ(bool above, double levelZ, double maxDistFt)
        {
            var key = (above ? "A" : "B") + levelZ.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
            if (_slabZ.TryGetValue(key, out var cached)) return cached;
            double? best = null;
            double bestArea = -1;
            void Consider(Element e, Transform tf)
            {
                BoundingBoxXYZ bb;
                try { bb = e.get_BoundingBox(null); } catch (Exception) { return; }
                if (bb == null) return;
                var t = tf ?? Transform.Identity;
                if (bb.Transform != null) t = t.Multiply(bb.Transform);
                double zMin = t.OfPoint(bb.Min).Z, zMax = t.OfPoint(bb.Max).Z;
                if (zMin > zMax) { var tmp = zMin; zMin = zMax; zMax = tmp; }
                double z = above ? zMin : zMax;
                bool inWindow = above
                    ? z > levelZ + 0.01 && z <= levelZ + maxDistFt
                    : z <= levelZ + SlabSearch.BelowStartMm / SlabSearch.MmPerFoot && z >= levelZ + SlabSearch.BelowStartMm / SlabSearch.MmPerFoot - maxDistFt;
                if (!inWindow) return;
                double area = Math.Abs((bb.Max.X - bb.Min.X) * (bb.Max.Y - bb.Min.Y));
                if (area > bestArea) { bestArea = area; best = z; }
            }
            foreach (var e in new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType())
                Consider(e, null);
            if (_searchLinks)
                foreach (var link in new FilteredElementCollector(_doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                {
                    Document linkDoc;
                    try { linkDoc = link.GetLinkDocument(); } catch (Exception) { continue; }
                    if (linkDoc == null) continue;
                    var tf = link.GetTotalTransform();
                    foreach (var e in new FilteredElementCollector(linkDoc).OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType())
                        Consider(e, tf);
                }
            _slabZ[key] = best;
            return best;
        }

        /// <summary>Nearest ceiling (Ceiling) or ceiling/slab/roof/beam (Face) straight above
        /// (x, y), searching from just above the level.</summary>
        public HostHit FindAboveRay(HostMode mode, double x, double y, double levelZ, double maxDistFt)
        {
            using (_timer.Time(Phases.HostRay))
            {
            var ctx = Intersector(mode).FindNearest(new XYZ(x, y, levelZ + 0.01), XYZ.BasisZ);
            if (ctx == null || ctx.Proximity > maxDistFt) return null;
            return MakeHit(ctx, XYZ.BasisZ);
            }
        }

        /// <summary>Nearest wall face around (x, y) at height z. Casts horizontal rays
        /// (starting at the CAD block's rotation) and keeps the shortest hit.</summary>
        public HostHit FindWallRay(double x, double y, double z, double maxDistFt, double startAngle, int rays = 16)
        {
            using (_timer.Time(Phases.HostRay))
            {
            var ri = Intersector(HostMode.Wall);
            var origin = new XYZ(x, y, z);
            ReferenceWithContext best = null;
            XYZ bestDir = null;
            for (int i = 0; i < rays; i++)
            {
                double a = startAngle + 2 * Math.PI * i / rays;
                var d = new XYZ(Math.Cos(a), Math.Sin(a), 0);
                var ctx = ri.FindNearest(origin, d);
                if (ctx != null && ctx.Proximity <= maxDistFt && (best == null || ctx.Proximity < best.Proximity))
                {
                    best = ctx;
                    bestDir = d;
                }
            }
            if (best == null) return null;
            var hit = MakeHit(best, bestDir);
            // Vertical walls: keep the horizontal part of the normal.
            var n = new XYZ(hit.FaceNormal.X, hit.FaceNormal.Y, 0);
            n = n.GetLength() < 1e-6 ? bestDir.Negate() : n.Normalize();
            hit.FaceNormal = n;
            // Room side = back towards the CAD point (differs from the face normal
            // only when the CAD point lies inside the wall thickness).
            hit.RoomNormal = n.DotProduct(bestDir) > 0 ? n.Negate() : n;
            return hit;
            }
        }

        XYZ FaceNormal(Reference reference, XYZ point)
        {
            try
            {
                GeometryObject face;
                Transform tf;
                if (reference.LinkedElementId != ElementId.InvalidElementId)
                {
                    var link = (RevitLinkInstance)_doc.GetElement(reference.ElementId);
                    var linkDoc = link.GetLinkDocument();
                    var elem = linkDoc.GetElement(reference.LinkedElementId);
                    face = elem.GetGeometryObjectFromReference(reference.CreateReferenceInLink());
                    tf = link.GetTotalTransform();
                }
                else
                {
                    face = _doc.GetElement(reference.ElementId).GetGeometryObjectFromReference(reference);
                    tf = Transform.Identity;
                }
                XYZ n;
                if (face is PlanarFace pf) n = pf.FaceNormal;
                else if (face is Face f)
                {
                    var proj = f.Project(tf.Inverse.OfPoint(point));
                    if (proj == null) return null;
                    n = f.ComputeNormal(proj.UVPoint);
                }
                else return null;
                return tf.OfVector(n).Normalize();
            }
            catch (Exception)
            {
                return null;
            }
        }

        (Element, bool) HostElement(Reference reference)
        {
            if (reference.LinkedElementId != ElementId.InvalidElementId)
            {
                var link = _doc.GetElement(reference.ElementId) as RevitLinkInstance;
                return (link?.GetLinkDocument()?.GetElement(reference.LinkedElementId), true);
            }
            return (_doc.GetElement(reference.ElementId), false);
        }
    }
}
