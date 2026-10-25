using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DMCRemapper.Core;
using DMCRemapper.UI;

namespace DMCRemapper.Revit
{
    /// <summary>
    /// The 2D symbol of each loaded family offered in the mapping window (Auto-match by symbol
    /// geometry). Only loadable families are opened (Edit Family); system and in-place families are
    /// skipped. See <see cref="Symbol"/> for which views and geometry count. Signatures are cached (see SignatureCache), so a
    /// family is only opened when it is new or changed.
    /// </summary>
    static class FamilySignatures
    {
        public static readonly string[] ViewOrder = { "Ref. Level", "Front", "Back" };

        public class Stats
        {
            public int Families, FromCache, Opened, WithSymbol, Skipped, Failed;
            public bool Cancelled;
            /// <summary>One line per family for the Auto-match report.</summary>
            public List<string> Lines = new List<string>();
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
                    stats.Lines.Add($"{g.Key}\tskipped: {(family == null ? "family not found" : family.IsInPlace ? "in-place family" : "not editable (system family)")}");
                    continue;
                }
                var key = SignatureCache.Key(g.Key, Stamp(family));
                string from = "cache";
                if (cache.TryGet(key, out var sig)) stats.FromCache++;
                else
                {
                    sig = Read(doc, family, out bool ok, out string error);
                    if (!ok)
                    {
                        stats.Failed++;
                        stats.Lines.Add($"{g.Key}\tcould not be opened: {error}");
                        continue;
                    }
                    stats.Opened++;
                    from = "opened";
                    cache.Put(key, sig);
                }
                if (sig == null || sig.IsEmpty)
                {
                    stats.Lines.Add($"{g.Key}\tno symbol in any plan or Front/Back view ({from}): matched by name only");
                    continue;
                }
                stats.WithSymbol++;
                stats.Lines.Add($"{g.Key}\t{sig.View}: {sig} ({from})");
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
        static SymbolSignature Read(Document doc, Family family, out bool ok, out string error)
        {
            ok = false;
            error = "";
            Document fam = null;
            try
            {
                fam = doc.EditFamily(family);
                if (fam == null) return null;
                ok = true;
                return Symbol(fam);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
            finally
            {
                try { fam?.Close(false); } catch (Exception) { }
            }
        }

        /// <summary>
        /// The family's symbol: the 2D line work (symbolic/detail lines, detail components, annotation
        /// symbols, imported CAD) of the first view that has any, in this order: plan views (Ref. Level
        /// first), then the Front and Back elevations. A family with no 2D symbol gets the outline of its
        /// 3D geometry as seen in its plan view (edges flattened), then in Front/Back. Views are found by
        /// type, so families made in other languages work too. Empty if nothing is drawn anywhere.
        /// </summary>
        public static SymbolSignature Symbol(Document fam)
        {
            var views = new FilteredElementCollector(fam).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate).ToList();
            bool Has(View v, string word) => (v.Name ?? "").IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
            var plans = views.Where(v => v is ViewPlan)
                .OrderBy(v => Has(v, ViewOrder[0]) ? 0 : Has(v, "Ref") ? 1 : 2).ThenBy(v => v.Name).ToList();
            var elevations = views.Where(v => v.ViewType == ViewType.Elevation)
                .OrderBy(v => Has(v, ViewOrder[1]) ? 0 : Has(v, ViewOrder[2]) ? 1 : 2).ThenBy(v => v.Name).Take(2).ToList();
            var order = plans.Concat(elevations).ToList();

            var skip = SymbolGeometry.LayerFilter(fam);

            // 1. 2D symbol: the line work the view shows that is not 3D geometry. Symbolic lines are
            // not owned by one view (they show in every parallel view), so the view is asked what it
            // shows: symbolic and detail lines; and the curves (not the solids) of detail components,
            // annotation symbols, nested families (their own symbolic lines and annotations) and
            // imported CAD.
            foreach (var view in order)
            {
                IList<Element> shown;
                try { shown = new FilteredElementCollector(fam, view.Id).WhereElementIsNotElementType().ToElements(); }
                catch (Exception) { continue; }
                var symbol = new BlockSymbol();
                var project = SymbolGeometry.OnView(view);
                var opts = new Options { View = view, ComputeReferences = false };
                foreach (var e in shown)
                {
                    try
                    {
                        if (e is SymbolicCurve || e is DetailCurve)
                        {
                            var ce = (CurveElement)e;
                            if (ce.LineStyle != null && (ce.LineStyle.Name ?? "").StartsWith("<Invisible", StringComparison.OrdinalIgnoreCase)) continue;
                            var curve = ce.GeometryCurve;
                            if (curve == null) continue;
                            if (!SymbolGeometry.CollectCurve(curve, Transform.Identity, symbol, project, circles: true)) break;
                        }
                        else if (e is FamilyInstance || e is ImportInstance)
                        {
                            if (!SymbolGeometry.Collect(e.get_Geometry(opts), Transform.Identity, symbol, project, skip, circles: true)) break;
                        }
                    }
                    catch (Exception) { /* unreadable element: the rest still counts */ }
                }
                if (!symbol.IsEmpty) return SymbolSignature.From(symbol, view.Name);
            }

            // 2. No 2D symbol: the 3D geometry as the view shows it.
            foreach (var view in order)
            {
                var symbol = new BlockSymbol();
                var project = SymbolGeometry.OnView(view);
                var opts = new Options { View = view, ComputeReferences = false };
                IEnumerable<Element> shown;
                try { shown = new FilteredElementCollector(fam, view.Id).WhereElementIsNotElementType().ToElements(); }
                catch (Exception) { continue; }
                foreach (var e in shown)
                {
                    if (!(e is GenericForm || e is FamilyInstance || e is ImportInstance || e is FreeFormElement)) continue;
                    if (e.OwnerViewId != null && e.OwnerViewId != ElementId.InvalidElementId) continue;   // 2D: read above
                    try
                    {
                        if (!SymbolGeometry.Collect(e.get_Geometry(opts), Transform.Identity, symbol, project, skip, circles: true, solids: true)) break;
                    }
                    catch (Exception) { }
                }
                if (!symbol.IsEmpty) return SymbolSignature.From(symbol, view.Name + SymbolSignature.Outline3DSuffix);
            }
            return new SymbolSignature();
        }
    }
}
