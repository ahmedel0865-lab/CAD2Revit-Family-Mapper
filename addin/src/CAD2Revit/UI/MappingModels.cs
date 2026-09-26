using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using CAD2Revit.Core;

namespace CAD2Revit.UI
{
    /// <summary>One entry of the "Revit Family" dropdown.</summary>
    public class FamilyOption
    {
        public static readonly FamilyOption Skip = new FamilyOption { Label = "(Skip)", IsSkip = true };

        public string Family = "";
        public string TypeName = "";
        public string Category = "";
        public string Label = "";
        public bool IsSkip;

        public override string ToString() => Label;
    }

    /// <summary>One row of the mapping grid = one unique CAD block name.</summary>
    public class BlockRow : INotifyPropertyChanged, IDataErrorInfo
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        /// <summary>Host Type dropdown, in this order.</summary>
        public static readonly string[] HostChoices =
        {
            Mapping.HostDisplay[HostMode.None],
            Mapping.HostDisplay[HostMode.Ceiling],
            Mapping.HostDisplay[HostMode.Wall],
            Mapping.HostDisplay[HostMode.RefPlane],
            Mapping.HostDisplay[HostMode.Face],
            Mapping.HostDisplay[HostMode.Vertical],
        };
        public static readonly string[] FacingChoices = { "Down", "Up" };
        public static readonly string RefPlaneLabel = Mapping.HostDisplay[HostMode.RefPlane];

        FamilyOption _family = FamilyOption.Skip;
        string _elevation = "0", _rotation = "0", _host = Mapping.HostDisplay[HostMode.None], _facing = "Down";

        string _level = "", _category = BlockCategories.Other;

        public BlockRow(string name, int count, ObservableCollection<FamilyOption> options, string defaultLevel = "")
        {
            BlockName = name;
            Count = count;
            DefaultLevel = defaultLevel ?? "";
            _level = DefaultLevel;
            _category = BlockCategories.Classify(name);
            // Own view per row so typing in one dropdown does not filter the others.
            Options = new ListCollectionView(options);
        }

        public string BlockName { get; }
        public int Count { get; }
        public string Display => $"{BlockName} ({Count})";
        public ICollectionView Options { get; }
        public bool AutoMatched { get; set; }

        public FamilyOption Family
        {
            get => _family;
            set
            {
                if (value == null || value == _family) return;   // null = transient state while filtering
                _family = value;
                AutoMatched = false;
                Changed(nameof(Family));
                Changed(nameof(FamilyLabel));
            }
        }

        public string FamilyLabel => _family.Label;

        /// <summary>The level picked in step 1 (used when Level is left at the default).</summary>
        public string DefaultLevel { get; }
        /// <summary>Level this block is placed on; Elevation is measured from it.</summary>
        public string Level { get => _level; set { _level = value ?? DefaultLevel; Changed(nameof(Level)); } }

        /// <summary>Electrical / Mechanical / Plumbing / Architectural / Structural / Annotation / Other.</summary>
        public string Category
        {
            get => _category;
            set
            {
                _category = BlockCategories.Normalize(value) ?? BlockCategories.Other;
                CategoryIsAuto = false;
                Changed(nameof(Category));
                Changed(nameof(CategoryOrder));
            }
        }
        public int CategoryOrder => BlockCategories.Order(_category);
        /// <summary>True while the category is still the automatic guess.</summary>
        public bool CategoryIsAuto { get; set; } = true;

        public string Elevation { get => _elevation; set { _elevation = value; Changed(nameof(Elevation)); } }
        public string Rotation { get => _rotation; set { _rotation = value; Changed(nameof(Rotation)); } }
        public string Host { get => _host; set { _host = value; Changed(nameof(Host)); } }
        /// <summary>Down (ceiling devices) or Up (floor devices); used by Reference Plane hosting.</summary>
        public string Facing { get => _facing; set { _facing = value; Changed(nameof(Facing)); } }
        /// <summary>Host Type before "Use reference planes for all rows" was ticked.</summary>
        public string HostBeforeAll { get; set; }

        public double? ElevationMm => Mapping.ParseNumber(_elevation);
        public double? RotationDeg => Mapping.ParseNumber(_rotation);

        public string this[string column]
        {
            get
            {
                if (column == nameof(Elevation) && ElevationMm == null) return "Elevation must be a number (mm)";
                if (column == nameof(Rotation) && RotationDeg == null) return "Rotation must be a number (degrees)";
                return null;
            }
        }

        public string Error => this[nameof(Elevation)] ?? this[nameof(Rotation)];

        /// <summary>Row -> mapping row (Family empty when skipped).</summary>
        public MapRow ToMapRow() => new MapRow
        {
            Block = BlockName,
            Family = _family.IsSkip ? "" : _family.Family,
            TypeName = _family.IsSkip ? "" : _family.TypeName,
            OffsetMm = ElevationMm ?? 0,
            RotationDeg = RotationDeg ?? 0,
            Host = Mapping.ParseHost(_host) ?? HostMode.None,
            Facing = Mapping.ParseFacing(_facing) ?? Core.Facing.Down,
            // Rows left on the default level follow whatever level is picked next time.
            LevelName = string.Equals(_level, DefaultLevel, StringComparison.OrdinalIgnoreCase) ? "" : _level,
            Category = _category,
        };

        /// <summary>Apply a mapping-file row to this grid row.</summary>
        public void Apply(MapRow row, FamilyOption option)
        {
            Family = option ?? FamilyOption.Skip;
            Elevation = row.OffsetMm.ToString("0.###", Inv);
            Rotation = row.RotationDeg.ToString("0.###", Inv);
            Host = Mapping.HostDisplay[row.Host];
            Facing = row.Facing.ToString();
            if (!string.IsNullOrEmpty(row.Category)) Category = row.Category;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Changed(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    /// <summary>Everything the mapping window works with (kept between Preview and Run).</summary>
    public class MappingSession
    {
        public string DwgName = "";
        public string LevelName = "";
        public int InstanceCount;
        public List<BlockRow> Rows = new List<BlockRow>();
        public ObservableCollection<FamilyOption> Options = new ObservableCollection<FamilyOption>();
        /// <summary>All loaded family types (any category), by "Family : Type" label.</summary>
        public Dictionary<string, FamilyOption> AllTypes = new Dictionary<string, FamilyOption>(StringComparer.OrdinalIgnoreCase);
        public string ProjectMappingPath = "";
        /// <summary>All level names in the model (Level dropdown), lowest first.</summary>
        public List<string> LevelNames = new List<string>();
        public Settings Settings;

        /// <summary>Find a family type by name; adds non-electrical types to the dropdown on demand.</summary>
        public FamilyOption Resolve(string family, string type)
        {
            if (string.IsNullOrWhiteSpace(family)) return FamilyOption.Skip;
            if (!AllTypes.TryGetValue(family.Trim() + " : " + (type ?? "").Trim(), out var opt)) return null;
            if (!Options.Contains(opt)) Options.Add(opt);
            return opt;
        }

        /// <summary>Apply a loaded mapping to the grid. Returns (rows applied, messages).</summary>
        public (int applied, List<string> messages) Apply(MappingResult mapping)
        {
            int applied = 0;
            var messages = new List<string>(mapping.Errors);
            foreach (var row in Rows)
            {
                if (mapping.Rows.TryGetValue(row.BlockName, out var m))
                {
                    var opt = Resolve(m.Family, m.TypeName);
                    if (opt == null)
                        messages.Add($"'{row.BlockName}': family '{m.Family} : {m.TypeName}' is not loaded in this model - left as (Skip)");
                    row.Apply(m, opt);
                    if (!string.IsNullOrEmpty(m.LevelName))
                    {
                        var lvl = LevelNames.Find(l => l.Equals(m.LevelName.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (lvl != null) row.Level = lvl;
                        else messages.Add($"'{row.BlockName}': level '{m.LevelName}' does not exist in this model - using {row.DefaultLevel}");
                    }
                    applied++;
                }
                else if (mapping.SkippedBlocks.Contains(row.BlockName))
                {
                    row.Family = FamilyOption.Skip;
                    applied++;
                }
                if (mapping.Categories.TryGetValue(row.BlockName, out var cat)) row.Category = cat;
            }
            return (applied, messages);
        }

        /// <summary>Pre-select a family for every (Skip) row whose name closely matches
        /// (optionally only rows accepted by <paramref name="filter"/>).</summary>
        public int AutoMatch(Func<BlockRow, bool> filter = null)
        {
            var candidates = new List<FamilyOption>();
            foreach (var o in Options) if (!o.IsSkip) candidates.Add(o);
            var labels = candidates.ConvertAll(o => o.Label);
            int n = 0;
            foreach (var row in Rows)
            {
                if (!row.Family.IsSkip || (filter != null && !filter(row))) continue;
                int i = FamilyMatcher.BestMatch(row.BlockName, labels, out _);
                if (i < 0) continue;
                row.Family = candidates[i];
                row.AutoMatched = true;
                UseFamilyCategory(row);
                n++;
            }
            return n;
        }

        /// <summary>If the category is still the automatic guess and could not be told from the
        /// block name, take it from the chosen family's Revit category (e.g. Lighting Fixtures).</summary>
        public static void UseFamilyCategory(BlockRow row)
        {
            if (!row.CategoryIsAuto || row.Category != BlockCategories.Other || row.Family.IsSkip) return;
            var fromFamily = BlockCategories.FromRevitCategory(row.Family.Category);
            if (fromFamily == null) return;
            row.Category = fromFamily;
            row.CategoryIsAuto = true;
        }

        public MappingResult ToMapping()
        {
            var result = new MappingResult();
            foreach (var row in Rows)
            {
                var m = row.ToMapRow();
                if (m.Family.Length == 0) result.SkippedBlocks.Add(m.Block);
                else result.Rows[m.Block] = m;
            }
            return result;
        }
    }
}
