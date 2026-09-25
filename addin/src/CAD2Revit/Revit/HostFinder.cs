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
        public double Distance;       // ray length, feet

        public string Describe()
        {
            string cat = "?";
            try { cat = Element?.Category?.Name ?? "?"; } catch (Exception) { }
            return cat + (IsLinked ? " (linked)" : "");
        }
    }

    /// <summary>
    /// Finds host faces (ceilings, slab soffits, walls) by ray casting with
    /// ReferenceIntersector in a temporary, clean 3D view, so view templates,
    /// hidden categories or section boxes in the user's views cannot hide hosts.
    /// Hosts inside linked Revit models are found too.
    /// </summary>
    public class HostFinder
    {
        public const string TempViewName = "CAD2Revit - host search (temporary)";

        readonly Document _doc;
        readonly View3D _view;
        readonly bool _searchLinks;
        readonly Dictionary<HostMode, ReferenceIntersector> _intersectors = new Dictionary<HostMode, ReferenceIntersector>();

        public HostFinder(Document doc, View3D view, bool searchLinks)
        {
            _doc = doc;
            _view = view;
            _searchLinks = searchLinks;
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
            };
        }

        /// <summary>Nearest ceiling (Ceiling) or ceiling/slab/roof/beam (Face) straight above
        /// (x, y), searching from just above the level.</summary>
        public HostHit FindAbove(HostMode mode, double x, double y, double levelZ, double maxDistFt)
        {
            var ctx = Intersector(mode).FindNearest(new XYZ(x, y, levelZ + 0.01), XYZ.BasisZ);
            if (ctx == null || ctx.Proximity > maxDistFt) return null;
            return MakeHit(ctx, XYZ.BasisZ);
        }

        /// <summary>Nearest wall face around (x, y) at height z. Casts horizontal rays
        /// (starting at the CAD block's rotation) and keeps the shortest hit.</summary>
        public HostHit FindWall(double x, double y, double z, double maxDistFt, double startAngle, int rays = 16)
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
