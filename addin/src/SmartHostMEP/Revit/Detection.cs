using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace SmartHostMEP.Revit
{
    /// <summary>The line a vertical-plane block was put on (Revit wall/column face or DWG line).</summary>
    public class DetectionLine
    {
        public XYZ A, B;          // the chosen line
        public XYZ Block, OnLine; // the block point and where it went on the line
        public string Label = ""; // "Touching line (layer: A-WALL)", "Revit wall (linked)", ...
    }

    /// <summary>
    /// "Show detection": the chosen line of every vertical-plane block drawn as detail lines in a
    /// plan view, in the line style "SmartHost Detection" (magenta), plus a short line from the block
    /// to where it went on the line. They are temporary: every Preview/Run removes the previous ones
    /// (all detail lines in that line style), and Ctrl+Z removes them too.
    /// </summary>
    public static class Detection
    {
        public const string StyleName = "SmartHost Detection";

        static GraphicsStyle Style(Document doc, bool create)
        {
            var lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            Category sub = null;
            foreach (Category c in lines.SubCategories)
                if (c.Name == StyleName) { sub = c; break; }
            if (sub == null)
            {
                if (!create) return null;
                sub = doc.Settings.Categories.NewSubcategory(lines, StyleName);
                sub.LineColor = new Color(230, 0, 160);
                try { sub.SetLineWeight(5, GraphicsStyleType.Projection); } catch (Exception) { }
            }
            return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
        }

        /// <summary>Ids of all detection lines in the model.</summary>
        static List<ElementId> Existing(Document doc)
        {
            var style = Style(doc, false);
            if (style == null) return new List<ElementId>();
            return new FilteredElementCollector(doc).OfClass(typeof(CurveElement)).Cast<CurveElement>()
                .Where(c => c.LineStyle != null && c.LineStyle.Id == style.Id).Select(c => c.Id).ToList();
        }

        /// <summary>Removes the previous detection lines and, when <paramref name="lines"/> has any,
        /// draws them in <paramref name="view"/>. One transaction. Returns how many were drawn.</summary>
        public static int Show(Document doc, View view, IList<DetectionLine> lines)
        {
            var old = Existing(doc);
            bool draw = lines != null && lines.Count > 0 && view != null && CanDraw(view);
            if (old.Count == 0 && !draw) return 0;
            int n = 0;
            using (var t = new Transaction(doc, "SmartHost: Show detection"))
            {
                t.Start();
                if (old.Count > 0) doc.Delete(old);
                if (draw)
                {
                    var style = Style(doc, true);
                    double z = (view as ViewPlan)?.GenLevel?.ProjectElevation ?? 0;
                    XYZ P(XYZ p) => new XYZ(p.X, p.Y, z);
                    var seen = new HashSet<string>();
                    void Add(XYZ a, XYZ b)
                    {
                        a = P(a);
                        b = P(b);
                        if (a.DistanceTo(b) < 5 / 304.8) return;
                        var key = $"{a.X:0.000},{a.Y:0.000},{b.X:0.000},{b.Y:0.000}";
                        if (!seen.Add(key)) return;
                        try
                        {
                            var dc = doc.Create.NewDetailCurve(view, Line.CreateBound(a, b));
                            dc.LineStyle = style;
                            n++;
                        }
                        catch (Exception) { /* line too short or not in the view plane */ }
                    }
                    foreach (var l in lines)
                    {
                        Add(l.A, l.B);
                        if (l.Block != null && l.OnLine != null) Add(l.Block, l.OnLine);
                    }
                }
                t.Commit();
            }
            return n;
        }

        public static bool CanDraw(View v) =>
            v is ViewPlan && !v.IsTemplate;
    }
}
