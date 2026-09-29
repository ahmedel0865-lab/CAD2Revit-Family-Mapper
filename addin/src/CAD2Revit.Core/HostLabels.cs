using System;
using System.Collections.Generic;
using System.Linq;

namespace CAD2Revit.Core
{
    /// <summary>Readable host names for the log and the mapping window's "Detected Host" column,
    /// e.g. "Floor: 250mm RC Slab - Third Floor (linked: STR.rvt)".</summary>
    public static class HostLabels
    {
        static readonly Dictionary<string, string> Singular = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Floors"] = "Floor", ["Ceilings"] = "Ceiling", ["Walls"] = "Wall", ["Roofs"] = "Roof",
            ["Structural Framing"] = "Beam", ["Structural Foundations"] = "Foundation",
        };

        public static string Format(string category, string typeName, string levelName, string linkName)
        {
            var cat = string.IsNullOrWhiteSpace(category) ? "Host"
                : Singular.TryGetValue(category.Trim(), out var one) ? one : category.Trim();
            var text = cat;
            if (!string.IsNullOrWhiteSpace(typeName)) text += ": " + typeName.Trim();
            if (!string.IsNullOrWhiteSpace(levelName)) text += " - " + levelName.Trim();
            if (linkName != null) text += string.IsNullOrWhiteSpace(linkName) ? " (linked)" : " (linked: " + linkName.Trim() + ")";
            return text;
        }

        /// <summary>One line per CAD block for the Preview: the most common host, how many
        /// instances use other hosts, and how many could not be placed.</summary>
        public static string Summarize(IEnumerable<PlacementResult> results)
        {
            var list = results.Where(r => r.HasBlock).ToList();
            if (list.Count == 0) return "";
            var placed = list.Where(r => r.Status == Status.Placed).ToList();
            var parts = new List<string>();
            var groups = placed.GroupBy(r => string.IsNullOrEmpty(r.Host) ? "no host (level)" : r.Host)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();
            if (groups.Count > 0)
            {
                var top = groups[0];
                parts.Add(groups.Count == 1 && top.Count() == list.Count ? top.Key : $"{top.Key} ({top.Count()})");
                int others = placed.Count - top.Count();
                if (groups.Count == 2) parts.Add($"{groups[1].Key} ({groups[1].Count()})");
                else if (others > 0) parts.Add($"{others} on {groups.Count - 1} other host(s)");
            }
            int dup = list.Count(r => r.Status == Status.Duplicate);
            int failed = list.Count(r => r.Status == Status.Failed || r.Status == Status.Skipped);
            if (dup > 0) parts.Add($"{dup} duplicate(s)");
            if (failed > 0) parts.Add($"{failed} failed");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Search windows for Host Type "Slab (above)" / "Slab (below)", in feet.</summary>
    public static class SlabSearch
    {
        public const double MmPerFoot = 304.8;
        /// <summary>Rays for "Slab (below)" start this far above the level.</summary>
        public const double BelowStartMm = 300;

        /// <summary>Max upward ray length: the level-to-level height plus a tolerance, so the
        /// search never reaches the slab two floors up. Without a level above, the fallback distance.</summary>
        public static double AboveDistanceFt(double levelZ, double? nextLevelZ, double toleranceMm, double fallbackMm) =>
            nextLevelZ.HasValue && nextLevelZ.Value > levelZ
                ? nextLevelZ.Value - levelZ + toleranceMm / MmPerFoot
                : fallbackMm / MmPerFoot;

        /// <summary>Max downward ray length from BelowStartMm above the level: down to the
        /// tolerance below the level (covers slabs with a finish offset or a small drop).</summary>
        public static double BelowDistanceFt(double toleranceMm) => (BelowStartMm + toleranceMm) / MmPerFoot;

        /// <summary>|normal Z| below this = a sloped face (more than about 3 degrees from flat).</summary>
        public const double SlopedNzMax = 0.9986;

        public static bool IsSloped(double nz) => Math.Abs(nz) < SlopedNzMax;

        /// <summary>Slab (above) on a sloped slab: its underside can rise past the next level + tolerance
        /// at high points, so sloped faces are searched up to the host search distance too.</summary>
        public static double SlopedAboveDistanceFt(double slabUpFt, double hostSearchMm) =>
            Math.Max(slabUpFt, hostSearchMm / MmPerFoot);

        /// <summary>
        /// Nearest slab face straight above/below (x, y): first within maxDist (any slope), then, if
        /// nothing, the nearest face within slopedMaxDist - kept only if that face is sloped, so a flat
        /// slab two floors up is never used.
        /// </summary>
        public static IndexedFace<T> Nearest<T>(FaceIndex<T> idx, double x, double y, double start, bool above,
                                                double maxDist, double slopedMaxDist, out double z)
        {
            bool Faces(IndexedFace<T> f) => above ? f.Nz < -0.5 : f.Nz > 0.5;
            var f0 = idx.Nearest(x, y, start, above, maxDist, Faces, out z);
            if (f0 != null || slopedMaxDist <= maxDist) return f0;
            var f1 = idx.Nearest(x, y, start, above, slopedMaxDist, Faces, out z);
            return f1 != null && IsSloped(f1.Nz) ? f1 : null;
        }
    }
}
