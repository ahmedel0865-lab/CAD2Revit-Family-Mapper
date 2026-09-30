using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using CAD2Revit.UI;

namespace CAD2Revit.Revit
{
    /// <summary>Loaded family types offered in the mapping window.</summary>
    public static class FamilyCatalog
    {
        /// <summary>Categories shown in the "Revit Family" dropdown.</summary>
        public static readonly BuiltInCategory[] Electrical =
        {
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_LightingDevices,       // switches
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_NurseCallDevices,
            BuiltInCategory.OST_TelephoneDevices,
        };

        /// <summary>Fills session.AllTypes (every loaded family type) and session.Options
        /// ("(Skip)" + electrical types, sorted by label).</summary>
        public static void Load(Document doc, MappingSession session)
        {
            var electricalIds = new HashSet<long>(Electrical.Select(c => Compat.IdValue(new ElementId(c))));
            var electrical = new List<FamilyOption>();
            foreach (var s in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
            {
                var opt = new FamilyOption
                {
                    Family = s.FamilyName.Trim(),
                    TypeName = s.Name.Trim(),
                    Category = s.Category?.Name ?? "",
                };
                opt.Label = opt.Family + " : " + opt.TypeName;
                if (session.AllTypes.ContainsKey(opt.Label)) continue;
                session.AllTypes[opt.Label] = opt;
                if (s.Category != null && electricalIds.Contains(Compat.IdValue(s.Category.Id)))
                    electrical.Add(opt);
            }
            session.Options.Clear();
            session.Options.Add(FamilyOption.Skip);
            foreach (var o in electrical.OrderBy(o => o.Label, System.StringComparer.OrdinalIgnoreCase))
                session.Options.Add(o);
        }
    }
}
