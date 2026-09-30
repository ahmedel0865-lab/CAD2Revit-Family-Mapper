using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using CAD2Revit.Core;

namespace CAD2Revit.Revit
{
    /// <summary>
    /// Vertical work planes for wall devices when there is no Revit wall to host on
    /// (Host Type "vertical", or "wall" with no wall found). A face-based family placed
    /// on one of these stands upright at its elevation, facing <c>facing</c>.
    ///
    /// Devices on the same line (same facing direction and same distance from the
    /// origin, to 1 mm / 0.1 deg) share one reference plane, and planes created by
    /// earlier runs (named "CAD2Revit vertical ...") are reused, so the model does
    /// not fill up with planes. Reference planes can be hidden per view
    /// (Visibility/Graphics > Annotation Categories > Reference Planes).
    /// </summary>
    public class VerticalPlanes
    {
        public const string NamePrefix = "CAD2Revit vertical";
        const double HalfLengthFt = 0.5;   // drawn length only; work planes are infinite
        static readonly double CosMaxTurn = Math.Cos(0.01 * Math.PI / 180);   // reuse planes turned by < 0.01 deg

        readonly Document _doc;
        readonly View _view;
        readonly Dictionary<(long, long), ReferencePlane> _planes = new Dictionary<(long, long), ReferencePlane>();

        public VerticalPlanes(Document doc, Level level)
        {
            _doc = doc;
            _view = PlanViewFor(doc, level);
            foreach (var rp in new FilteredElementCollector(doc).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>())
            {
                if (!(rp.Name ?? "").StartsWith(NamePrefix, StringComparison.Ordinal)) continue;
                var n = rp.Normal;
                if (Math.Abs(n.Z) > 1e-6) continue;
                var k = Key(n, rp.BubbleEnd);
                if (!_planes.ContainsKey(k)) _planes[k] = rp;
            }
        }

        static (long, long) Key(XYZ facing, XYZ point)
        {
            double deg = Math.Atan2(facing.Y, facing.X) * 180.0 / Math.PI;
            if (deg < 0) deg += 360.0;
            double distMm = facing.DotProduct(new XYZ(point.X, point.Y, 0)) * 304.8;
            return ((long)Math.Round(deg * 10) % 3600, (long)Math.Round(distMm));
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

        /// <summary>A vertical plane through <paramref name="point"/> whose normal is the
        /// horizontal <paramref name="facing"/> direction. <paramref name="onPlane"/> is the
        /// point projected onto the plane (at most 0.5 mm away: a shared plane is only reused
        /// when the point is that close to it) and <paramref name="normal"/> the plane's
        /// actual normal (= facing).</summary>
        public Reference Get(XYZ point, XYZ facing, out XYZ onPlane, out XYZ normal)
        {
            facing = new XYZ(facing.X, facing.Y, 0).Normalize();
            var key = Key(facing, point);
            // A plane created for a block whose sub-transaction was rolled back no longer exists.
            // A plane found under the same key can still be slightly turned or shifted (the key
            // is rounded): reuse it only if it faces the same way and passes through the point.
            if (!_planes.TryGetValue(key, out var rp) || !rp.IsValidObject ||
                rp.Normal.DotProduct(facing) < CosMaxTurn ||
                Math.Abs(VerticalPlacement.Distance(V(point), V(rp.BubbleEnd), V(rp.Normal))) * VerticalPlacement.MmPerFoot > VerticalPlacement.PlaneReuseMm)
            {
                rp = Create(point, facing);
                _planes[key] = rp;
            }
            normal = rp.Normal;
            onPlane = X(VerticalPlacement.Project(V(point), V(rp.BubbleEnd), V(normal)));
            return rp.GetReference();
        }

        /// <summary>New plane through the point, normal = facing. The end points are ordered so
        /// the normal already points the right way; no Flip() (which would need a regeneration
        /// before the normal can be trusted).</summary>
        ReferencePlane Create(XYZ point, XYZ facing)
        {
            if (_view == null) throw new InvalidOperationException("no floor plan view found to create a vertical work plane");
            var along = X(VerticalPlacement.Along(V(facing)));
            ReferencePlane Make(double sign) => _doc.Create.NewReferencePlane(
                point.Subtract(along.Multiply(sign * HalfLengthFt)), point.Add(along.Multiply(sign * HalfLengthFt)), XYZ.BasisZ, _view);
            var rp = Make(1);
            if (rp.Normal.DotProduct(facing) < 0)
            {
                _doc.Delete(rp.Id);
                rp = Make(-1);
            }
            if (rp.Normal.DotProduct(facing) < 0) rp.Flip();   // not expected
            try { rp.Name = NamePrefix + " " + Compat.IdValue(rp.Id); } catch (Exception) { }
            return rp;
        }
    }
}
