using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media;
using SmartHostMEP.Core;

namespace SmartHostMEP.UI
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
            Mapping.HostDisplay[HostMode.SlabAbove],
            Mapping.HostDisplay[HostMode.SlabBelow],
            Mapping.HostDisplay[HostMode.Wall],
            Mapping.HostDisplay[HostMode.RefPlane],
            Mapping.HostDisplay[HostMode.Face],
            Mapping.HostDisplay[HostMode.Vertical],
        };
        public static readonly string[] FacingChoices = { "Down", "Up" };
        public static readonly string[] PlaceAtChoices = { "Symbol centre", "Base point" };
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
                MatchReview = "";
                DetectedHost = "";
                Changed(nameof(Family));
                Changed(nameof(FamilyLabel));
                Changed(nameof(IsSkipped));
                Changed(nameof(ScoreText));
                Changed(nameof(SourceView));
                Changed(nameof(SelectedCandidate));
            }
        }

        // ---- Auto-match by symbol geometry ----------------------------------------------------

        List<SymbolCandidate> _candidates = new List<SymbolCandidate>();
        /// <summary>The best families for this block from the last Auto-match (at most 3, best first).</summary>
        public List<SymbolCandidate> Candidates
        {
            get => _candidates;
            set
            {
                _candidates = value ?? new List<SymbolCandidate>();
                Changed(nameof(Candidates));
                Changed(nameof(ScoreText));
                Changed(nameof(SourceView));
                Changed(nameof(SelectedCandidate));
            }
        }
        /// <summary>Finds the dropdown entry of a candidate (set by the session).</summary>
        public Func<SymbolCandidate, FamilyOption> ResolveCandidate { get; set; }
        /// <summary>Needs Review reason of the automatic choice ("Ambiguous symbol match"), else "".</summary>
        public string MatchReview { get; set; } = "";

        SymbolCandidate Current => _family.IsSkip ? null
            : _candidates.FirstOrDefault(c => string.Equals(c.Label, _family.Label, StringComparison.OrdinalIgnoreCase));

        /// <summary>Match score of the selected family ("" when it is not one of the candidates).</summary>
        public string ScoreText
        {
            get
            {
                var c = Current;
                if (c == null) return _candidates.Count > 0 && _family.IsSkip ? $"({_candidates[0].Score:0})" : "";
                return c.Learned ? "Learned" : c.Score.ToString("0", Inv) + (MatchReview.Length > 0 ? " ?" : "");
            }
        }
        /// <summary>The family view the matched symbol came from.</summary>
        public string SourceView => Current?.View ?? "";

        /// <summary>The "Top 3" dropdown: picking a candidate selects its family (a manual choice).</summary>
        public SymbolCandidate SelectedCandidate
        {
            get => Current;
            set
            {
                if (value == null || ResolveCandidate == null) return;
                var opt = ResolveCandidate(value);
                if (opt != null) Family = opt;
            }
        }

        /// <summary>Signature of the block symbol (built on first use).</summary>
        public SymbolSignature Signature => _signature ?? (_signature = SymbolSignature.FromBlock(Symbol));
        SymbolSignature _signature;

        public string FamilyLabel => _family.Label;
        public bool IsSkipped => _family.IsSkip;

        /// <summary>2D line work of the CAD block (for the symbol preview), may be empty.</summary>
        public BlockSymbol Symbol { get; set; }
        ImageSource _symbolImage;
        bool _symbolRendered;
        /// <summary>The symbol drawn as an image (built on first use), or null if there is no line work.</summary>
        public ImageSource SymbolImage
        {
            get
            {
                if (!_symbolRendered)
                {
                    _symbolImage = SymbolRenderer.Render(Symbol, Theme.ColorOf("Sh.Text"));
                    _symbolRendered = true;
                }
                return _symbolImage;
            }
        }

        /// <summary>Tooltip for the CAD Block cell: the full name and the symbol.</summary>
        public object SymbolToolTip
        {
            get
            {
                var panel = new System.Windows.Controls.StackPanel { MaxWidth = 320 };
                panel.Children.Add(new System.Windows.Controls.TextBlock { Text = Display, FontWeight = System.Windows.FontWeights.SemiBold, TextWrapping = System.Windows.TextWrapping.Wrap });
                if (SymbolImage != null)
                    panel.Children.Add(new System.Windows.Controls.Image { Source = SymbolImage, Width = 140, Height = 140, Margin = new System.Windows.Thickness(0, 6, 0, 0) });
                return panel;
            }
        }

        /// <summary>The window's Level (rows left on it follow when it changes).</summary>
        public string DefaultLevel { get; private set; }

        /// <summary>The window's Level changed: rows still on the old one move to the new one.</summary>
        public void SetDefaultLevel(string level)
        {
            level = level ?? "";
            bool follow = string.Equals(_level, DefaultLevel, StringComparison.OrdinalIgnoreCase);
            DefaultLevel = level;
            if (follow) Level = level;
        }
        /// <summary>Level this block is placed on; Elevation is measured from it.</summary>
        public string Level { get => _level; set { var v = value ?? DefaultLevel; if (v != _level) DetectedHost = ""; _level = v; Changed(nameof(Level)); } }

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

        public string Elevation { get => _elevation; set { _elevation = value; Changed(nameof(Elevation)); Changed(nameof(ElevationError)); Changed(nameof(ElevationInvalid)); Changed(nameof(HasError)); } }
        public string Rotation { get => _rotation; set { _rotation = value; Changed(nameof(Rotation)); Changed(nameof(RotationError)); Changed(nameof(RotationInvalid)); Changed(nameof(HasError)); } }
        /// <summary>Inline validation (red border + tooltip in the grid); null when valid.</summary>
        public string ElevationError => this[nameof(Elevation)];
        public string RotationError => this[nameof(Rotation)];
        public bool HasError => Error != null;
        public bool ElevationInvalid => ElevationError != null;
        public bool RotationInvalid => RotationError != null;
        public string Host { get => _host; set { if (value != _host) DetectedHost = ""; _host = value; Changed(nameof(Host)); } }

        string _detectedHost = "";
        /// <summary>Host found by the last Preview for this block, e.g. "Floor: 250mm RC Slab -
        /// Third Floor (linked: STR.rvt)". Cleared when Family, Level or Host Type change.</summary>
        public string DetectedHost
        {
            get => _detectedHost;
            set
            {
                if (_detectedHost == (value ?? "")) return;
                _detectedHost = value ?? "";
                Changed(nameof(DetectedHost));
            }
        }
        /// <summary>Down (ceiling devices) or Up (floor devices); used by Reference Plane hosting.</summary>
        public string Facing { get => _facing; set { _facing = value; Changed(nameof(Facing)); } }
        string _placeAt = "Symbol centre";
        /// <summary>Symbol centre (centre of the drawn symbol, default) or Base point (block insertion point).</summary>
        public string PlaceAt { get => _placeAt; set { _placeAt = value; Changed(nameof(PlaceAt)); } }
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
            PlaceAt = Mapping.ParsePlaceAt(_placeAt) ?? Core.PlaceAt.SymbolCentre,
            Review = AutoMatched ? MatchReview : "",
        };

        /// <summary>Apply a mapping-file row to this grid row.</summary>
        public void Apply(MapRow row, FamilyOption option)
        {
            Family = option ?? FamilyOption.Skip;
            Elevation = row.OffsetMm.ToString("0.###", Inv);
            Rotation = row.RotationDeg.ToString("0.###", Inv);
            Host = Mapping.HostDisplay[row.Host];
            Facing = row.Facing.ToString();
            PlaceAt = Mapping.PlaceAtText(row.PlaceAt);
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
        /// <summary>Add-in version shown in the window header.</summary>
        public string Version = "";
        /// <summary>DWG links/imports of the project (setup row) and the one these rows come from.</summary>
        public List<string> DwgNames = new List<string>();
        public int DwgIndex;
        /// <summary>The DWG these rows were read from (DwgIndex changes when another one is chosen).</summary>
        public int PreviousDwgIndex;
        /// <summary>Blocks nested inside other blocks were read (Advanced).</summary>
        public bool IncludeNested;
        /// <summary>Preview (true) or Run (false) from inside the window, with the window's progress bar:
        /// progress(done, total) returns false to stop. Returns true when the window should close (Run done).</summary>
        public Func<bool, Func<int, int, bool>, bool> Place;
        /// <summary>One line shown under the grid after Preview (set by <see cref="Place"/>).</summary>
        public string Message;
        public bool MessageIsWarning;
        /// <summary>Search ranges and fallback plane height (Advanced).</summary>
        public SlabOptions Slab = new SlabOptions();

        /// <summary>Find a family type by name; adds non-electrical types to the dropdown on demand.</summary>
        public FamilyOption Resolve(string family, string type)
        {
            if (string.IsNullOrWhiteSpace(family)) return FamilyOption.Skip;
            if (!AllTypes.TryGetValue(family.Trim() + " : " + (type ?? "").Trim(), out var opt)) return null;
            if (!Options.Contains(opt)) Options.Add(opt);
            return opt;
        }

        /// <summary>Apply a loaded mapping to the grid. Returns (rows applied, messages).
        /// families = false (opening the tool): every row stays on (Skip); only Elevation, Host
        /// Type, Rotation, Facing, Level, Category and the Slab / Ceiling options are restored.</summary>
        public (int applied, List<string> messages) Apply(MappingResult mapping, bool families = true)
        {
            int applied = 0;
            var messages = new List<string>(mapping.Errors);
            if (mapping.HasSlabOptions) Slab = mapping.Slab.Clone();
            foreach (var row in Rows)
            {
                if (mapping.Rows.TryGetValue(row.BlockName, out var m))
                {
                    var opt = families ? Resolve(m.Family, m.TypeName) : FamilyOption.Skip;
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

        /// <summary>"Clear All Families": every row back to (Skip). Returns how many rows changed.</summary>
        public int ClearFamilies()
        {
            int n = 0;
            foreach (var row in Rows)
                if (!row.Family.IsSkip)
                {
                    row.Family = FamilyOption.Skip;
                    n++;
                }
            return n;
        }

        /// <summary>Reads the symbols of the loaded families (opens families not in the cache; set by
        /// the command). Argument: progress(done, total, family) returning false to stop.</summary>
        public Func<Func<int, int, string, bool>, List<FamilySymbolEntry>> LoadFamilySymbols;
        /// <summary>What the last family symbol read did ("Family symbols: 40 read, 12 opened ...").</summary>
        public string SymbolInfo = "";
        /// <summary>One line per family from the last symbol read (Auto-match report).</summary>
        public List<string> SymbolReport = new List<string>();
        /// <summary>Remembered manual matches (loaded on first use).</summary>
        public LearnedMatches Learned;

        public class AutoMatchSummary
        {
            public int Learned, BySymbol, Ambiguous, ByName, Suggested, FamiliesWithSymbol;
            /// <summary>One line per block for the Auto-match report.</summary>
            public List<string> Lines = new List<string>();
            public int Selected => Learned + BySymbol + ByName;
        }

        FamilyOption ResolveLabel(string label) =>
            label != null && AllTypes.TryGetValue(label, out var o) ? Resolve(o.Family, o.TypeName) : null;

        /// <summary>
        /// Auto-match (rows accepted by <paramref name="filter"/>): every row gets its top 3 families by
        /// symbol geometry; rows still on (Skip) get a family when one was matched manually before
        /// (same block and symbol), else when the best scores at least the AutoMatch score (Needs Review
        /// "Ambiguous symbol match" when the second is close). Rows already mapped are never changed.
        /// Blocks without line work fall back to name matching; so do families without a symbol (only
        /// for blocks that no family with a symbol matched).
        /// </summary>
        public AutoMatchSummary AutoMatch(List<FamilySymbolEntry> families, Func<BlockRow, bool> filter = null)
        {
            families = families ?? new List<FamilySymbolEntry>();
            var summary = new AutoMatchSummary { FamiliesWithSymbol = families.Count };
            var matcher = new SymbolMatcher(Settings?.SymbolScore);
            if (Learned == null) Learned = new LearnedMatches();
            var nameCandidates = new List<FamilyOption>();
            foreach (var o in Options) if (!o.IsSkip) nameCandidates.Add(o);
            var labels = nameCandidates.ConvertAll(o => o.Label);
            // Families without a drawn 2D symbol can also be matched by name (an outline of their 3D
            // geometry is a weak signature).
            var withSymbol = new HashSet<string>(families.Where(f => !f.Signature.IsOutline3D).Select(f => f.Family), StringComparer.OrdinalIgnoreCase);
            var noSymbol = nameCandidates.Where(o => !withSymbol.Contains(o.Family)).ToList();
            var noSymbolLabels = noSymbol.ConvertAll(o => o.Label);
            foreach (var row in Rows)
            {
                if (filter != null && !filter(row)) continue;
                row.ResolveCandidate = c => ResolveLabel(c.Label);
                var sig = row.Signature;
                // The category only filters when it was set by hand (the automatic guess can be wrong).
                var ranked = families.Count > 0 ? matcher.Rank(sig, row.BlockName, row.CategoryIsAuto ? null : row.Category, families) : new List<SymbolCandidate>();
                bool open = row.Family.IsSkip;

                // 1. A manual match remembered for this block and symbol.
                if (open)
                {
                    foreach (var m in Learned.Find(row.BlockName, sig.Hash()))
                    {
                        var opt = Resolve(m.Family, m.TypeName);
                        if (opt == null || opt.IsSkip) continue;
                        var same = ranked.FirstOrDefault(c => string.Equals(c.Label, opt.Label, StringComparison.OrdinalIgnoreCase));
                        var learned = new SymbolCandidate
                        {
                            Family = opt.Family, Label = opt.Label, Learned = true,
                            Score = same?.Score ?? 100, Geometry = same?.Geometry ?? 100, View = same?.View ?? "",
                        };
                        ranked.Remove(same);
                        ranked.Insert(0, learned);
                        if (ranked.Count > SymbolMatcher.TopCount) ranked.RemoveAt(ranked.Count - 1);
                        row.Candidates = ranked;
                        row.Family = opt;
                        row.AutoMatched = true;
                        UseFamilyCategory(row);
                        summary.Learned++;
                        open = false;
                        break;
                    }
                    if (!open) continue;
                }
                row.Candidates = ranked;
                if (!open) continue;

                // 2. Symbol geometry.
                if (!sig.IsEmpty && families.Count > 0)
                {
                    var d = matcher.Decide(ranked);
                    var opt = d.Chosen == null ? null : ResolveLabel(d.Chosen.Label);
                    if (opt == null)
                    {
                        // Families without a drawn symbol (or only a 3D outline) can still match by name.
                        if (ByName(row, noSymbol, noSymbolLabels)) summary.ByName++;
                        else if (d.Best != null) summary.Suggested++;
                        continue;
                    }
                    row.Family = opt;
                    row.AutoMatched = true;
                    row.MatchReview = d.Ambiguous ? SymbolMatcher.AmbiguousReason : "";
                    row.Candidates = ranked;   // refresh the score text with the review mark
                    UseFamilyCategory(row);
                    summary.BySymbol++;
                    if (d.Ambiguous) summary.Ambiguous++;
                    continue;
                }

                // 3. No symbol to compare: the block name.
                if (ByName(row, nameCandidates, labels)) summary.ByName++;
            }
            foreach (var row in Rows)
            {
                if (filter != null && !filter(row)) continue;
                var how = row.Family.IsSkip ? "unmatched" : !row.AutoMatched ? "kept (mapped before)" : "-> " + row.Family.Label + " [" + row.ScoreText + "]";
                var top = string.Join(" | ", row.Candidates.Select(c => c.Text + (c.View.Length > 0 ? " (" + c.View + ")" : "")));
                summary.Lines.Add($"{row.BlockName}\t{row.Signature}\t{how}\t{(top.Length > 0 ? top : "no family scored " + matcher.Weights.Minimum.ToString("0", CultureInfo.InvariantCulture) + "+")}");
            }
            return summary;
        }

        /// <summary>Name matching (no symbol to compare): selects the closest name among <paramref name="options"/>.</summary>
        static bool ByName(BlockRow row, List<FamilyOption> options, List<string> labels)
        {
            int i = FamilyMatcher.BestMatch(row.BlockName, labels, out _);
            if (i < 0) return false;
            row.Family = options[i];
            row.AutoMatched = true;
            UseFamilyCategory(row);
            return true;
        }

        /// <summary>Remembers every row whose family was picked by hand (not by Auto-match) for the
        /// next Auto-match. Returns how many rows were remembered.</summary>
        public int Learn()
        {
            if (Learned == null) Learned = new LearnedMatches();
            int n = 0;
            foreach (var row in Rows)
            {
                if (row.Family.IsSkip || row.AutoMatched) continue;
                Learned.Add(row.BlockName, row.Signature.Hash(), row.Family.Family, row.Family.TypeName);
                n++;
            }
            Learned.Save();
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

        /// <summary>After a Preview: show each block's detected host in the grid.</summary>
        public void SetDetectedHosts(IEnumerable<PlacementResult> results)
        {
            var byBlock = new Dictionary<string, List<PlacementResult>>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in results)
            {
                if (!r.HasBlock || r.Status == Status.Unmapped) continue;
                if (!byBlock.TryGetValue(r.BlockName, out var list)) byBlock[r.BlockName] = list = new List<PlacementResult>();
                list.Add(r);
            }
            foreach (var row in Rows)
                row.DetectedHost = byBlock.TryGetValue(row.BlockName, out var list) ? HostLabels.Summarize(list) : "";
        }

        public MappingResult ToMapping()
        {
            var result = new MappingResult { Slab = Slab.Clone(), HasSlabOptions = true };
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
