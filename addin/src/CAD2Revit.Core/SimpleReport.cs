using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CAD2Revit.Core
{
    /// <summary>
    /// What the result window shows by default: one headline, placed count per family,
    /// and one plain line per problem. Technical details (element ids, slopes, raw log)
    /// stay in <see cref="Report.SummaryText"/>, behind "Show details".
    /// </summary>
    public class SimpleReport
    {
        public int Placed;
        public int Total;
        public bool Preview;
        public List<KeyValuePair<string, int>> ByFamily = new List<KeyValuePair<string, int>>();
        public List<string> Warnings = new List<string>();
        /// <summary>Slab (above) blocks placed at the fallback height, and that line's text.</summary>
        public int SlabFallbacks;
        public List<string> SlabFallbackLines = new List<string>();

        public bool AllPlaced => Total > 0 && Placed == Total;

        /// <summary>"Placed 12 of 14 families" (preview: "Would place ...").</summary>
        public string Headline =>
            $"{(Preview ? "Would place" : "Placed")} {Placed} of {Total} {(Total == 1 ? "family" : "families")}";

        public static SimpleReport From(IEnumerable<PlacementResult> results, bool preview)
        {
            var list = results.ToList();
            var r = new SimpleReport { Preview = preview };
            r.Total = list.Sum(x => x.Count);
            r.Placed = list.Where(x => x.Status == Status.Placed).Sum(x => x.Count);
            r.ByFamily = list.Where(x => x.Status == Status.Placed && x.Row != null)
                .GroupBy(x => x.Row.Family, StringComparer.OrdinalIgnoreCase)
                .Select(g => new KeyValuePair<string, int>(g.Key, g.Sum(x => x.Count)))
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Slab (above) fallbacks: one count line (every block is in the log).
            var fallbacks = list.Where(x => x.Status == Status.Placed && x.SlabFallback).ToList();
            r.SlabFallbacks = fallbacks.Sum(x => x.Count);
            r.SlabFallbackLines = fallbacks
                .GroupBy(x => (x.Message ?? "").Split(new[] { "; " }, StringSplitOptions.None)
                                  .FirstOrDefault(m => m.StartsWith("No slab within", StringComparison.Ordinal)) ?? "No slab in range")
                .Select(g =>
                {
                    int n = g.Sum(x => x.Count);
                    return $"{g.Key} ({n} {(n == 1 ? "family" : "families")})";
                })
                .ToList();

            // One line per (block, problem); worst problems first.
            r.Warnings = list
                .Where(x => !(x.Status == Status.Placed && x.SlabFallback))
                .Select(x => new { x.BlockName, x.Status, x.Count, Text = Problem(x, preview) })
                .Where(x => x.Text != null)
                .GroupBy(x => new { x.BlockName, x.Text, x.Status })
                .OrderBy(g => Rank(g.Key.Status))
                .ThenBy(g => g.Key.BlockName, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    int n = g.Sum(x => x.Count);
                    return $"{g.Key.BlockName}{(n > 1 ? $" (x{n})" : "")}: {g.Key.Text}";
                })
                .ToList();
            return r;
        }

        static int Rank(Status s) =>
            s == Status.Failed ? 0 : s == Status.Skipped ? 1 : s == Status.Unmapped ? 2 : s == Status.Placed ? 3 : 4;

        /// <summary>Plain text for what went wrong with this block, or null if nothing did.</summary>
        public static string Problem(PlacementResult r, bool preview)
        {
            switch (r.Status)
            {
                case Status.Placed:
                    return PlainReason(r.Message);
                case Status.Duplicate:
                    return "already in the model at this spot, skipped";
                case Status.Unmapped:
                    return "no family chosen for this block, not placed";
                case Status.Skipped:
                    return (r.Row != null ? $"family '{r.Row.Family}' is" : "family is") + " not loaded in this project, not placed";
                default:
                    var why = PlainReason(r.Message);
                    return (why ?? "Revit could not place it") + (preview ? ", would not be placed" : ", not placed");
            }
        }

        static readonly Regex[] Strip =
        {
            new Regex(@"\s*\(placement type [^)]*\)", RegexOptions.IgnoreCase),
            new Regex(@",?\s*slope -?[\d.]+ ?deg", RegexOptions.IgnoreCase),
            new Regex(@"\s*\(?\b(element ?id|element|id)[ =:]*\d+\)?", RegexOptions.IgnoreCase),
            new Regex(@" at -?[\d.]+ mm \([^)]*\)", RegexOptions.IgnoreCase),
            new Regex(@";?\s*use a family made from a face-based template", RegexOptions.IgnoreCase),
        };

        /// <summary>
        /// Placer message -> short plain sentence. Drops DEBUG parts, "WARNING:" / "failed -"
        /// prefixes, distances, ids and slopes. e.g. "no ceiling found within 6000 mm above the
        /// level - placed unhosted" -> "no ceiling found above, placed on level". Null if empty.
        /// </summary>
        public static string PlainReason(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return null;
            var parts = new List<string>();
            foreach (var raw in message.Split(new[] { "; " }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = raw.Trim();
                if (p.Length == 0 || p.StartsWith("DEBUG", StringComparison.OrdinalIgnoreCase)) continue;
                if (p.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase)) p = p.Substring(8).Trim();
                if (p.StartsWith("failed - ", StringComparison.OrdinalIgnoreCase)) p = p.Substring(9).Trim();
                foreach (var rx in Strip) p = rx.Replace(p, "");
                p = Regex.Replace(p, @"within [\d.]+ mm above the level", "above", RegexOptions.IgnoreCase);
                p = Regex.Replace(p, @"(above|below) this point within [\d.]+ mm", "$1", RegexOptions.IgnoreCase);
                p = Regex.Replace(p, @"within [\d.]+ mm", "nearby", RegexOptions.IgnoreCase);
                p = Regex.Replace(p, @"placed unhosted", "placed on level", RegexOptions.IgnoreCase);
                p = p.Replace(" - ", ", ").Trim().TrimEnd(';', ',').Trim();
                if (p.Length > 0 && !parts.Contains(p)) parts.Add(p);
            }
            return parts.Count == 0 ? null : string.Join("; ", parts);
        }
    }
}
