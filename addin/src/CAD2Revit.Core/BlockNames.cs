using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CAD2Revit.Core
{
    /// <summary>
    /// Turns the block names Revit reports into the names people recognise, so that
    /// all instances of the same block end up in ONE row.
    ///
    /// 1. Revit prefixes every symbol with the DWG file name:
    ///      "EL101-GROUND FLOOR LIGHTING PLAN.dwg.SMOKE-DET"  ->  "SMOKE-DET"
    /// 2. DWGs exported FROM Revit name blocks "&lt;Family&gt; - &lt;Type&gt;[-&lt;element id&gt;]-&lt;view&gt;":
    ///      "MAAP_Luminaire - F1-7107100-GROUND FLOOR LIGHTING PLAN"  ->  "MAAP_Luminaire - F1"
    ///    The view suffix is only removed when it is shared by many blocks (so normal
    ///    AutoCAD names like "DL-1200-X" are left alone), and the element id only
    ///    when that export pattern was found.
    /// </summary>
    public static class BlockNames
    {
        static readonly Regex IdAndView = new Regex(@"^(.*\S)-(\d{3,})-(.+)$", RegexOptions.Compiled);
        static readonly Regex TrailingId = new Regex(@"^(.*\S)-\d{3,}$", RegexOptions.Compiled);

        public static string StripFilePrefix(string name)
        {
            name = name ?? "";
            int i = name.IndexOf(".dwg.", StringComparison.OrdinalIgnoreCase);
            return i >= 0 ? name.Substring(i + 5) : name;
        }

        /// <summary>The view name suffix of a Revit-exported DWG ("-&lt;id&gt;-&lt;view&gt;"),
        /// or null when the names do not follow that pattern.</summary>
        public static string DetectExportView(ICollection<string> names)
        {
            var views = names.Select(n => IdAndView.Match(n)).Where(m => m.Success)
                .GroupBy(m => m.Groups[3].Value.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).FirstOrDefault();
            if (views == null) return null;
            int n = views.Count();
            return n >= 3 && n >= 0.25 * names.Count ? views.Key : null;
        }

        /// <summary>raw Revit symbol name -> simplified block name, for every distinct name.</summary>
        public static Dictionary<string, string> Simplify(IEnumerable<string> rawNames)
        {
            var raws = rawNames.Distinct().ToList();
            var stripped = raws.ToDictionary(r => r, r => StripFilePrefix(r).Trim());
            var view = DetectExportView(stripped.Values.Distinct().ToList());
            var result = new Dictionary<string, string>();
            foreach (var raw in raws)
            {
                var name = stripped[raw];
                if (view != null)
                {
                    var suffix = "-" + view;
                    if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        name = name.Substring(0, name.Length - suffix.Length).TrimEnd();
                    var id = TrailingId.Match(name);
                    if (id.Success) name = id.Groups[1].Value.TrimEnd();
                }
                result[raw] = name.Length > 0 ? name : stripped[raw];
            }
            return result;
        }
    }
}
