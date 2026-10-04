using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using SmartHostMEP.Core;

namespace SmartHostMEP.Revit
{
    /// <summary>
    /// Vertical work planes for wall devices when there is no Revit wall to host on
    /// (Host Type "Vertical plane", or "wall" with no wall found). A face-based family placed
    /// on one of these stands upright at its elevation, facing the CAD block's direction.
    ///
    /// Each block gets a plane through its own insertion point (X, Y), along the block's X
    /// direction, facing the block's +Y, drawn from the level up to level + 3000 mm and named
    /// "SmartHost_V_&lt;Level&gt;_&lt;n&gt;". An existing SmartHost vertical plane (or one named by the old version) (this run or an
    /// earlier one) is reused ONLY if it is colinear with the block: the block point lies on it
    /// (&lt; 5 mm) and it faces the same way (&lt; 0.5 deg). It is never reused just because it is
    /// the nearest one or on the same level. Reference planes can be hidden per view
    /// (Visibility/Graphics > Annotation Categories > Reference Planes).
    /// </summary>
    public class VerticalPlanes
    {
        /// <summary>Name prefix of planes made by 0.5-0.20 (still recognised for reuse).</summary>
        public const string OldNamePrefix = "CAD2Revit vertical";
        const double HalfLengthFt = 0.5;   // drawn length only; work planes are infinite

        readonly Document _doc;
        readonly Level _level;
        readonly View _view;
        readonly List<(ReferencePlane plane, XYZ point, XYZ normal)> _planes = new List<(ReferencePlane, XYZ, XYZ)>();
        int _next = 1;

        public VerticalPlanes(Document doc, Level level)
        {
            _doc = doc;
            _level = level;
            _view = PlanViewFor(doc, level);
            foreach (var rp in new FilteredElementCollector(doc).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>())
            {
                var name = rp.Name ?? "";
                if (!name.StartsWith(OldNamePrefix, StringComparison.Ordinal) &&
                    !name.StartsWith(VerticalPlacement.OldPlaneNamePrefix, StringComparison.Ordinal) &&
                    !name.StartsWith(VerticalPlacement.PlaneNamePrefix, StringComparison.Ordinal)) continue;
                _next = Math.Max(_next, VerticalPlacement.PlaneNumber(name, level.Name) + 1);
                var n = rp.Normal;
                if (Math.Abs(n.Z) > 1e-6) continue;
                _planes.Add((rp, rp.BubbleEnd, n));
            }
        }

        static View PlanViewFor(Document doc, Level level)
        {
            var plans = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .Where(v => !v.IsTemplate).ToList();
            return plans.FirstOrDefault(v => v.GenLevel != null && v.GenLevel.Id == level.Id && v.ViewType == ViewType.FloorPlan)
                   ?? plans.FirstOrDefault(v => v.GenLevel != null && v.GenLevel.Id == level.Id)
                   ?? (doc.ActiveView as ViewPlan)
                   ?? plans.FirstOrDefault();
        }

        static V3 V(XYZ p) => new V3(p.X, p.Y, p.Z);
        static XYZ X(V3 p) => new XYZ(p.X, p.Y, p.Z);

        /// <summary>The vertical plane for a block at <paramref name="point"/> facing
        /// <paramref name="facing"/>: a colinear existing plane, else a new one through the point.
        /// <paramref name="onPlane"/> is the point on the plane (within 5 mm of it, exact for a new
        /// plane), <paramref name="normal"/> the plane's actual normal (= facing) and
        /// <paramref name="name"/> its name.</summary>
        public Reference Get(XYZ point, XYZ facing, out XYZ onPlane, out XYZ normal, out string name)
        {
            facing = new XYZ(facing.X, facing.Y, 0).Normalize();
            ReferencePlane rp = null;
            XYZ origin = null;
            // Planes created for a block whose sub-transaction was rolled back no longer exist.
            _planes.RemoveAll(p => !p.plane.IsValidObject);
            foreach (var (plane, o, n) in _planes)
                if (VerticalPlacement.CanReuse(V(point), V(o), V(n), V(facing)))
                {
                    rp = plane;
                    origin = o;
                    break;
                }
            if (rp == null)
            {
                rp = Create(point, facing);
                origin = rp.BubbleEnd;
                _planes.Add((rp, origin, rp.Normal));
            }
            normal = rp.Normal;
            name = rp.Name;
            onPlane = X(VerticalPlacement.Project(V(point), V(origin), V(normal)));
            return rp.GetReference();
        }

        /// <summary>New plane through the point (at the level), along the block's X direction,
        /// from the level up to level + 3000 mm, normal = facing. The end points are ordered so the
        /// normal already points the right way; no Flip() (its effect needs a regeneration).</summary>
        ReferencePlane Create(XYZ point, XYZ facing)
        {
            if (_view == null) throw new InvalidOperationException("no floor plan view found to create a vertical work plane");
            var along = X(VerticalPlacement.Along(V(facing)));
            double z = _level.ProjectElevation;
            var basePt = new XYZ(point.X, point.Y, z);
            var up = XYZ.BasisZ.Multiply(VerticalPlacement.PlaneHeightMm / VerticalPlacement.MmPerFoot);
            ReferencePlane Make(double sign)
            {
                var a = basePt.Subtract(along.Multiply(sign * HalfLengthFt));
                var b = basePt.Add(along.Multiply(sign * HalfLengthFt));
                return _doc.Create.NewReferencePlane2(a, b, a.Add(up), _view);
            }
            var rp = Make(1);
            if (rp.Normal.DotProduct(facing) < 0)
            {
                _doc.Delete(rp.Id);
                rp = Make(-1);
            }
            if (rp.Normal.DotProduct(facing) < 0) rp.Flip();   // not expected
            for (int tries = 0; tries < 50; tries++)
            {
                try { rp.Name = VerticalPlacement.PlaneName(_level.Name, _next++); break; }
                catch (Exception) { /* name taken (e.g. by a plane on another level): next number */ }
            }
            return rp;
        }
    }
}
