using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using CAD2Revit.Core;

namespace CAD2Revit.Revit
{
    /// <summary>
    /// Horizontal reference planes for Host Type "Reference Plane (auto-create)".
    ///
    /// One plane per (level, elevation, facing), named e.g. "CAD2Revit_Level 1_+2800mm"
    /// ("_Up" appended for up-facing planes). An existing plane with that name is reused,
    /// so rows with the same elevation share one plane and re-runs do not add duplicates.
    /// New planes span the DWG link's extents plus a margin, and their normal points
    /// down (ceiling devices) or up (floor devices). They are created inside the caller's
    /// transaction, so the single Ctrl+Z of the run removes them too.
    /// </summary>
    public class LevelPlanes
    {
        const double MarginFt = 1000 / 304.8;

        readonly Document _doc;
        readonly Level _level;
        readonly double _minX, _minY, _maxX, _maxY;
        readonly Func<View> _view;
        readonly Dictionary<string, ReferencePlane> _byName = new Dictionary<string, ReferencePlane>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Planes this run created (for the summary).</summary>
        public List<string> Created { get; } = new List<string>();

        /// <param name="extents">minX, minY, maxX, maxY of the DWG link (feet, model coordinates).</param>
        /// <param name="view">View to create planes in (a section/elevation, or a 3D view).</param>
        public LevelPlanes(Document doc, Level level, double[] extents, Func<View> view)
        {
            _doc = doc;
            _level = level;
            _view = view;
            _minX = extents[0] - MarginFt;
            _minY = extents[1] - MarginFt;
            _maxX = extents[2] + MarginFt;
            _maxY = extents[3] + MarginFt;
            foreach (var rp in new FilteredElementCollector(doc).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>())
            {
                var name = rp.Name ?? "";
                if (name.StartsWith(RefPlaneNames.Prefix, StringComparison.OrdinalIgnoreCase) && !_byName.ContainsKey(name))
                    _byName[name] = rp;
            }
        }

        public static XYZ Normal(Facing facing) => facing == Facing.Up ? XYZ.BasisZ : XYZ.BasisZ.Negate();

        /// <summary>The plane for this elevation (mm above the level) and facing.</summary>
        public ReferencePlane Get(double elevationMm, Facing facing)
        {
            var name = RefPlaneNames.For(_level.Name, elevationMm, facing);
            // A plane created for a block whose sub-transaction was rolled back no longer exists.
            if (_byName.TryGetValue(name, out var existing) && existing.IsValidObject) return existing;

            double z = _level.ProjectElevation + elevationMm / 304.8;
            var view = _view() ?? throw new InvalidOperationException("no view available to create a reference plane");
            var rp = _doc.Create.NewReferencePlane2(new XYZ(_minX, _minY, z), new XYZ(_maxX, _minY, z),
                                                    new XYZ(_minX, _maxY, z), view);
            if (rp.Normal.DotProduct(Normal(facing)) < 0) rp.Flip();
            try { rp.Name = name; } catch (Exception) { /* name taken by a non-matching element: keep Revit's name */ }
            _byName[name] = rp;
            if (!Created.Contains(name)) Created.Add(name);
            return rp;
        }

        /// <summary>A section/elevation view to create horizontal planes in (they show there
        /// as lines), or null to let the caller fall back to a 3D view.</summary>
        public static View SectionView(Document doc) =>
            new FilteredElementCollector(doc).OfClass(typeof(ViewSection)).Cast<ViewSection>()
                .FirstOrDefault(v => !v.IsTemplate && (v.ViewType == ViewType.Elevation || v.ViewType == ViewType.Section));
    }
}
