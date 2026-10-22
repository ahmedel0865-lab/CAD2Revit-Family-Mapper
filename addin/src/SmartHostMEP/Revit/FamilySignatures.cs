using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using SmartHostMEP.Core;
using SmartHostMEP.UI;

namespace SmartHostMEP.Revit
{
    /// <summary>
    /// The 2D symbol of each loaded family offered in the mapping window (Auto-match by symbol
    /// geometry). Only loadable families are opened (Edit Family); system and in-place families are
    /// skipped. In the family, the views Ref. Level, Front and Back are read in that order and the
    /// first one with line work counts: symbolic and detail lines, detail components and annotation
    /// symbols, and imported CAD owned by that view. Signatures are cached (see SignatureCache), so a
    /// family is only opened when it is new or changed.
    /// </summary>
    static class FamilySignatures
    {
        public static readonly string[] ViewOrder = { "Ref. Level", "Front", "Back" };

        public class Stats
        {
            public int Families, FromCache, Opened, WithSymbol, Skipped, Failed;
            public bool Cancelled;
        }

        /// <summary>Signatures of the families of <paramref name="options"/> (one entry per family).
        /// <paramref name="progress"/>(done, total, family name) returns false to stop.</summary>
        public static List<FamilySymbolEntry> Load(Document doc, IEnumerable<FamilyOption> options, SignatureCache cache,
                                                   Func<int, int, string, bool> progress, out Stats stats)
        {
            stats = new Stats();
            var byName = new Dictionary<string, Family>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>())
            {
                var n = (f.Name ?? "").Trim();
                if (n.Length > 0 && !byName.ContainsKey(n)) byName[n] = f;
            }
            var groups = options.Where(o => !o.IsSkip).GroupBy(o => o.Family, StringComparer.OrdinalIgnoreCase).ToList();
            stats.Families = groups.Count;
            var result = new List<FamilySymbolEntry>();
            int done = 0;
            foreach (var g in groups)
            {
                if (progress != null && !progress(done, groups.Count, g.Key)) { stats.Cancelled = true; break; }
                done++;
                if (!byName.TryGetValue(g.Key, out var family) || family.IsInPlace || !family.IsEditable)
                {
                    stats.Skipped++;
                    continue;
                }
                var key = SignatureCache.Key(g.Key, Stamp(family));
                if (cache.TryGet(key, out var sig)) stats.FromCache++;
                else
                {
                    sig = Read(doc, family, out bool ok);
                    if (!ok) { stats.Failed++; continue; }
                    stats.Opened++;
                    cache.Put(key, sig);
                }
                if (sig == null || sig.IsEmpty) continue;
                stats.WithSymbol++;
                result.Add(new FamilySymbolEntry
                {
                    Family = g.Key,
                    Category = g.First().Category,
                    Types = g.Select(o => o.Label).ToList(),
                    Signature = sig,
                });
            }
            progress?.Invoke(done, groups.Count, "");
            cache.Save();
            return result;
        }

        /// <summary>Changes whenever the family is edited or reloaded.</summary>
        static string Stamp(Family family)
        {
            try { return family.VersionGuid.ToString("N"); }
            catch (Exception) { return Compat.IdValue(family.Id).ToString(); }
        }

        /// <summary>Opens the family, reads its symbol and closes it without saving. ok = false when
        /// the family could not be opened (it is tried again next time).</summary>
        static SymbolSignature Read(Document doc, Family family, out bool ok)
        {
            ok = false;
            Document fam = null;
            try
            {
                fam = doc.EditFamily(family);
                if (fam == null) return null;
                ok = true;
                return Symbol(fam);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                try { fam?.Close(false); } catch (Exception) { }
            }
        }

        /// <summary>The symbol of the first of Ref. Level, Front, Back that has line work (empty if none).</summary>
        public static SymbolSignature Symbol(Document fam)
        {
            var views = new FilteredElementCollector(fam).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate).ToList();
            var owned = new FilteredElementCollector(fam).WhereElementIsNotElementType().ToElements()
                .Where(e => e.OwnerViewId != null && e.OwnerViewId != ElementId.InvalidElementId)
                .GroupBy(e => Compat.IdValue(e.OwnerViewId)).ToDictionary(g => g.Key, g => g.ToList());
            var skip = SymbolGeometry.LayerFilter(fam);
            foreach (var name in ViewOrder)
            {
                var view = views.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase))
                           ?? views.FirstOrDefault(v => (v.Name ?? "").IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                if (view == null || !owned.TryGetValue(Compat.IdValue(view.Id), out var elements)) continue;
                var symbol = new BlockSymbol();
                var project = SymbolGeometry.OnView(view);
                var opts = new Options { View = view, ComputeReferences = false };
                foreach (var e in elements)
                {
                    try
                    {
                        if (e is CurveElement ce)
                        {
                            if (ce.LineStyle != null && (ce.LineStyle.Name ?? "").StartsWith("<Invisible", StringComparison.OrdinalIgnoreCase)) continue;
                            var curve = ce.GeometryCurve;
                            if (curve == null) continue;
                            SymbolGeometry.CollectCurve(curve, Transform.Identity, symbol, project, circles: true);
                        }
                        else if (e is FamilyInstance || e is ImportInstance)
                            SymbolGeometry.Collect(e.get_Geometry(opts), Transform.Identity, symbol, project, skip, circles: true);
                    }
                    catch (Exception) { /* unreadable element: the rest still counts */ }
                }
                if (!symbol.IsEmpty) return SymbolSignature.From(symbol, view.Name);
            }
            return new SymbolSignature();
        }
    }
}
