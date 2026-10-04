using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CAD2Revit.Core
{
    public enum HostMode { None, Ceiling, Face, Wall, Vertical, RefPlane, SlabAbove, SlabBelow }

    /// <summary>Which side a family on a horizontal reference plane faces.</summary>
    public enum Facing { Down, Up }

    /// <summary>Which point of the CAD block the family goes on: the block's base (insertion)
    /// point, or the centre of the block symbol's geometry (for blocks drawn away from their base
    /// point, e.g. base point on the wall line and the symbol in the room).</summary>
    public enum PlaceAt { BasePoint, SymbolCentre }

    /// <summary>One row of the mapping table: CAD block -> Revit family type.</summary>
    public class MapRow
    {
        public string Block;
        public string Family;
        public string TypeName;
        public double OffsetMm;
        public double RotationDeg;
        public HostMode Host;
        public Facing Facing = Facing.Down;   // used by HostMode.RefPlane
        public string LevelName = "";         // "" = the level chosen when running
        public string Category = "";          // Electrical / Architectural / ... ("" = auto)
        public PlaceAt PlaceAt = PlaceAt.SymbolCentre;
        public int Line;   // row number in the source file (for messages)

        public string Label => Family + " : " + TypeName;
    }

    /// <summary>Options set above the grid and saved with the mapping: Slab (above) / Ceiling search
    /// range and fallback plane height, and the Wall search distance.</summary>
    public class SlabOptions
    {
        public const double DefaultSearchRangeMm = 5000, DefaultFallbackPlaneMm = 3000, DefaultWallSearchMm = 500, DefaultReviewDistanceMm = 50;
        public double SearchRangeMm = DefaultSearchRangeMm;
        public double FallbackPlaneMm = DefaultFallbackPlaneMm;
        /// <summary>Host Type Wall: max plan distance (mm) from the CAD point to the wall.</summary>
        public double WallSearchMm = DefaultWallSearchMm;
        /// <summary>All host types: an element placed farther than this (mm, in plan) from its CAD block goes to Needs Review.</summary>
        public double ReviewDistanceMm = DefaultReviewDistanceMm;
        /// <summary>Vertical planes: search radius (mm) for the nearest wall/column face or DWG wall line.</summary>
        public double EdgeSearchMm = EdgeSnap.DefaultSearchMm;
        /// <summary>Vertical planes: Snap to face (default) or Through block point.</summary>
        public PlanePosition PlanePosition = PlanePosition.SnapToFace;
        /// <summary>Vertical planes, DWG detection: wall thickness range (distance between the two
        /// parallel lines) and column size range (sides / diameter), mm.</summary>
        public double WallMinMm = CadDetectOptions.DefaultWallMinMm, WallMaxMm = CadDetectOptions.DefaultWallMaxMm;
        public double ColumnMinMm = CadDetectOptions.DefaultColumnMinMm, ColumnMaxMm = CadDetectOptions.DefaultColumnMaxMm;
        /// <summary>Draw the detected walls/columns as detail lines in the active view.</summary>
        public bool ShowDetection;

        public CadDetectOptions Detect() => new CadDetectOptions
        {
            SearchMm = EdgeSearchMm, WallMinMm = WallMinMm, WallMaxMm = WallMaxMm, ColumnMinMm = ColumnMinMm, ColumnMaxMm = ColumnMaxMm,
        };

        public SlabOptions Clone() => (SlabOptions)MemberwiseClone();
    }

    public class MappingResult
    {
        /// <summary>Slab (above) options (defaults when the file has no such columns).</summary>
        public SlabOptions Slab = new SlabOptions();
        /// <summary>True when the file had the slab columns (so loading it should change the window's values).</summary>
        public bool HasSlabOptions;
        /// <summary>Case-insensitive: block name -> row.</summary>
        public Dictionary<string, MapRow> Rows = new Dictionary<string, MapRow>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Blocks listed in the file with an empty family ("do not place" / Skip).</summary>
        public HashSet<string> SkippedBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Category column of every row in the file, including skipped ones.</summary>
        public Dictionary<string, string> Categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> Errors = new List<string>();
    }

    /// <summary>Loads and validates the CAD-block -> Revit-family mapping (CSV or XLSX).</summary>
    public static class Mapping
    {
        public static readonly string[] TemplateHeader =
        {
            "CAD_Block_Name", "Revit_Family_Name", "Revit_Type_Name", "Level",
            "Offset_From_Level_mm", "Rotation_Adjustment_deg", "Host_Type", "Facing", "Category", "Place_At",
        };

        /// <summary>Written on every row by <see cref="Save"/> (same value on each row).</summary>
        public static readonly string[] SlabHeader = { "Slab_Search_Range_mm", "Slab_Fallback_Plane_mm", "Wall_Search_Distance_mm", "Review_Distance_mm",
            "Vertical_Edge_Search_mm", "Vertical_Plane_Position", "Wall_Thickness_Min_mm", "Wall_Thickness_Max_mm",
            "Column_Size_Min_mm", "Column_Size_Max_mm", "Show_Detection" };

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
            ["facing"] = new[] { "facing", "face direction", "facingdirection" },
            ["level"] = new[] { "level", "levelname", "targetlevel", "revitlevel" },
            ["category"] = new[] { "category", "discipline", "blockcategory" },
            ["placeat"] = new[] { "placeat", "insertat", "anchor", "anchorpoint", "placementpoint" },
            ["slabrange"] = new[] { "slabsearchrangemm", "slabsearchrange", "slabrangemm" },
            ["slabplane"] = new[] { "slabfallbackplanemm", "slabfallbackplane", "fallbackreferenceplaneheightmm", "fallbackplanemm" },
            ["wallsearch"] = new[] { "wallsearchdistancemm", "wallsearchdistance", "wallsearchmm", "wallsearch" },
            ["reviewdistance"] = new[] { "reviewdistancemm", "reviewdistance", "maxdistancemm" },
            ["edgesearch"] = new[] { "verticaledgesearchmm", "edgesearchmm", "edgesearch" },
            ["planeposition"] = new[] { "verticalplaneposition", "planeposition" },
            ["wallmin"] = new[] { "wallthicknessminmm", "wallthicknessmin", "wallminmm" },
            ["wallmax"] = new[] { "wallthicknessmaxmm", "wallthicknessmax", "wallmaxmm" },
            ["colmin"] = new[] { "columnsizeminmm", "columnsizemin", "columnminmm" },
            ["colmax"] = new[] { "columnsizemaxmm", "columnsizemax", "columnmaxmm" },
            ["showdetection"] = new[] { "showdetection", "detectionlines" },
        };

        static readonly Dictionary<string, HostMode> HostValues = new Dictionary<string, HostMode>
        {
            [""] = HostMode.None, ["none"] = HostMode.None, ["nonhosted"] = HostMode.None,
            ["unhosted"] = HostMode.None, ["level"] = HostMode.None, ["levelbased"] = HostMode.None,
            ["no"] = HostMode.None,
            ["ceiling"] = HostMode.Ceiling,
            ["face"] = HostMode.Face,
            ["wall"] = HostMode.Wall,
            ["vertical"] = HostMode.Vertical,
            ["verticalplane"] = HostMode.Vertical,
            ["vplane"] = HostMode.Vertical,
            ["referenceplane"] = HostMode.RefPlane,
            ["referenceplaneautocreate"] = HostMode.RefPlane,
            ["refplane"] = HostMode.RefPlane,
            ["plane"] = HostMode.RefPlane,
            ["slab"] = HostMode.SlabAbove,
            ["slababove"] = HostMode.SlabAbove,
            ["slabsoffit"] = HostMode.SlabAbove,
            ["soffit"] = HostMode.SlabAbove,
            ["slabbelow"] = HostMode.SlabBelow,
            ["floor"] = HostMode.SlabBelow,
            ["floorslab"] = HostMode.SlabBelow,
            // display labels used in the mapping window
            ["nonelevelbased"] = HostMode.None,
            ["faceceilingslabroof"] = HostMode.Face,
            ["verticalplanenowall"] = HostMode.Vertical,
        };

        /// <summary>Host Type labels shown in the mapping window, in dropdown order.</summary>
        public static readonly Dictionary<HostMode, string> HostDisplay = new Dictionary<HostMode, string>
        {
            [HostMode.None] = "None (level-based)",
            [HostMode.Ceiling] = "Ceiling",
            [HostMode.SlabAbove] = "Slab (above)",
            [HostMode.SlabBelow] = "Slab (below)",
            [HostMode.Wall] = "Wall",
            [HostMode.RefPlane] = "Reference Plane (auto-create)",
            [HostMode.Face] = "Face (ceiling/slab/roof)",
            [HostMode.Vertical] = "Vertical plane (no wall)",
        };

        /// <summary>Host_Type cell text -> HostMode (null if not recognised).</summary>
        public static HostMode? ParseHost(string text) =>
            HostValues.TryGetValue(Norm(text), out var h) ? h : (HostMode?)null;

        /// <summary>HostMode -> the text written in mapping files.</summary>
        public static string HostText(HostMode host) =>
            host == HostMode.None ? "non-hosted"
            : host == HostMode.RefPlane ? "reference plane"
            : host == HostMode.SlabAbove ? "slab above"
            : host == HostMode.SlabBelow ? "slab below"
            : host.ToString().ToLowerInvariant();

        /// <summary>"down"/"up" (default down).</summary>
        public static Facing? ParseFacing(string text)
        {
            var n = Norm(text);
            if (n.Length == 0 || n == "down" || n == "d") return Facing.Down;
            if (n == "up" || n == "u") return Facing.Up;
            return null;
        }

        /// <summary>"" / "symbol centre" / "center" -> SymbolCentre (the default); "base point" / "insertion point" -> BasePoint; else null.</summary>
        public static PlaceAt? ParsePlaceAt(string text)
        {
            var n = Norm(text);
            if (n.Length == 0) return PlaceAt.SymbolCentre;
            if (n == "basepoint" || n == "base" || n == "insertionpoint" || n == "insertion" || n == "origin")
                return PlaceAt.BasePoint;
            if (n == "symbolcentre" || n == "symbolcenter" || n == "centre" || n == "center" || n == "symbol" || n == "geometrycentre" || n == "geometrycenter")
                return PlaceAt.SymbolCentre;
            return null;
        }

        public static PlanePosition? ParsePlanePosition(string text)
        {
            var n = Norm(text);
            if (n.Length == 0 || n == "snaptoface" || n == "snap" || n == "face") return PlanePosition.SnapToFace;
            if (n == "throughblockpoint" || n == "throughpoint" || n == "blockpoint" || n == "through") return PlanePosition.ThroughBlockPoint;
            return null;
        }

        public static string PlanePositionText(PlanePosition p) => p == PlanePosition.ThroughBlockPoint ? "Through block point" : "Snap to face";

        public static string PlaceAtText(PlaceAt p) => p == PlaceAt.SymbolCentre ? "Symbol centre" : "Base point";

        /// <summary>Writes rows in the standard mapping format (.xlsx or .csv).
        /// Rows with an empty Family are written as "do not place" (Skip).</summary>
        public static void Save(string path, IEnumerable<MapRow> rows, SlabOptions slab = null)
        {
            slab = slab ?? new SlabOptions();
            Tables.WriteTable(path, TemplateHeader.Concat(SlabHeader).ToArray(), rows.Select(r => (IList<object>)new object[]
            {
                r.Block, r.Family ?? "", r.TypeName ?? "", r.LevelName ?? "", r.OffsetMm, r.RotationDeg, HostText(r.Host),
                r.Facing.ToString(), string.IsNullOrEmpty(r.Category) ? BlockCategories.Classify(r.Block) : r.Category,
                PlaceAtText(r.PlaceAt),
                slab.SearchRangeMm, slab.FallbackPlaneMm, slab.WallSearchMm, slab.ReviewDistanceMm,
                slab.EdgeSearchMm, PlanePositionText(slab.PlanePosition), slab.WallMinMm, slab.WallMaxMm, slab.ColumnMinMm, slab.ColumnMaxMm, slab.ShowDetection ? "yes" : "no",
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

            // Slab (above) options: first row with a valid number wins.
            double? First(string field, Func<double, bool> ok)
            {
                if (!cols.ContainsKey(field)) return null;
                foreach (var r in rows)
                {
                    var t = Get(r, field);
                    if (t.Length == 0) continue;
                    var v = ParseNumber(t);
                    if (v.HasValue && ok(v.Value)) return v;
                    result.Errors.Add($"'{cols[field]}' value '{t}' is not valid - using the default");
                    return null;
                }
                return null;
            }
            var range = First("slabrange", v => v > 0);
            var plane = First("slabplane", v => true);
            if (range.HasValue) result.Slab.SearchRangeMm = range.Value;
            if (plane.HasValue) result.Slab.FallbackPlaneMm = plane.Value;
            var wall = First("wallsearch", v => v > 0);
            if (wall.HasValue) result.Slab.WallSearchMm = wall.Value;
            var review = First("reviewdistance", v => v > 0);
            if (review.HasValue) result.Slab.ReviewDistanceMm = review.Value;
            var edge = First("edgesearch", v => v > 0);
            if (edge.HasValue) result.Slab.EdgeSearchMm = edge.Value;
            string FirstText(string field) =>
                cols.ContainsKey(field) ? rows.Select(r => Get(r, field)).FirstOrDefault(t => t.Length > 0) : null;
            var pos = FirstText("planeposition");
            bool hasPos = pos != null && ParsePlanePosition(pos).HasValue;
            if (hasPos) result.Slab.PlanePosition = ParsePlanePosition(pos).Value;
            var wallMin = First("wallmin", v => v > 0);
            var wallMax = First("wallmax", v => v > 0);
            var colMin = First("colmin", v => v > 0);
            var colMax = First("colmax", v => v > 0);
            if (wallMin.HasValue) result.Slab.WallMinMm = wallMin.Value;
            if (wallMax.HasValue) result.Slab.WallMaxMm = wallMax.Value;
            if (colMin.HasValue) result.Slab.ColumnMinMm = colMin.Value;
            if (colMax.HasValue) result.Slab.ColumnMaxMm = colMax.Value;
            var show = FirstText("showdetection");
            if (show != null) result.Slab.ShowDetection = Norm(show) == "yes" || Norm(show) == "true" || show.Trim() == "1";
            result.HasSlabOptions = range.HasValue || plane.HasValue || wall.HasValue || review.HasValue || edge.HasValue ||
                                    hasPos || wallMin.HasValue || wallMax.HasValue || colMin.HasValue || colMax.HasValue || show != null;

            int line = 1;
            foreach (var r in rows)
            {
                line++;
                var block = Get(r, "block");
                var family = Get(r, "family");
                if (block.Length == 0) continue;
                var cat = BlockCategories.Normalize(Get(r, "category"));
                if (cat != null) result.Categories[block] = cat;
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
                    result.Errors.Add($"Row {line}: Host_Type '{rawHost}' not recognised (use none/ceiling/slab above/slab below/face/wall/vertical/reference plane) - using none");
                    host = HostMode.None;
                }
                result.SkippedBlocks.Remove(block);
                var facing = ParseFacing(Get(r, "facing"));
                if (facing == null)
                {
                    result.Errors.Add($"Row {line}: Facing '{Get(r, "facing")}' not recognised (use Down/Up) - using Down");
                    facing = Facing.Down;
                }
                var placeAt = ParsePlaceAt(Get(r, "placeat"));
                if (placeAt == null)
                {
                    result.Errors.Add($"Row {line}: Place_At '{Get(r, "placeat")}' not recognised (use Symbol centre / Base point) - using Symbol centre");
                    placeAt = PlaceAt.SymbolCentre;
                }
                if (result.Rows.ContainsKey(block))
                    result.Errors.Add($"Row {line}: block '{block}' is mapped twice - last row wins");
                result.Rows[block] = new MapRow
                {
                    Block = block, Family = family, TypeName = type,
                    OffsetMm = offset.Value, RotationDeg = rot.Value, Host = host, Facing = facing.Value, Line = line,
                    LevelName = Get(r, "level"),
                    Category = BlockCategories.Normalize(Get(r, "category")) ?? "",
                    PlaceAt = placeAt.Value,
                };
            }
            return result;
        }
    }
}
