using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CAD2Revit.Core
{
    public enum Status
    {
        Placed,     // created (or, in preview, would be created)
        Duplicate,  // an instance already exists at this location
        Unmapped,   // block name not in the mapping file
        Skipped,    // mapped, but family/type not loaded in the project
        Failed,     // Revit refused the placement / no host found
    }

    /// <summary>The outcome for one CAD block (or one unmapped block name).</summary>
    public class PlacementResult
    {
        public string BlockName = "";
        public MapRow Row;
        public Status Status;
        public long? ElementId;
        public string Message = "";
        public double[] Point;          // X, Y, Z in feet, model internal coordinates
        public double? Rotation;        // radians
        public string Host = "";
        public string Level = "";
        public double ScaleX = 1, ScaleY = 1;
        public bool Mirrored;
        public bool HasBlock = true;    // false for grouped "unmapped" rows
        public int Count = 1;
    }

    public class Summary
    {
        public List<KeyValuePair<string, int>> ByType = new List<KeyValuePair<string, int>>();
        public Dictionary<Status, int> Status = new Dictionary<Status, int>();
        public List<KeyValuePair<string, int>> Unmapped = new List<KeyValuePair<string, int>>();
        public List<PlacementResult> Problems = new List<PlacementResult>();
        /// <summary>Placed, but with a "WARNING:" message (e.g. placed level-based instead of on a plane).</summary>
        public List<PlacementResult> Warnings = new List<PlacementResult>();

        public int Get(Status s) => Status.TryGetValue(s, out var n) ? n : 0;
    }

    public static class Report
    {
        const double MmPerFoot = 304.8;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static readonly string[] LogHeader =
        {
            "Status", "CAD_Block", "Family", "Type", "Level", "ElementId", "Host", "X_mm", "Y_mm", "Z_mm",
            "Rotation_deg", "Block_Scale", "Mirrored", "Message",
        };

        public static string StatusText(Status s) => s.ToString().ToLowerInvariant();

        public static Summary Summarize(IEnumerable<PlacementResult> results)
        {
            var s = new Summary();
            var byType = new Dictionary<string, int>();
            var unmapped = new Dictionary<string, int>();
            foreach (var r in results)
            {
                s.Status[r.Status] = s.Get(r.Status) + r.Count;
                if (r.Status == Status.Placed && r.Row != null)
                {
                    byType[r.Row.Label] = (byType.TryGetValue(r.Row.Label, out var n) ? n : 0) + 1;
                    if ((r.Message ?? "").Contains("WARNING:")) s.Warnings.Add(r);
                }
                else if (r.Status == Status.Unmapped)
                    unmapped[r.BlockName] = (unmapped.TryGetValue(r.BlockName, out var m) ? m : 0) + r.Count;
                else if (r.Status == Status.Failed || r.Status == Status.Skipped)
                    s.Problems.Add(r);
            }
            s.ByType = byType.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToList();
            s.Unmapped = unmapped.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToList();
            return s;
        }

        /// <summary>Identical problems collapsed: (status, block, message, count).</summary>
        public static List<Tuple<Status, string, string, int>> GroupProblems(IEnumerable<PlacementResult> problems) =>
            problems.GroupBy(r => new { r.Status, r.BlockName, r.Message })
                .Select(g => Tuple.Create(g.Key.Status, g.Key.BlockName, g.Key.Message, g.Count()))
                .OrderBy(t => t.Item1).ThenBy(t => t.Item2, StringComparer.OrdinalIgnoreCase)
                .ToList();

        static string Mm(double v) => (v * MmPerFoot).ToString("0.0", Inv);

        static string Deg(double rad)
        {
            var d = rad * 180.0 / Math.PI % 360.0;
            if (d < 0) d += 360.0;
            if (Math.Abs(d - 360.0) < 0.005) d = 0;
            return d.ToString("0.00", Inv);
        }

        static string G3(double v) => v.ToString("0.###", Inv);

        public static List<IList<object>> LogRows(IEnumerable<PlacementResult> results)
        {
            var rows = new List<IList<object>>();
            foreach (var r in results)
            {
                var p = r.Point;
                string scale = !r.HasBlock ? ""
                    : Math.Abs(r.ScaleX - r.ScaleY) < 1e-6 ? G3(r.ScaleX) : G3(r.ScaleX) + " x " + G3(r.ScaleY);
                rows.Add(new object[]
                {
                    StatusText(r.Status), r.BlockName,
                    r.Row?.Family ?? "", r.Row?.TypeName ?? "", r.Level ?? "",
                    r.ElementId.HasValue ? (object)r.ElementId.Value : "",
                    r.Host ?? "",
                    p != null ? Mm(p[0]) : "", p != null ? Mm(p[1]) : "", p != null ? Mm(p[2]) : "",
                    r.Rotation.HasValue ? Deg(r.Rotation.Value) : "",
                    scale,
                    r.Mirrored ? "yes" : "",
                    r.Count == 1 ? r.Message : $"{r.Count} instance(s). {r.Message}".Trim(),
                });
            }
            return rows;
        }

        /// <summary>Plain-text summary shown in the result window.</summary>
        public static string SummaryText(Summary s, bool preview)
        {
            var lines = new List<string>
            {
                preview ? "PREVIEW - nothing was changed in the model." : "DONE",
                "",
                $"{s.Get(Status.Placed)} {(preview ? "would be placed" : "placed")}, " +
                $"{s.Get(Status.Duplicate)} duplicates skipped, {s.Get(Status.Failed)} failed, " +
                $"{s.Get(Status.Skipped)} not loaded, {s.Get(Status.Unmapped)} unmapped instances",
            };
            if (s.ByType.Count > 0)
            {
                lines.Add("");
                lines.Add(TextTable.Format(new[] { "Family : Type", preview ? "Would place" : "Placed" },
                    s.ByType.Select(kv => new object[] { kv.Key, kv.Value })));
            }
            if (s.Unmapped.Count > 0)
            {
                lines.Add("");
                lines.Add("Unmapped blocks (add them to the mapping file to place them):");
                lines.Add(TextTable.Format(new[] { "CAD block", "Instances" },
                    s.Unmapped.Select(kv => new object[] { kv.Key, kv.Value })));
            }
            if (s.Warnings.Count > 0)
            {
                lines.Add("");
                lines.Add(preview ? "Would be placed, with warnings:" : "Placed with warnings:");
                lines.Add(TextTable.Format(new[] { "CAD block", "Count", "Warning" },
                    s.Warnings.GroupBy(r => new { r.BlockName, r.Message })
                        .Select(g => new object[] { g.Key.BlockName, g.Count(), g.Key.Message })));
            }
            if (s.Problems.Count > 0)
            {
                lines.Add("");
                lines.Add("Failed / skipped:");
                lines.Add(TextTable.Format(new[] { "Status", "CAD block", "Count", "Reason" },
                    GroupProblems(s.Problems).Select(g => new object[] { StatusText(g.Item1), g.Item2, g.Item4, g.Item3 })));
            }
            return string.Join(Environment.NewLine, lines);
        }
    }
}
