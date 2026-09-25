using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

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

        /// <summary>A vertical plane through <paramref name="point"/> whose normal is the
        /// horizontal <paramref name="facing"/> direction. <paramref name="onPlane"/> is the
        /// point moved onto the plane (differs by &lt; 1 mm when a plane is reused).</summary>
        public Reference Get(XYZ point, XYZ facing, out XYZ onPlane)
        {
            facing = new XYZ(facing.X, facing.Y, 0).Normalize();
            var key = Key(facing, point);
            if (!_planes.TryGetValue(key, out var rp))
            {
                if (_view == null) throw new InvalidOperationException("no floor plan view found to create a vertical work plane");
                var along = new XYZ(-facing.Y, facing.X, 0);
                rp = _doc.Create.NewReferencePlane(point.Subtract(along.Multiply(HalfLengthFt)),
                                                   point.Add(along.Multiply(HalfLengthFt)), XYZ.BasisZ, _view);
                if (rp.Normal.DotProduct(facing) < 0) rp.Flip();
                try { rp.Name = NamePrefix + " " + Compat.IdValue(rp.Id); } catch (Exception) { }
                _planes[key] = rp;
            }
            // Project onto the (possibly shared) plane.
            var n = rp.Normal;
            double off = n.DotProduct(point.Subtract(rp.BubbleEnd));
            onPlane = point.Subtract(n.Multiply(off));
            return rp.GetReference();
        }
    }
}
