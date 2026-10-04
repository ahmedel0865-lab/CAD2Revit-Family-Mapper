using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using CAD2Revit.Core;

namespace CAD2Revit.Revit
{
    /// <summary>
    /// "Show detection": draws the wall/column faces the tool used for vertical planes as red
    /// detail lines in a plan view, so you can see what it understood. The lines go in their own
    /// TransactionGroup: <see cref="Discard"/> rolls the group back (nothing left, nothing in the
    /// undo list), <see cref="Keep"/> keeps them as one undo step.
    /// </summary>
    class DetectionLines : IDisposable
    {
        readonly TransactionGroup _group;
        public int Count { get; private set; }
        public string ViewName { get; private set; } = "";

        DetectionLines(Document doc)
        {
            _group = new TransactionGroup(doc, "CAD2Revit: detection lines");
        }

        /// <summary>Draws the faces in the active view when it is a plan view, else in the first
        /// floor plan of <paramref name="level"/>. Null when nothing could be drawn.</summary>
        public static DetectionLines Draw(Document doc, View active, Level level, IEnumerable<CadFace> faces)
        {
            var list = faces.ToList();
            if (list.Count == 0) return null;
            var view = active is ViewPlan vp && !vp.IsTemplate ? vp : PlanFor(doc, level);
            if (view == null) return null;
            double z = view.GenLevel?.ProjectElevation ?? level?.ProjectElevation ?? 0;
            var lines = new DetectionLines(doc) { ViewName = view.Name };
            lines._group.Start();
            try
            {
                DrawLines(doc, view, z, list, lines);
            }
            catch (Exception)
            {
                lines.Dispose();
                throw;
            }
            if (lines.Count == 0)
            {
                lines.Dispose();
                return null;
            }
            return lines;
        }

        static void DrawLines(Document doc, View view, double z, List<CadFace> list, DetectionLines lines)
        {
            using (var t = new Transaction(doc, "CAD2Revit: draw detection lines"))
            {
                t.Start();
                var ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(new Color(220, 30, 30));
                ogs.SetProjectionLineWeight(5);
                var done = new HashSet<string>();
                foreach (var f in list)
                    foreach (var (a, b) in f.Outline)
                    {
                        var key = $"{a.X:0.###},{a.Y:0.###},{b.X:0.###},{b.Y:0.###}";
                        if (!done.Add(key)) continue;
                        var p = new XYZ(a.X, a.Y, z);
                        var q = new XYZ(b.X, b.Y, z);
                        if (p.DistanceTo(q) < doc.Application.ShortCurveTolerance) continue;
                        try
                        {
                            var dc = doc.Create.NewDetailCurve(view, Line.CreateBound(p, q));
                            view.SetElementOverrides(dc.Id, ogs);
                            lines.Count++;
                        }
                        catch (Exception) { /* a line Revit refuses: skip it */ }
                    }
                t.Commit();
            }
        }

        static ViewPlan PlanFor(Document doc, Level level) =>
            new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.GenLevel != null && (level == null || v.GenLevel.Id == level.Id))
                .OrderBy(v => v.ViewType == ViewType.FloorPlan ? 0 : 1).FirstOrDefault();

        public void Keep()
        {
            if (_group.HasStarted() && !_group.HasEnded()) _group.Assimilate();
        }

        public void Discard()
        {
            if (_group.HasStarted() && !_group.HasEnded()) _group.RollBack();
        }

        /// <summary>Anything not explicitly kept is removed.</summary>
        public void Dispose()
        {
            Discard();
            _group.Dispose();
        }
    }
}
