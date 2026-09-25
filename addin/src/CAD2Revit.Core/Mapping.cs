using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CAD2Revit.Core
{
    public enum HostMode { None, Ceiling, Face, Wall }

    /// <summary>One row of the mapping table: CAD block -> Revit family type.</summary>
    public class MapRow
    {
        public string Block;
        public string Family;
        public string TypeName;
        public double OffsetMm;
        public double RotationDeg;
        public HostMode Host;
        public int Line;   // row number in the source file (for messages)

        public string Label => Family + " : " + TypeName;
    }

    public class MappingResult
    {
        /// <summary>Case-insensitive: block name -> row.</summary>
        public Dictionary<string, MapRow> Rows = new Dictionary<string, MapRow>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Blocks listed in the file with an empty family ("do not place" / Skip).</summary>
        public HashSet<string> SkippedBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<string> Errors = new List<string>();
    }

    /// <summary>Loads and validates the CAD-block -> Revit-family mapping (CSV or XLSX).</summary>
    public static class Mapping
    {
        public static readonly string[] TemplateHeader =
        {
            "CAD_Block_Name", "Revit_Family_Name", "Revit_Type_Name",
            "Offset_From_Level_mm", "Rotation_Adjustment_deg", "Host_Type",
        };

        // Accepted header spellings, compared after lower-casing and removing
        // everything that is not a letter/digit ("Offset_From_Level (mm)" ==
        // "offset from level mm" == "offsetfromlevelmm").
        static readonly Dictionary<string, string[]> Aliases = new Dictionary<string, string[]>
        {
            ["block"] = new[] { "cadblockname", "blockname", "cadblock", "block" },
            ["family"] = new[] { "revitfamilyname", "familyname", "family" },
            ["type"] = new[] { "revittypename", "typename", "type" },
            ["offset"] = new[] { "offsetfromlevelmm", "offsetfromlevel", "offsetmm", "offset", "elevationmm" },
            ["rotation"] = new[] { "rotationadjustmentdeg", "rotationadjustment", "rotationdeg", "rotation" },
            ["host"] = new[] { "hosttype", "host", "hosting" },
        };

        static readonly Dictionary<string, HostMode> HostValues = new Dictionary<string, HostMode>
        {
            [""] = HostMode.None, ["none"] = HostMode.None, ["nonhosted"] = HostMode.None,
            ["unhosted"] = HostMode.None, ["level"] = HostMode.None, ["levelbased"] = HostMode.None,
            ["no"] = HostMode.None,
            ["ceiling"] = HostMode.Ceiling,
            ["face"] = HostMode.Face,
            ["wall"] = HostMode.Wall,
        };

        /// <summary>Host_Type cell text -> HostMode (null if not recognised).</summary>
        public static HostMode? ParseHost(string text) =>
            HostValues.TryGetValue(Norm(text), out var h) ? h : (HostMode?)null;

        /// <summary>HostMode -> the text written in mapping files.</summary>
        public static string HostText(HostMode host) =>
            host == HostMode.None ? "non-hosted" : host.ToString().ToLowerInvariant();

        /// <summary>Writes rows in the standard mapping format (.xlsx or .csv).
        /// Rows with an empty Family are written as "do not place" (Skip).</summary>
        public static void Save(string path, IEnumerable<MapRow> rows)
        {
            Tables.WriteTable(path, TemplateHeader, rows.Select(r => (IList<object>)new object[]
            {
                r.Block, r.Family ?? "", r.TypeName ?? "", r.OffsetMm, r.RotationDeg, HostText(r.Host),
            }).ToList());
        }

        public static string Norm(string s) => Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z0-9]", "");

        /// <summary>"" -> 0, "2800" -> 2800, "2,5" -> 2.5, "abc" -> null.</summary>
        public static double? ParseNumber(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return 0.0;
            if (text.Contains(",") && !text.Contains(".")) text = text.Replace(",", ".");
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (double?)null;
        }

        public static MappingResult Load(string path)
        {
            try
            {
                return Parse(Tables.ReadTable(path));
            }
            catch (Exception ex)
            {
                var r = new MappingResult();
                r.Errors.Add($"Could not read '{path}': {ex.Message}");
                return r;
            }
        }

        public static MappingResult Parse(List<Dictionary<string, string>> rows)
        {
            var result = new MappingResult();
            if (rows.Count == 0)
            {
                result.Errors.Add("Mapping file has no data rows");
                return result;
            }
            var byNorm = new Dictionary<string, string>();
            foreach (var h in rows[0].Keys)
                if (!byNorm.ContainsKey(Norm(h))) byNorm[Norm(h)] = h;
            var cols = new Dictionary<string, string>();
            foreach (var kv in Aliases)
            {
                var hit = kv.Value.FirstOrDefault(byNorm.ContainsKey);
                if (hit != null) cols[kv.Key] = byNorm[hit];
            }
            var missing = new[] { "block", "family", "type" }.Where(f => !cols.ContainsKey(f)).ToList();
            if (missing.Count > 0)
            {
                result.Errors.Add($"Missing column(s): {string.Join(", ", missing)}. Expected headers: {string.Join(", ", TemplateHeader)}");
                return result;
            }

            string Get(Dictionary<string, string> r, string field) =>
                cols.TryGetValue(field, out var h) && r.TryGetValue(h, out var v) ? (v ?? "").Trim() : "";

            int line = 1;
            foreach (var r in rows)
            {
                line++;
                var block = Get(r, "block");
                var family = Get(r, "family");
                if (block.Length == 0) continue;
                if (family.Length == 0)                 // empty family = "do not place" (Skip)
                {
                    if (!result.Rows.ContainsKey(block)) result.SkippedBlocks.Add(block);
                    continue;
                }
                var type = Get(r, "type");
                if (type.Length == 0)
                {
                    result.Errors.Add($"Row {line}: '{block}' has a family but no type - row skipped");
                    continue;
                }
                var offset = ParseNumber(Get(r, "offset"));
                var rot = ParseNumber(Get(r, "rotation"));
                if (offset == null || rot == null)
                {
                    result.Errors.Add($"Row {line}: offset/rotation must be numbers - row skipped");
                    continue;
                }
                var rawHost = Get(r, "host");
                if (!HostValues.TryGetValue(Norm(rawHost), out var host))
                {
                    result.Errors.Add($"Row {line}: Host_Type '{rawHost}' not recognised (use none/ceiling/face/wall) - using none");
                    host = HostMode.None;
                }
                result.SkippedBlocks.Remove(block);
                if (result.Rows.ContainsKey(block))
                    result.Errors.Add($"Row {line}: block '{block}' is mapped twice - last row wins");
                result.Rows[block] = new MapRow
                {
                    Block = block, Family = family, TypeName = type,
                    OffsetMm = offset.Value, RotationDeg = rot.Value, Host = host, Line = line,
                };
            }
            return result;
        }
    }
}
