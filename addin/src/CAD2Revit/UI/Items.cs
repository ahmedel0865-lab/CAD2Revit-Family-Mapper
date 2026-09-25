using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using CAD2Revit.Revit;

namespace CAD2Revit.UI
{
    /// <summary>A Revit element with a display name, for combo boxes and lists.</summary>
    public class Item<T> where T : Element
    {
        public T Element;
        public string Name;
        public override string ToString() => Name;
    }

    public static class Items
    {
        public static List<Item<ImportInstance>> Imports(Document doc) =>
            new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>()
                .Select(imp =>
                {
                    var type = doc.GetElement(imp.GetTypeId());
                    var kind = imp.IsLinked ? "Link" : "Import";
                    if (imp.ViewSpecific) kind += ", view-only";
                    return new Item<ImportInstance>
                    {
                        Element = imp,
                        Name = $"{type?.Name ?? "DWG"} - {kind} (id {Compat.IdValue(imp.Id)})",
                    };
                })
                .OrderBy(i => i.Name).ToList();

        public static List<Item<Level>> Levels(Document doc) =>
            new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.ProjectElevation)
                .Select(l => new Item<Level>
                {
                    Element = l,
                    Name = $"{l.Name}  ({(l.Elevation * 304.8).ToString("+0;-0;0", CultureInfo.InvariantCulture)} mm)",
                })
                .ToList();

        /// <summary>Level of the DWG's own view (view-only links), else the active plan's level.</summary>
        public static int DefaultLevelIndex(Document doc, List<Item<Level>> levels, ImportInstance imp)
        {
            var candidates = new List<Level>();
            if (imp != null && imp.ViewSpecific && doc.GetElement(imp.OwnerViewId) is View owner)
                candidates.Add(owner.GenLevel);
            candidates.Add(doc.ActiveView?.GenLevel);
            foreach (var lvl in candidates.Where(l => l != null))
            {
                int idx = levels.FindIndex(i => i.Element.Id == lvl.Id);
                if (idx >= 0) return idx;
            }
            return 0;
        }
    }
}
