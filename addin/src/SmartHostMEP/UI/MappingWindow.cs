using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SmartHostMEP.Core;
using Microsoft.Win32;

namespace SmartHostMEP.UI
{
    /// <summary>How the mapping window was closed.</summary>
    public enum MappingAction
    {
        Cancel,
        /// <summary>Run finished inside the window.</summary>
        Done,
        /// <summary>Another DWG (or Include nested blocks) was chosen: read the blocks again.</summary>
        Reload,
        /// <summary>"Pick in view": click the DWG in Revit, then open the window again.</summary>
        PickDwg,
    }

    /// <summary>
    /// The mapping window, top to bottom: header (name, version, what is loaded), setup row (DWG,
    /// Level, Host default), toolbar (search, All / Mapped / Unmapped, Auto-match, Columns), the grid
    /// (one row per unique CAD block), live summary or progress bar, Advanced (closed by default) and
    /// the footer (Load / Save; Preview, Run, Cancel). Preview and Run happen inside the window with
    /// its own progress bar. Built in code (styles in <see cref="Theme"/>), so it compiles on any SDK.
    /// </summary>
    public class MappingWindow : Window
    {
        readonly MappingSession _s;
        readonly Settings _settings;
        readonly DataGrid _grid = new DataGrid();

        public MappingAction Action { get; private set; } = MappingAction.Cancel;

        const double S = Theme.Space;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public MappingWindow(MappingSession session)
        {
            _s = session;
            _settings = session.Settings ?? new Settings();
            Theme.Apply(this);
            Title = "SmartHost MEP";
            Width = 1320;
            Height = 800;
            MinWidth = 980;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            RestorePlacement();

            var root = new Grid { Margin = new Thickness(2 * S) };
            for (int i = 0; i < 8; i++)
                root.RowDefinitions.Add(new RowDefinition { Height = i == 4 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Add(root, BuildHeader(), 0);
            Add(root, BuildSetupRow(), 1);
            Add(root, BuildToolbar(), 2);
            Add(root, BuildSelectionBar(), 3);
            Add(root, BuildBody(), 4);
            Add(root, BuildSummaryRow(), 5);
            Add(root, BuildAdvanced(), 6);
            Add(root, BuildFooter(), 7);
            Content = root;

            PreviewKeyDown += OnWindowKey;
            Closing += (o, e) =>
            {
                if (_busy) { e.Cancel = true; _cancelRequested = true; return; }
                SavePlacement();
            };
            foreach (var row in _s.Rows) row.PropertyChanged += OnRowChanged;
            Closed += (o, e) => { foreach (var row in _s.Rows) row.PropertyChanged -= OnRowChanged; };
            Loaded += (o, e) => { _grid.Focus(); UpdateStatus(); };
            UpdateStatus();
        }

        static void Add(Grid g, UIElement child, int row)
        {
            Grid.SetRow(child, row);
            g.Children.Add(child);
        }

        // ---- 1. Header ---------------------------------------------------------------------

        readonly TextBlock _headerStatus = Theme.Text("", Theme.LabelSize, muted: true);

        UIElement BuildHeader()
        {
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(Theme.Text("SmartHost MEP", Theme.TitleSize, bold: true));
            if (!string.IsNullOrEmpty(_s.Version))
                title.Children.Add(Badge("v" + _s.Version, accent: true, margin: new Thickness(S, 2, 0, 0)));
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 1.5 * S) };
            stack.Children.Add(title);
            _headerStatus.Margin = new Thickness(0, S / 2, 0, 0);
            stack.Children.Add(_headerStatus);
            UpdateHeader();
            return stack;
        }

        void UpdateHeader()
        {
            int instances = _s.Rows.Sum(r => r.Count);
            _headerStatus.Text = $"DWG: {_s.DwgName}  |  Level: {_s.LevelName}  |  {_s.Rows.Count} block types, {instances:N0} instances";
        }

        /// <summary>Small rounded label ("v0.35.0", a block count).</summary>
        static Border Badge(string text, bool accent = false, Thickness? margin = null)
        {
            var t = new TextBlock { Text = text, FontSize = Theme.SmallSize, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, accent ? "Sh.Accent" : "Sh.Muted");
            var b = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1, 7, 1), Child = t, VerticalAlignment = VerticalAlignment.Center };
            b.SetResourceReference(Border.BackgroundProperty, accent ? "Sh.AccentSoft" : "Sh.Surface2");
            if (margin.HasValue) b.Margin = margin.Value;
            return b;
        }

        // ---- 2. Setup row: DWG, Level, Host default -----------------------------------------

        readonly ComboBox _dwg = new ComboBox { Width = 380 };
        readonly ComboBox _level = new ComboBox { Width = 200 };
        readonly ComboBox _hostDefault = new ComboBox { Width = 220 };
        bool _setupReady;

        UIElement BuildSetupRow()
        {
            var bar = new WrapPanel();
            bar.Children.Add(Field("DWG", _dwg, "The DWG link or import whose blocks are listed. Changing it reads the blocks of the other DWG (families picked for blocks with the same name are kept)."));
            var pick = Theme.Button(this, "Pick in view", (o, e) => { Action = MappingAction.PickDwg; Close(); },
                "Click the DWG in the Revit view instead of choosing it from the list (Esc in Revit comes back here)");
            pick.VerticalAlignment = VerticalAlignment.Bottom;
            pick.Margin = new Thickness(0, 0, 3 * S, 0);
            bar.Children.Add(pick);
            bar.Children.Add(Field("Level", _level, "Level the rows are placed on (Elevation is measured from it). Rows set to another level in the Level column keep theirs."));
            bar.Children.Add(Field("Host default", _hostDefault, "Host Type of new rows. Changing it also changes every row that still has the previous default."));

            foreach (var d in _s.DwgNames) _dwg.Items.Add(d);
            if (_s.DwgNames.Count > 0) _dwg.SelectedIndex = Math.Max(0, Math.Min(_s.DwgIndex, _s.DwgNames.Count - 1));
            _dwg.SelectionChanged += (o, e) =>
            {
                if (!_setupReady || _dwg.SelectedIndex < 0 || _dwg.SelectedIndex == _s.DwgIndex) return;
                _s.DwgIndex = _dwg.SelectedIndex;
                Action = MappingAction.Reload;
                Close();
            };
            foreach (var l in _s.LevelNames) _level.Items.Add(l);
            _level.SelectedItem = _s.LevelName;
            _level.SelectionChanged += (o, e) =>
            {
                if (!_setupReady || !(_level.SelectedItem is string lvl)) return;
                CommitEdits();
                _s.LevelName = lvl;
                foreach (var r in _s.Rows) r.SetDefaultLevel(lvl);
                UpdateHeader();
            };
            foreach (var h in BlockRow.HostChoices) _hostDefault.Items.Add(h);
            _hostDefault.SelectedItem = HostDefault;
            _hostDefault.SelectionChanged += (o, e) =>
            {
                if (!_setupReady || !(_hostDefault.SelectedItem is string host)) return;
                CommitEdits();
                var old = HostDefault;
                foreach (var r in _s.Rows) if (r.Host == old) r.Host = host;
                _settings.DefaultHost = host;
                Settings.TrySave(_settings);
            };
            _setupReady = true;
            var card = Theme.Card(bar, new Thickness(1.5 * S, S, 1.5 * S, S));
            card.Margin = new Thickness(0, 0, 0, 1.5 * S);
            return card;
        }

        string HostDefault =>
            BlockRow.HostChoices.Contains(_settings.DefaultHost) ? _settings.DefaultHost : BlockRow.HostChoices[0];

        /// <summary>Label above a control, both with the same tooltip.</summary>
        static FrameworkElement Field(string label, FrameworkElement control, string tooltip, double right = 3 * S)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, right, S / 2) };
            var t = Theme.Text(label, Theme.SmallSize, muted: true);
            t.Margin = new Thickness(0, 0, 0, S / 2);
            t.ToolTip = tooltip;
            control.ToolTip = tooltip;
            stack.Children.Add(t);
            stack.Children.Add(control);
            return stack;
        }

        // ---- 3. Toolbar: search, chips, Auto-match, Columns ---------------------------------

        readonly TextBox _search = new TextBox { Width = 280 };
        readonly RadioButton _chipAll = new RadioButton { IsChecked = true }, _chipMapped = new RadioButton(), _chipUnmapped = new RadioButton();
        enum RowFilter { All, Mapped, Unmapped }
        RowFilter _show = RowFilter.All;

        UIElement BuildToolbar()
        {
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, S), LastChildFill = false };
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            // Search box with a placeholder.
            var hint = Theme.Text("Search blocks   (Ctrl+F)", Theme.LabelSize, muted: true);
            hint.IsHitTestVisible = false;
            hint.Margin = new Thickness(S + 2, 0, 0, 0);
            var searchHost = new Grid { Margin = new Thickness(0, 0, 2 * S, 0) };
            searchHost.Children.Add(_search);
            searchHost.Children.Add(hint);
            _search.ToolTip = "Show only blocks whose name (or family) contains these words";
            _search.TextChanged += (o, e) =>
            {
                hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                ApplyFilter();
            };
            left.Children.Add(searchHost);
            var chipStyle = Theme.Named(this, "Sh.Chip");
            foreach (var (chip, which, tip) in new[]
            {
                (_chipAll, RowFilter.All, "Show every block"),
                (_chipMapped, RowFilter.Mapped, "Show only blocks that have a family"),
                (_chipUnmapped, RowFilter.Unmapped, "Show only blocks still on (Skip)"),
            })
            {
                if (chipStyle != null) chip.Style = chipStyle;
                chip.GroupName = "show";
                chip.ToolTip = tip;
                chip.VerticalAlignment = VerticalAlignment.Center;
                var w = which;
                chip.Checked += (o, e) => { _show = w; ApplyFilter(); };
                left.Children.Add(chip);
            }
            DockPanel.SetDock(left, Dock.Left);
            bar.Children.Add(left);

            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(Theme.Button(this, "Auto-match", (o, e) => AutoMatch(),
                "Pick families for rows still on (Skip): your earlier manual choices first, then the family whose symbol looks the same, then the name. Rows already mapped are not changed."));
            var columns = Theme.Button(this, "Columns ▾", null, "Show or hide the optional columns");
            columns.Margin = new Thickness(0);
            columns.Click += (o, e) => { var m = BuildColumnsMenu(); m.PlacementTarget = columns; m.Placement = PlacementMode.Bottom; m.IsOpen = true; };
            right.Children.Add(columns);
            DockPanel.SetDock(right, Dock.Right);
            bar.Children.Add(right);
            return bar;
        }

        // ---- 3b. Selection bar (2+ rows selected): Family / Host / Elevation for all -------

        readonly Border _selectionBar = new Border();
        readonly TextBlock _selCount = Theme.Text("", Theme.LabelSize, bold: true);
        FilterComboBox _selFamily;
        readonly ComboBox _selHost = new ComboBox { Width = 200 };
        readonly TextBox _selElevation = new TextBox { Width = 90 };
        const string Keep = "(keep)";
        static readonly FamilyOption KeepFamily = new FamilyOption { Label = Keep };

        UIElement BuildSelectionBar()
        {
            var keepFamilies = new System.Collections.ObjectModel.ObservableCollection<FamilyOption> { KeepFamily };
            foreach (var o in _s.Options) keepFamilies.Add(o);
            _s.Options.CollectionChanged += (o, e) =>
            {
                if (e.NewItems != null) foreach (FamilyOption f in e.NewItems) if (!keepFamilies.Contains(f)) keepFamilies.Add(f);
            };
            _selFamily = new FilterComboBox { Width = 340, ItemsSource = new ListCollectionView(keepFamilies), SelectedItem = KeepFamily };
            _selHost.Items.Add(Keep);
            foreach (var h in BlockRow.HostChoices) _selHost.Items.Add(h);
            _selHost.SelectedIndex = 0;
            _selElevation.ToolTip = "Elevation From Level (mm) for the selected rows; leave empty to keep each row's value";

            var bar = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            _selCount.Margin = new Thickness(0, 0, 2 * S, 0);
            _selCount.SetResourceReference(TextBlock.ForegroundProperty, "Sh.Accent");
            bar.Children.Add(_selCount);
            bar.Children.Add(Inline("Family", _selFamily));
            bar.Children.Add(Inline("Host", _selHost));
            bar.Children.Add(Inline("Elevation (mm)", _selElevation));
            bar.Children.Add(Theme.Button(this, "Apply to selected", (o, e) => ApplySelectionBar(), "Set the chosen values on every selected row; (keep) fields are not changed", primary: true));
            bar.Children.Add(Theme.Button(this, "Skip selected", (o, e) => SkipSelected(), "Set every selected row to (Skip)"));
            _selectionBar.Child = bar;
            _selectionBar.CornerRadius = new CornerRadius(6);
            _selectionBar.Padding = new Thickness(1.5 * S, S, 1.5 * S, S);
            _selectionBar.Margin = new Thickness(0, 0, 0, S);
            _selectionBar.BorderThickness = new Thickness(1);
            _selectionBar.SetResourceReference(Border.BackgroundProperty, "Sh.AccentSoft");
            _selectionBar.SetResourceReference(Border.BorderBrushProperty, "Sh.Accent");
            _selectionBar.Visibility = Visibility.Collapsed;
            return _selectionBar;
        }

        static UIElement Inline(string label, FrameworkElement control)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 2 * S, 0) };
            var t = Theme.Text(label, Theme.LabelSize, muted: true);
            t.Margin = new Thickness(0, 0, S, 0);
            p.Children.Add(t);
            p.Children.Add(control);
            return p;
        }

        void UpdateSelectionBar()
        {
            int n = _grid.SelectedItems.Count;
            _selectionBar.Visibility = n >= 2 && !_busy ? Visibility.Visible : Visibility.Collapsed;
            _selCount.Text = $"{n} rows selected";
        }

        void ApplySelectionBar()
        {
            CommitEdits();
            var rows = _grid.SelectedItems.OfType<BlockRow>().ToList();
            var family = _selFamily.SelectedItem as FamilyOption;
            var host = _selHost.SelectedItem as string;
            var elev = (_selElevation.Text ?? "").Trim();
            if (elev.Length > 0 && Mapping.ParseNumber(elev) == null)
            {
                MarkInvalid(_selElevation, "Elevation must be a number (mm), or empty to keep each row's value");
                return;
            }
            MarkInvalid(_selElevation, null);
            foreach (var r in rows)
            {
                if (family != null && family != KeepFamily) { r.Family = family; MappingSession.UseFamilyCategory(r); }
                if (host != null && host != Keep) r.Host = host;
                if (elev.Length > 0) r.Elevation = elev;
            }
            _selFamily.SelectedItem = KeepFamily;
            _selHost.SelectedIndex = 0;
            _selElevation.Text = "";
        }

        void SkipSelected()
        {
            CommitEdits();
            foreach (var r in _grid.SelectedItems.OfType<BlockRow>().ToList()) r.Family = FamilyOption.Skip;
        }

        // ---- 4. Body: grid (+ optional symbol preview) ---------------------------------------

        readonly Dictionary<string, DataGridColumn> _cols = new Dictionary<string, DataGridColumn>();
        /// <summary>Optional columns (Columns menu), hidden by default: key, menu text.</summary>
        static readonly (string key, string text)[] Optional =
        {
            ("rotation", "Rotation"), ("facing", "Facing"), ("level", "Level"), ("category", "Category"), ("placeat", "Place At"),
            ("score", "Auto-match score"), ("sourceview", "Source view"), ("top3", "Top 3 candidates"), ("detected", "Detected host (Preview)"),
        };
        readonly HashSet<string> _shown = new HashSet<string>();
        Border _previewPanel;
        bool _showPreviewPanel;

        UIElement BuildBody()
        {
            foreach (var k in (_settings.ShownColumns ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) _shown.Add(k.Trim());
            _showPreviewPanel = _shown.Contains("preview");
            SetUpRowView();
            BuildGrid();
            var body = new DockPanel();
            _previewPanel = BuildPreviewPanel();
            _previewPanel.Visibility = _showPreviewPanel ? Visibility.Visible : Visibility.Collapsed;
            DockPanel.SetDock(_previewPanel, Dock.Right);
            body.Children.Add(_previewPanel);
            var frame = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = _grid, ClipToBounds = true };
            frame.SetResourceReference(Border.BorderBrushProperty, "Sh.Border");
            frame.SetResourceReference(Border.BackgroundProperty, "Sh.Bg");
            body.Children.Add(frame);
            return body;
        }

        ContextMenu BuildColumnsMenu()
        {
            var menu = new ContextMenu();
            foreach (var (key, text) in Optional)
            {
                var k = key;
                var item = new MenuItem { Header = text, IsCheckable = true, IsChecked = _shown.Contains(k) };
                item.Click += (o, e) => SetColumnShown(k, item.IsChecked);
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var preview = new MenuItem { Header = "Symbol preview panel", IsCheckable = true, IsChecked = _showPreviewPanel };
            preview.Click += (o, e) =>
            {
                _showPreviewPanel = preview.IsChecked;
                _previewPanel.Visibility = _showPreviewPanel ? Visibility.Visible : Visibility.Collapsed;
                if (_showPreviewPanel) _shown.Add("preview"); else _shown.Remove("preview");
                SaveShown();
            };
            menu.Items.Add(preview);
            return menu;
        }

        void SetColumnShown(string key, bool shown)
        {
            if (shown) _shown.Add(key); else _shown.Remove(key);
            if (_cols.TryGetValue(key, out var col)) col.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            SaveShown();
        }

        void SaveShown()
        {
            _settings.ShownColumns = string.Join(",", _shown);
            Settings.TrySave(_settings);
        }

        DataGridColumn Col(string key, DataGridColumn col, string tooltip)
        {
            var header = Theme.Text(col.Header as string ?? "", Theme.SmallSize);
            header.FontWeight = FontWeights.SemiBold;
            header.ToolTip = tooltip;
            col.Header = header;
            _cols[key] = col;
            if (Optional.Any(o => o.key == key)) col.Visibility = _shown.Contains(key) ? Visibility.Visible : Visibility.Collapsed;
            _grid.Columns.Add(col);
            return col;
        }

        void BuildGrid()
        {
            _grid.AutoGenerateColumns = false;
            _grid.CanUserAddRows = false;
            _grid.CanUserDeleteRows = false;
            _grid.CanUserReorderColumns = false;
            _grid.CanUserResizeRows = false;
            _grid.SelectionMode = DataGridSelectionMode.Extended;
            _grid.SelectionUnit = DataGridSelectionUnit.FullRow;
            _grid.SelectionChanged += (o, e) => { UpdateSelectionBar(); ShowPreview(null); };
            _grid.PreviewMouseLeftButtonDown += OnGridMouseDown;
            _grid.PreviewMouseRightButtonDown += OnGridRightMouseDown;
            _grid.PreviewKeyDown += (o, e) => { if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.PageUp || e.Key == Key.PageDown) _editGroup = null; };
            _grid.BeginningEdit += (o, e) => _editing = true;
            _grid.CellEditEnding += (o, e) => _editing = false;
            _grid.MouseLeave += (o, e) => ShowPreview(null);
            _grid.RowHeight = 36;
            _grid.RowStyle = RowStyle();
            _grid.EnableRowVirtualization = true;
            _grid.ContextMenu = BuildRowMenu();
            VirtualizingPanel.SetVirtualizationMode(_grid, VirtualizationMode.Recycling);
            VirtualizingPanel.SetIsVirtualizingWhenGrouping(_grid, true);
            _grid.ItemsSource = _s.Rows;
            _grid.GroupStyle.Add(new GroupStyle { HeaderTemplate = GroupHeaderTemplate() });

            // Block: name + count badge (tooltip: full name and symbol).
            var blockCell = new FrameworkElementFactory(typeof(DockPanel));
            blockCell.SetValue(DockPanel.LastChildFillProperty, true);
            blockCell.SetValue(FrameworkElement.ToolTipProperty, new Binding(nameof(BlockRow.SymbolToolTip)));
            blockCell.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
            var badge = new FrameworkElementFactory(typeof(Border));
            badge.SetValue(DockPanel.DockProperty, Dock.Right);
            badge.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            badge.SetValue(Border.PaddingProperty, new Thickness(7, 0, 7, 1));
            badge.SetValue(FrameworkElement.MarginProperty, new Thickness(S, 0, 0, 0));
            badge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            badge.SetResourceReference(Border.BackgroundProperty, "Sh.Surface2");
            var count = new FrameworkElementFactory(typeof(TextBlock));
            count.SetBinding(TextBlock.TextProperty, new Binding(nameof(BlockRow.Count)) { Mode = BindingMode.OneWay, StringFormat = "{0:N0}" });
            count.SetValue(TextBlock.FontSizeProperty, Theme.SmallSize);
            count.SetResourceReference(TextBlock.ForegroundProperty, "Sh.Muted");
            badge.AppendChild(count);
            var name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetBinding(TextBlock.TextProperty, new Binding(nameof(BlockRow.BlockName)) { Mode = BindingMode.OneWay });
            name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            name.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            blockCell.AppendChild(badge);
            blockCell.AppendChild(name);
            Col("block", new DataGridTemplateColumn
            {
                Header = "Block",
                CellTemplate = new DataTemplate { VisualTree = blockCell },
                SortMemberPath = nameof(BlockRow.BlockName),
                IsReadOnly = true,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 220,
            }, "CAD block name and how many times it is in the DWG. Hover to see its symbol.");

            // Revit Family : Type (searchable).
            var combo = new FrameworkElementFactory(typeof(FilterComboBox));
            combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(BlockRow.Options)) { Mode = BindingMode.OneWay });
            combo.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(BlockRow.Family)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            combo.SetValue(FrameworkElement.HeightProperty, 28.0);
            combo.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1));
            Col("family", new DataGridTemplateColumn
            {
                Header = "Revit Family : Type",
                CellTemplate = new DataTemplate { VisualTree = combo },
                SortMemberPath = nameof(BlockRow.FamilyLabel),
                Width = new DataGridLength(420),
                MinWidth = 260,
            }, "The family type placed for this block. Type to search. (Skip) = not placed.");

            Col("host", ComboColumn("Host", BlockRow.HostChoices, nameof(BlockRow.Host), 210),
                "What the family is hosted on: none (level), ceiling, slab above/below, wall, reference plane, face or vertical plane.");
            Col("elevation", NumberColumn("Elevation (mm)", nameof(BlockRow.Elevation), nameof(BlockRow.ElevationInvalid), nameof(BlockRow.ElevationError), 120),
                "Height of the family above the row's level, in mm.");

            Col("rotation", NumberColumn("Rotation (deg)", nameof(BlockRow.Rotation), nameof(BlockRow.RotationInvalid), nameof(BlockRow.RotationError), 110),
                "Extra rotation added to the block's own rotation, in degrees.");
            Col("facing", ComboColumn("Facing", BlockRow.FacingChoices, nameof(BlockRow.Facing), 90),
                "Reference Plane host: Down for ceiling devices, Up for floor devices.");
            Col("level", ComboColumn("Level", _s.LevelNames, nameof(BlockRow.Level), 150),
                "Level of this row (the Level above, unless changed here).");
            Col("category", ComboColumn("Category", BlockCategories.All, nameof(BlockRow.Category), 120),
                "Discipline of the block, used for grouping (guessed from its name).");
            Col("placeat", ComboColumn("Place At", BlockRow.PlaceAtChoices, nameof(BlockRow.PlaceAt), 120),
                "Symbol centre (default) or the block's base point.");
            Col("score", TextColumn("Score", nameof(BlockRow.ScoreText), 70, center: true),
                "Auto-match score (0-100) of the selected family. (n) = best candidate, not selected. ? = ambiguous. Learned = your earlier choice.");
            Col("sourceview", TextColumn("Source View", nameof(BlockRow.SourceView), 110),
                "The family view whose symbol matched.");
            var top = new FrameworkElementFactory(typeof(ComboBox));
            top.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(BlockRow.Candidates)) { Mode = BindingMode.OneWay });
            top.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(BlockRow.SelectedCandidate)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            top.SetValue(ItemsControl.DisplayMemberPathProperty, nameof(SymbolCandidate.Text));
            top.SetValue(FrameworkElement.HeightProperty, 28.0);
            Col("top3", new DataGridTemplateColumn
            {
                Header = "Top 3 Candidates",
                CellTemplate = new DataTemplate { VisualTree = top },
                Width = new DataGridLength(260),
            }, "The best families by symbol after Auto-match (score  Family : Type). Pick one to use it.");
            Col("detected", TextColumn("Detected Host", nameof(BlockRow.DetectedHost), 280),
                "Filled by Preview: the host each block would use.");
            RestoreColumnWidths();
        }

        DataGridColumn ComboColumn(string header, System.Collections.IEnumerable items, string property, double width)
        {
            var style = new Style(typeof(ComboBox), TryFindResource(typeof(ComboBox)) as Style);
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 28.0));
            return new DataGridComboBoxColumn
            {
                Header = header,
                ItemsSource = items,
                SelectedItemBinding = new Binding(property) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = new DataGridLength(width),
                EditingElementStyle = style,
            };
        }

        static DataGridColumn TextColumn(string header, string property, double width, bool center = false)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            if (center) style.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(property)));
            return new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(property) { Mode = BindingMode.OneWay },
                IsReadOnly = true,
                Width = new DataGridLength(width),
                ElementStyle = style,
            };
        }

        /// <summary>Number column with inline validation: red border and the problem as tooltip.</summary>
        DataGridColumn NumberColumn(string header, string property, string invalidProperty, string errorProperty, double width)
        {
            var element = new Style(typeof(TextBlock));
            element.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right));
            element.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            var editing = new Style(typeof(TextBox), TryFindResource(typeof(TextBox)) as Style);
            editing.Setters.Add(new Setter(FrameworkElement.HeightProperty, 28.0));
            editing.Setters.Add(new Setter(TextBox.TextAlignmentProperty, TextAlignment.Right));
            var cell = new Style(typeof(DataGridCell), TryFindResource(typeof(DataGridCell)) as Style);
            var bad = new DataTrigger { Binding = new Binding(invalidProperty), Value = true };
            bad.Setters.Add(new Setter(Control.BorderBrushProperty, TryFindResource("Sh.Danger") ?? Brushes.Firebrick));
            bad.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1.5)));
            bad.Setters.Add(new Setter(Control.BackgroundProperty, TryFindResource("Sh.DangerSoft") ?? Brushes.MistyRose));
            bad.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(errorProperty)));
            cell.Triggers.Add(bad);
            return new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(property) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                ElementStyle = element,
                EditingElementStyle = editing,
                CellStyle = cell,
                Width = new DataGridLength(width),
            };
        }

        /// <summary>Selected rows highlighted; (Skip) rows greyed out; hovering shows the symbol in the preview panel.</summary>
        Style RowStyle()
        {
            var style = new Style(typeof(DataGridRow));
            style.Setters.Add(new EventSetter(UIElement.MouseEnterEvent, new MouseEventHandler((o, e) => ShowPreview((o as DataGridRow)?.Item as BlockRow))));
            var skipped = new DataTrigger { Binding = new Binding(nameof(BlockRow.IsSkipped)), Value = true };
            skipped.Setters.Add(new Setter(UIElement.OpacityProperty, 0.55));
            style.Triggers.Add(skipped);
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, TryFindResource("Sh.Hover")));
            style.Triggers.Add(hover);
            var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, TryFindResource("Sh.Selection")));
            selected.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0));
            style.Triggers.Add(selected);
            return style;
        }

        /// <summary>Group header: "ELECTRICAL  23".</summary>
        static DataTemplate GroupHeaderTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetResourceReference(Border.BackgroundProperty, "Sh.Surface");
            border.SetValue(Border.PaddingProperty, new Thickness(S, S / 2, S, S / 2));
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            var name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            name.SetValue(TextBlock.FontSizeProperty, Theme.SmallSize);
            name.SetResourceReference(TextBlock.ForegroundProperty, "Sh.Accent");
            var count = new FrameworkElementFactory(typeof(TextBlock));
            count.SetBinding(TextBlock.TextProperty, new Binding("ItemCount") { StringFormat = "   {0} block type(s)" });
            count.SetValue(TextBlock.FontSizeProperty, Theme.SmallSize);
            count.SetResourceReference(TextBlock.ForegroundProperty, "Sh.Muted");
            panel.AppendChild(name);
            panel.AppendChild(count);
            border.AppendChild(panel);
            return new DataTemplate { VisualTree = border };
        }

        void SetUpRowView()
        {
            var view = CollectionViewSource.GetDefaultView(_s.Rows);
            view.Filter = null;
            view.GroupDescriptions.Clear();
            view.SortDescriptions.Clear();
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BlockRow.Category)));
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(BlockRow.CategoryOrder), System.ComponentModel.ListSortDirection.Ascending));
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(BlockRow.BlockName), System.ComponentModel.ListSortDirection.Ascending));
            if (view is System.ComponentModel.ICollectionViewLiveShaping live)
            {
                if (live.CanChangeLiveGrouping)
                {
                    live.LiveGroupingProperties.Clear();
                    live.LiveGroupingProperties.Add(nameof(BlockRow.Category));
                    live.IsLiveGrouping = true;
                }
                if (live.CanChangeLiveSorting)
                {
                    live.LiveSortingProperties.Clear();
                    live.LiveSortingProperties.Add(nameof(BlockRow.CategoryOrder));
                    live.IsLiveSorting = true;
                }
                if (live.CanChangeLiveFiltering)
                {
                    // Mapped / Unmapped chips: a row leaves the view when its family changes.
                    live.LiveFilteringProperties.Clear();
                    live.LiveFilteringProperties.Add(nameof(BlockRow.IsSkipped));
                }
            }
        }

        void ApplyFilter()
        {
            CommitEdits();   // changing the filter during a cell edit throws
            var view = CollectionViewSource.GetDefaultView(_s.Rows);
            var terms = (_search.Text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var show = _show;
            if (view is System.ComponentModel.ICollectionViewLiveShaping live && live.CanChangeLiveFiltering)
                live.IsLiveFiltering = show != RowFilter.All;
            view.Filter = terms.Length == 0 && show == RowFilter.All ? null : (Predicate<object>)(o =>
            {
                var r = (BlockRow)o;
                if (show == RowFilter.Mapped && r.IsSkipped) return false;
                if (show == RowFilter.Unmapped && !r.IsSkipped) return false;
                var hay = r.BlockName + " " + r.FamilyLabel;
                return terms.All(t => hay.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
            });
        }

        // ---- Symbol preview panel (optional) ----------------------------------------------

        readonly Image _previewImage = new Image { Width = 200, Height = 200, Stretch = Stretch.Uniform };
        readonly TextBlock _previewName = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, S, 0, 2) };
        readonly TextBlock _previewInfo = new TextBlock { TextWrapping = TextWrapping.Wrap };
        readonly TextBlock _previewEmpty = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(1.5 * S),
        };

        Border BuildPreviewPanel()
        {
            _previewInfo.SetResourceReference(TextBlock.ForegroundProperty, "Sh.Muted");
            _previewEmpty.SetResourceReference(TextBlock.ForegroundProperty, "Sh.Muted");
            var frame = new Grid { Width = 216, Height = 216 };
            frame.Children.Add(_previewImage);
            frame.Children.Add(_previewEmpty);
            var framed = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = frame };
            framed.SetResourceReference(Border.BorderBrushProperty, "Sh.Border");
            framed.SetResourceReference(Border.BackgroundProperty, "Sh.Bg");
            var stack = new StackPanel();
            stack.Children.Add(Theme.Text("SYMBOL PREVIEW", Theme.SmallSize, muted: true, bold: true));
            framed.Margin = new Thickness(0, S, 0, 0);
            stack.Children.Add(framed);
            stack.Children.Add(_previewName);
            stack.Children.Add(_previewInfo);
            ShowPreview(null);
            var card = Theme.Card(stack, new Thickness(1.5 * S));
            card.Width = 252;
            card.Margin = new Thickness(S, 0, 0, 0);
            return card;
        }

        void ShowPreview(BlockRow row)
        {
            if (!_showPreviewPanel) return;
            row = row ?? _grid.SelectedItem as BlockRow;
            if (row == null)
            {
                _previewImage.Source = null;
                _previewEmpty.Text = "Hover over or select a block to see its 2D symbol.";
                _previewEmpty.Visibility = Visibility.Visible;
                _previewName.Text = "";
                _previewInfo.Text = "";
                return;
            }
            var img = row.SymbolImage;
            _previewImage.Source = img;
            _previewEmpty.Text = "No line work to preview\n(the block may contain only text, hatches or solids).";
            _previewEmpty.Visibility = img == null ? Visibility.Visible : Visibility.Collapsed;
            _previewName.Text = row.BlockName;
            var size = row.Symbol?.SizeText() ?? "";
            _previewInfo.Text = $"{row.Count} instance(s)  ·  {row.Category}" + (size.Length > 0 ? $"\nSize: {size}" : "") +
                                (row.IsSkipped ? "\nNot mapped (Skip)" : "\n" + row.FamilyLabel);
        }

        // ---- Right-click menu --------------------------------------------------------------

        BlockRow _menuRow;
        BlockRow _copied;
        MenuItem _paste;

        ContextMenu BuildRowMenu()
        {
            var menu = new ContextMenu();
            MenuItem Item(string text, Action act, string gesture = null)
            {
                var m = new MenuItem { Header = text, InputGestureText = gesture ?? "" };
                m.Click += (o, e) => { CommitEdits(); act(); };
                menu.Items.Add(m);
                return m;
            }
            Item("Apply this row to selected", () =>
            {
                if (_menuRow == null) return;
                foreach (var r in _grid.SelectedItems.OfType<BlockRow>().ToList())
                {
                    if (ReferenceEquals(r, _menuRow)) continue;
                    r.Family = _menuRow.Family;
                    r.Host = _menuRow.Host;
                    r.Elevation = _menuRow.Elevation;
                    MappingSession.UseFamilyCategory(r);
                }
            });
            Item("Skip selected", SkipSelected, "Del");
            menu.Items.Add(new Separator());
            Item("Copy settings from this row", () => { _copied = _menuRow; }, "Ctrl+C");
            _paste = Item("Paste settings to selected", PasteSettings, "Ctrl+V");
            menu.Items.Add(new Separator());
            Item("Select all shown", () => { _grid.Focus(); _grid.SelectAll(); }, "Ctrl+A");
            Item("Clear all families", ClearFamilies);
            menu.Opened += (o, e) =>
            {
                _paste.IsEnabled = _copied != null;
                _paste.Header = _copied != null ? $"Paste settings of '{Short(_copied.BlockName)}' to selected" : "Paste settings to selected";
            };
            return menu;
        }

        static string Short(string s) => s.Length > 30 ? s.Substring(0, 28) + "..." : s;

        /// <summary>Host, Elevation, Rotation, Facing, Level and Place At of the copied row (not its family).</summary>
        void PasteSettings()
        {
            if (_copied == null) return;
            foreach (var r in _grid.SelectedItems.OfType<BlockRow>().ToList())
            {
                if (ReferenceEquals(r, _copied)) continue;
                r.Host = _copied.Host;
                r.Elevation = _copied.Elevation;
                r.Rotation = _copied.Rotation;
                r.Facing = _copied.Facing;
                r.Level = _copied.Level;
                r.PlaceAt = _copied.PlaceAt;
            }
        }

        void OnGridRightMouseDown(object sender, MouseButtonEventArgs e)
        {
            var rowEl = Ancestor<DataGridRow>(e.OriginalSource as DependencyObject);
            _menuRow = rowEl?.Item as BlockRow;
            if (_menuRow != null && !_grid.SelectedItems.Contains(_menuRow))
            {
                _grid.SelectedItems.Clear();
                _grid.SelectedItems.Add(_menuRow);
            }
        }

        // ---- 5. Summary / progress -----------------------------------------------------------

        readonly TextBlock _summary = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _message = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2 * S, 0, 0, 0) };
        readonly Grid _summaryRow = new Grid { Margin = new Thickness(0, S, 0, S), MinHeight = 32 };
        readonly DockPanel _summaryPanel = new DockPanel();
        readonly DockPanel _progressPanel = new DockPanel { Visibility = Visibility.Collapsed };
        readonly ProgressBar _bar = new ProgressBar { Minimum = 0, Maximum = 1000, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _progressText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2 * S, 0), MinWidth = 260 };
        Button _progressCancel;

        UIElement BuildSummaryRow()
        {
            _summaryPanel.Children.Add(_summary);
            _summaryPanel.Children.Add(_message);
            _progressCancel = Theme.Button(this, "Cancel", (o, e) => { _cancelRequested = true; _progressCancel.IsEnabled = false; _progressText.Text = "Stopping..."; },
                "Stop: everything done so far is rolled back");
            _progressCancel.Margin = new Thickness(2 * S, 0, 0, 0);
            DockPanel.SetDock(_progressText, Dock.Left);
            DockPanel.SetDock(_progressCancel, Dock.Right);
            _progressPanel.Children.Add(_progressText);
            _progressPanel.Children.Add(_progressCancel);
            _progressPanel.Children.Add(_bar);
            _summaryRow.Children.Add(_summaryPanel);
            _summaryRow.Children.Add(_progressPanel);
            return _summaryRow;
        }

        void UpdateStatus()
        {
            int mapped = _s.Rows.Count(r => !r.Family.IsSkip);
            int skipped = _s.Rows.Count - mapped;
            int elements = _s.Rows.Where(r => !r.Family.IsSkip).Sum(r => r.Count);
            int bad = _s.Rows.Count(r => r.HasError);
            _summary.Inlines.Clear();
            _summary.Inlines.Add(new Run($"{mapped} mapped") { FontWeight = FontWeights.SemiBold });
            _summary.Inlines.Add(new Run($", {skipped} skipped, "));
            _summary.Inlines.Add(new Run($"{elements:N0} elements will be placed") { FontWeight = FontWeights.SemiBold });
            if (bad > 0)
            {
                var r = new Run($"   ·   {bad} row(s) with an invalid number (red cells)");
                r.SetResourceReference(TextElement.ForegroundProperty, "Sh.Danger");
                _summary.Inlines.Add(r);
            }
            _chipAll.Content = $"All  {_s.Rows.Count}";
            _chipMapped.Content = $"Mapped  {mapped}";
            _chipUnmapped.Content = $"Unmapped  {skipped}";

            var advancedProblem = ReadAdvanced();
            string block = bad > 0 ? "Fix the red cells first."
                         : mapped == 0 ? "Pick a family for at least one block."
                         : advancedProblem;
            if (_preview != null)
            {
                bool ok = block == null && !_busy;
                _preview.IsEnabled = ok;
                _run.IsEnabled = ok;
                _footerHint.Text = block ?? "";
            }
        }

        void ShowMessage(string text, string colorKey = "Sh.Muted")
        {
            _message.Text = text ?? "";
            _message.SetResourceReference(TextBlock.ForegroundProperty, colorKey);
        }

        // ---- 6. Advanced ---------------------------------------------------------------------

        readonly TextBox _slabRange = new TextBox { Width = 100 };
        readonly TextBox _slabPlane = new TextBox { Width = 100 };
        readonly TextBox _wallSearch = new TextBox { Width = 100 };
        readonly TextBox _edgeSearch = new TextBox { Width = 100 };
        readonly ComboBox _planePosition = new ComboBox { Width = 170, ItemsSource = new[] { "Snap to face", "Through block point" } };
        readonly TextBox _lineSearch = new TextBox { Width = 100 };
        readonly TextBox _reviewDistance = new TextBox { Width = 100 };
        readonly CheckBox _showDetection = new CheckBox { Content = "Show detection" };
        readonly CheckBox _nested = new CheckBox { Content = "Include nested blocks" };
        Expander _advanced;

        UIElement BuildAdvanced()
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, S / 2, 0, 0) };
            wrap.Children.Add(Field("Slab / ceiling search range (mm)", _slabRange,
                "Slab (above) and Ceiling: look for a slab, beam or ceiling from the row's level up to this height."));
            wrap.Children.Add(Field("Fallback plane height (mm)", _slabPlane,
                "No slab or ceiling in range: the family goes on a reference plane at this height above the level (listed in Needs Review)."));
            wrap.Children.Add(Field("Wall search (mm)", _wallSearch,
                "Host Wall: the nearest wall within this plan distance of the block is used."));
            wrap.Children.Add(Field("Vertical plane: wall/column search (mm)", _edgeSearch,
                "Vertical plane: a Revit wall or column face within this distance of the block is used first."));
            wrap.Children.Add(Field("Vertical plane position", _planePosition,
                "Snap to face: the device sits on the wall face. Through block point: the plane stays at the block, parallel to the face."));
            wrap.Children.Add(Field("DWG line search (mm)", _lineSearch,
                "No Revit wall nearby: DWG lines within this distance of the block outline are scored and the best one is used."));
            wrap.Children.Add(Field("Review if farther than (mm)", _reviewDistance,
                "Distance check: any element placed farther than this from its CAD block is listed in Needs Review."));
            var checks = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, S) };
            _showDetection.ToolTip = "After Preview or Run, draw the line chosen for each vertical-plane block as magenta detail lines in the plan view.";
            _nested.ToolTip = "Also list blocks nested inside other blocks. Changing it reads the DWG again.";
            _nested.Margin = new Thickness(0, S, 0, 0);
            checks.Children.Add(_showDetection);
            checks.Children.Add(_nested);
            wrap.Children.Add(checks);

            ShowAdvanced();
            foreach (var tb in new[] { _slabRange, _slabPlane, _wallSearch, _edgeSearch, _lineSearch, _reviewDistance })
                tb.TextChanged += (o, e) => UpdateStatus();
            _planePosition.SelectionChanged += (o, e) => UpdateStatus();
            _showDetection.IsChecked = _settings.ShowDetection;
            _showDetection.Checked += (o, e) => SetShowDetection(true);
            _showDetection.Unchecked += (o, e) => SetShowDetection(false);
            _nested.IsChecked = _s.IncludeNested;
            _nested.Checked += (o, e) => ReloadNested(true);
            _nested.Unchecked += (o, e) => ReloadNested(false);

            _advanced = new Expander
            {
                Header = Theme.Text("Advanced", Theme.LabelSize, bold: true),
                Content = wrap,
                IsExpanded = _settings.AdvancedOpen,
                Margin = new Thickness(0, 0, 0, 1.5 * S),
                ToolTip = "Search ranges, fallback plane, snapping, distance check and detection lines",
            };
            _advanced.Expanded += (o, e) => { _settings.AdvancedOpen = true; Settings.TrySave(_settings); };
            _advanced.Collapsed += (o, e) => { _settings.AdvancedOpen = false; Settings.TrySave(_settings); };
            return _advanced;
        }

        void ReloadNested(bool on)
        {
            if (_s.IncludeNested == on) return;
            _s.IncludeNested = on;
            _settings.IncludeNestedBlocks = on;
            Settings.TrySave(_settings);
            Action = MappingAction.Reload;
            Close();
        }

        void ShowAdvanced()
        {
            string F(double v) => v.ToString("0.#", Inv);
            _slabRange.Text = F(_s.Slab.SearchRangeMm);
            _slabPlane.Text = F(_s.Slab.FallbackPlaneMm);
            _wallSearch.Text = F(_s.Slab.WallSearchMm);
            _reviewDistance.Text = F(_s.Slab.ReviewDistanceMm);
            _edgeSearch.Text = F(_s.Slab.EdgeSearchMm);
            _planePosition.SelectedItem = Mapping.PlanePositionText(_s.Slab.PlanePosition);
            _lineSearch.Text = F(_s.Slab.LineSearchMm);
        }

        /// <summary>Copies valid values into the session; invalid boxes get a red border and a tooltip.
        /// Returns the first problem, or null.</summary>
        string ReadAdvanced()
        {
            string problem = null;
            double? Read(TextBox box, string what, bool positive)
            {
                var v = Mapping.ParseNumber(box.Text);
                bool ok = v.HasValue && (box.Text ?? "").Trim().Length > 0 && (!positive || v.Value > 0);
                var msg = ok ? null : $"{what} must be a number of mm{(positive ? " greater than 0" : "")}.";
                MarkInvalid(box, msg);
                if (!ok && problem == null) problem = "Advanced: " + msg;
                return ok ? v : null;
            }
            var range = Read(_slabRange, "Slab / ceiling search range", true);
            if (range.HasValue) _s.Slab.SearchRangeMm = range.Value;
            var plane = Read(_slabPlane, "Fallback plane height", false);
            if (plane.HasValue) _s.Slab.FallbackPlaneMm = plane.Value;
            var wall = Read(_wallSearch, "Wall search", true);
            if (wall.HasValue) _s.Slab.WallSearchMm = wall.Value;
            var edge = Read(_edgeSearch, "Wall/column search", true);
            if (edge.HasValue) _s.Slab.EdgeSearchMm = edge.Value;
            var line = Read(_lineSearch, "DWG line search", true);
            if (line.HasValue) _s.Slab.LineSearchMm = line.Value;
            var review = Read(_reviewDistance, "Review distance", true);
            if (review.HasValue) _s.Slab.ReviewDistanceMm = review.Value;
            if (_planePosition.SelectedItem is string pos && Mapping.ParsePlanePosition(pos) is PlanePosition pp) _s.Slab.PlanePosition = pp;
            if (problem != null && _advanced != null) _advanced.IsExpanded = true;
            return problem;
        }

        void MarkInvalid(Control box, string error)
        {
            if (error == null)
            {
                box.SetResourceReference(Control.BorderBrushProperty, "Sh.Border");
                box.ClearValue(Control.BorderThicknessProperty);
            }
            else
            {
                box.SetResourceReference(Control.BorderBrushProperty, "Sh.Danger");
                box.BorderThickness = new Thickness(2);
            }
            var tip = box.Tag as string ?? box.ToolTip as string;
            if (box.Tag == null) box.Tag = tip;   // the normal tooltip, kept to restore
            box.ToolTip = error ?? tip;
        }

        void SetShowDetection(bool on)
        {
            _settings.ShowDetection = on;
            Settings.TrySave(_settings);
        }

        // ---- 7. Footer -----------------------------------------------------------------------

        Button _preview, _run, _cancel;
        readonly TextBlock _footerHint = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2 * S, 0) };

        UIElement BuildFooter()
        {
            var bar = new DockPanel { LastChildFill = false };
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(Theme.Button(this, "Load mapping...", (o, e) => LoadMapping(), "Load a saved mapping (XLSX or CSV) into the grid, families included"));
            left.Children.Add(Theme.Button(this, "Save mapping...", (o, e) => SaveMapping(), "Save the grid as a mapping file (XLSX or CSV)"));
            DockPanel.SetDock(left, Dock.Left);
            bar.Children.Add(left);
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            _footerHint.SetResourceReference(TextBlock.ForegroundProperty, "Sh.Muted");
            right.Children.Add(_footerHint);
            _preview = Theme.Button(this, "Preview", (o, e) => Place(true), "Place and undo: see exactly what Run would do, nothing is changed  (Enter)");
            _run = Theme.Button(this, "Run", (o, e) => Place(false), "Place the families. One Ctrl+Z in Revit undoes the whole run  (Ctrl+Enter)", primary: true);
            _cancel = Theme.Button(this, "Cancel", (o, e) => { Action = MappingAction.Cancel; Close(); }, "Close without placing  (Esc)");
            _cancel.Margin = new Thickness(0);
            right.Children.Add(_preview);
            right.Children.Add(_run);
            right.Children.Add(_cancel);
            DockPanel.SetDock(right, Dock.Right);
            bar.Children.Add(right);
            return bar;
        }

        // ---- Keyboard --------------------------------------------------------------------

        bool _editing;

        void OnWindowKey(object sender, KeyEventArgs e)
        {
            if (_busy)
            {
                if (e.Key == Key.Escape) { _cancelRequested = true; e.Handled = true; }
                return;
            }
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var focus = Keyboard.FocusedElement as DependencyObject;
            bool inText = focus is TextBox || Ancestor<ComboBox>(focus) != null;
            bool dropDownOpen = Ancestor<ComboBox>(focus)?.IsDropDownOpen == true;
            if (ctrl && e.Key == Key.F)
            {
                _search.Focus();
                _search.SelectAll();
                e.Handled = true;
            }
            else if (ctrl && e.Key == Key.Enter)
            {
                CommitEdits();
                if (_run.IsEnabled) Place(false);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && !ctrl && !_editing && !dropDownOpen && !(focus is TextBox && !ReferenceEquals(focus, _search)) && Ancestor<FilterComboBox>(focus) == null)
            {
                CommitEdits();
                if (_preview.IsEnabled) Place(true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && !_editing && !dropDownOpen)
            {
                Action = MappingAction.Cancel;
                Close();
                e.Handled = true;
            }
            else if (!_editing && !inText && Ancestor<DataGrid>(focus) != null)
            {
                if (e.Key == Key.Delete) { SkipSelected(); e.Handled = true; }
                else if (ctrl && e.Key == Key.C && _grid.SelectedItem is BlockRow r) { _copied = r; e.Handled = true; }
                else if (ctrl && e.Key == Key.V && _copied != null) { PasteSettings(); e.Handled = true; }
            }
        }

        // ---- Preview / Run inside the window -----------------------------------------------

        bool _busy, _cancelRequested;
        readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();
        long _lastPump = -1000;

        void Place(bool preview)
        {
            CommitEdits();
            UpdateStatus();
            if (!_run.IsEnabled || _s.Place == null) return;
            ShowMessage("");
            bool close = false;
            SetBusy(true, preview ? "Preview: placing and undoing..." : "Placing families...");
            try
            {
                close = _s.Place(preview, (done, total) => Report(done, total, $"{(preview ? "Preview" : "Placing")}: block {Math.Min(done + 1, total):N0} of {total:N0}"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "SmartHost MEP", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false, null);
            }
            if (close)
            {
                Action = MappingAction.Done;
                Close();
                return;
            }
            ShowMessage(_s.Message, _s.MessageIsWarning ? "Sh.Warn" : "Sh.Good");
            if (_s.Rows.Any(r => r.DetectedHost.Length > 0) && !_shown.Contains("detected")) SetColumnShown("detected", true);
            UpdateStatus();
        }

        void SetBusy(bool busy, string text)
        {
            _busy = busy;
            _cancelRequested = false;
            foreach (var child in ((Grid)Content).Children.OfType<UIElement>())
                if (!ReferenceEquals(child, _summaryRow)) child.IsEnabled = !busy;
            _summaryPanel.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
            _progressPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            _progressCancel.IsEnabled = true;
            _bar.Value = 0;
            _progressText.Text = text ?? "";
            _clock.Restart();
            _lastPump = -1000;
            UpdateSelectionBar();
            if (busy) Pump();
        }

        /// <summary>Progress callback (placement, family symbols): updates the bar a few times a second
        /// and keeps the window responsive. Returns false once Cancel was clicked.</summary>
        bool Report(int done, int total, string text)
        {
            if (_cancelRequested) return false;
            if (_clock.ElapsedMilliseconds - _lastPump < 120 && done < total) return true;
            _bar.Value = total > 0 ? Math.Min(1000, 1000.0 * done / total) : 0;
            var secs = _clock.Elapsed.TotalSeconds;
            string eta = done > 0 && done < total ? $"  ·  about {Math.Max(1, secs / done * (total - done)):0} s left" : "";
            _progressText.Text = text + eta;
            Pump();
            return !_cancelRequested;
        }

        void Pump()
        {
            _lastPump = _clock.ElapsedMilliseconds;
            var frame = new DispatcherFrame();
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(f => { ((DispatcherFrame)f).Continue = false; return null; }), frame);
            Dispatcher.PushFrame(frame);
        }

        // ---- Multi-row editing (edit one of several selected rows -> all of them) -----------

        void OnRowChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BlockRow.Family) || e.PropertyName == nameof(BlockRow.Elevation) ||
                e.PropertyName == nameof(BlockRow.Rotation) || e.PropertyName == nameof(BlockRow.IsSkipped))
                UpdateStatus();
            if (_propagating || _editGroup == null || !(sender is BlockRow src) || !_editGroup.Contains(src)) return;
            if (!GroupProps.Contains(e.PropertyName)) return;
            _propagating = true;
            try
            {
                foreach (var r in _editGroup)
                    if (!ReferenceEquals(r, src)) CopyProp(src, r, e.PropertyName);
            }
            finally { _propagating = false; }
        }

        List<BlockRow> _editGroup;
        bool _propagating;
        BlockRow _anchor;   // Shift+click range start
        static readonly HashSet<string> GroupProps = new HashSet<string>
        {
            nameof(BlockRow.Family), nameof(BlockRow.Level), nameof(BlockRow.Elevation), nameof(BlockRow.Rotation),
            nameof(BlockRow.Host), nameof(BlockRow.Facing), nameof(BlockRow.PlaceAt), nameof(BlockRow.Category),
        };

        static void CopyProp(BlockRow from, BlockRow to, string prop)
        {
            switch (prop)
            {
                case nameof(BlockRow.Family): to.Family = from.Family; break;
                case nameof(BlockRow.Level): to.Level = from.Level; break;
                case nameof(BlockRow.Elevation): to.Elevation = from.Elevation; break;
                case nameof(BlockRow.Rotation): to.Rotation = from.Rotation; break;
                case nameof(BlockRow.Host): to.Host = from.Host; break;
                case nameof(BlockRow.Facing): to.Facing = from.Facing; break;
                case nameof(BlockRow.PlaceAt): to.PlaceAt = from.PlaceAt; break;
                case nameof(BlockRow.Category): to.Category = from.Category; break;
            }
        }

        static T Ancestor<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T))
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }

        void OnGridMouseDown(object sender, MouseButtonEventArgs e)
        {
            var src = e.OriginalSource as DependencyObject;
            var rowEl = Ancestor<DataGridRow>(src);
            if (rowEl == null || !(rowEl.Item is BlockRow item)) return;   // header, scrollbar...
            var mods = Keyboard.Modifiers;
            if ((mods & ModifierKeys.Control) != 0)
            {
                // Ctrl+click: add / remove this row, wherever on the row (dropdowns do not open).
                CommitEdits();
                if (_grid.SelectedItems.Contains(item)) _grid.SelectedItems.Remove(item);
                else _grid.SelectedItems.Add(item);
                _anchor = item;
                _grid.Focus();
                e.Handled = true;
                return;
            }
            if ((mods & ModifierKeys.Shift) != 0)
            {
                // Shift+click: every shown row from the last clicked one to this one.
                CommitEdits();
                var shown = _grid.Items.OfType<BlockRow>().ToList();
                int a = _anchor != null ? shown.IndexOf(_anchor) : -1, b = shown.IndexOf(item);
                if (a < 0) a = b;
                _grid.SelectedItems.Clear();
                for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) _grid.SelectedItems.Add(shown[i]);
                _grid.Focus();
                e.Handled = true;
                return;
            }
            _anchor = item;
            // Plain click in an editable cell of a row that is part of a multi-selection: keep the
            // selection and apply what is changed in this row to all selected rows.
            var cell = Ancestor<DataGridCell>(src);
            var sel = _grid.SelectedItems.OfType<BlockRow>().ToList();
            if (sel.Count > 1 && sel.Contains(item) && cell != null && !cell.Column.IsReadOnly)
            {
                _editGroup = sel;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_editGroup == null) return;
                    foreach (var r in _editGroup)
                        if (!_grid.SelectedItems.Contains(r)) _grid.SelectedItems.Add(r);
                }), DispatcherPriority.Input);
            }
            else _editGroup = null;
        }

        void CommitEdits()
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
            _editing = false;
            _editGroup = null;   // buttons (Apply, Auto-match, Load...) change rows themselves
        }

        // ---- Window size, position and column widths ------------------------------------

        void RestorePlacement()
        {
            var p = (_settings.WindowPlacement ?? "").Split(',');
            if (p.Length < 5) return;
            if (!double.TryParse(p[0], NumberStyles.Float, Inv, out var l) || !double.TryParse(p[1], NumberStyles.Float, Inv, out var t) ||
                !double.TryParse(p[2], NumberStyles.Float, Inv, out var w) || !double.TryParse(p[3], NumberStyles.Float, Inv, out var h)) return;
            // Only when it is still on a screen.
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                  SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (w < MinWidth || h < MinHeight || !screen.IntersectsWith(new Rect(l + 50, t + 10, Math.Max(1, w - 100), 40))) return;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = l; Top = t; Width = w; Height = h;
            if (p[4] == "1") Loaded += (o, e) => WindowState = WindowState.Maximized;
        }

        void SavePlacement()
        {
            var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (r.IsEmpty || double.IsNaN(r.Width)) return;
            _settings.WindowPlacement = string.Join(",", r.Left.ToString("0", Inv), r.Top.ToString("0", Inv), r.Width.ToString("0", Inv),
                                                    r.Height.ToString("0", Inv), WindowState == WindowState.Maximized ? "1" : "0");
            _settings.GridColumns = string.Join(";", _cols.Where(kv => !kv.Value.Width.IsStar)
                .Select(kv => kv.Key + ":" + kv.Value.ActualWidth.ToString("0", Inv)));
            Settings.TrySave(_settings);
        }

        void RestoreColumnWidths()
        {
            foreach (var part in (_settings.GridColumns ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split(':');
                if (kv.Length == 2 && _cols.TryGetValue(kv[0], out var col) && !col.Width.IsStar &&
                    double.TryParse(kv[1], NumberStyles.Float, Inv, out var w) && w >= 40 && w <= 1200)
                    col.Width = new DataGridLength(w);
            }
        }

        // ---- Load / Save / Auto-match ---------------------------------------------------------

        void LoadMapping()
        {
            CommitEdits();
            var dlg = new OpenFileDialog
            {
                Title = "Load mapping (XLSX or CSV)",
                Filter = "Mapping files (*.xlsx;*.csv)|*.xlsx;*.csv|All files (*.*)|*.*",
            };
            TrySetInitialDir(dlg);
            if (dlg.ShowDialog(this) != true) return;
            var mapping = Mapping.Load(dlg.FileName);
            var (applied, messages) = _s.Apply(mapping);
            ShowAdvanced();
            RememberFolder(dlg.FileName);
            UpdateStatus();
            var text = $"Applied {applied} of {_s.Rows.Count} blocks from\n{dlg.FileName}";
            int notInDwg = mapping.Rows.Keys.Count(k => !_s.Rows.Any(r => r.BlockName.Equals(k, StringComparison.OrdinalIgnoreCase)));
            if (notInDwg > 0) text += $"\n\n{notInDwg} mapped block(s) in the file are not in this DWG (ignored).";
            if (messages.Count > 0) text += "\n\n" + string.Join("\n", messages.Take(20)) + (messages.Count > 20 ? "\n..." : "");
            if (messages.Count > 0) MessageBox.Show(this, text, "SmartHost MEP", MessageBoxButton.OK, MessageBoxImage.Warning);
            else ShowMessage($"Loaded {applied} block(s) from {Path.GetFileName(dlg.FileName)}.", "Sh.Good");
        }

        void SaveMapping()
        {
            CommitEdits();
            var dlg = new SaveFileDialog
            {
                Title = "Save mapping",
                Filter = "Excel workbook (*.xlsx)|*.xlsx|CSV UTF-8 (*.csv)|*.csv",
                FileName = "smarthost_mapping.xlsx",
            };
            TrySetInitialDir(dlg);
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                Mapping.Save(dlg.FileName, _s.Rows.Select(r => r.ToMapRow()), _s.Slab);
                RememberFolder(dlg.FileName);
                ShowMessage("Mapping saved: " + dlg.FileName, "Sh.Good");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the mapping:\n" + ex.Message, "SmartHost MEP", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void AutoMatch()
        {
            CommitEdits();
            List<FamilySymbolEntry> families = null;
            if (_s.LoadFamilySymbols != null)
            {
                SetBusy(true, "Reading family symbols...");
                try
                {
                    families = _s.LoadFamilySymbols((done, total, name) =>
                        Report(done, total, total == 0 ? "" : $"Reading family {Math.Min(done + 1, total):N0} of {total:N0}: {name}"));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not read the family symbols - matching by name only.\n\n" + ex.Message, "SmartHost MEP",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                finally
                {
                    SetBusy(false, null);
                }
            }
            var r = _s.AutoMatch(families);
            if (!_shown.Contains("score")) SetColumnShown("score", true);
            if (!_shown.Contains("top3")) SetColumnShown("top3", true);
            UpdateStatus();
            var w = _s.Settings?.SymbolScore ?? new SymbolScoreWeights();
            var lines = new List<string>
            {
                r.Selected == 0 ? "No family was selected automatically." : $"A family was selected for {r.Selected} block(s):",
            };
            if (r.Learned > 0) lines.Add($"  - {r.Learned} from your earlier manual matches");
            if (r.BySymbol > 0) lines.Add($"  - {r.BySymbol} by symbol (score {w.AutoMatch:0} or more)" +
                                          (r.Ambiguous > 0 ? $", {r.Ambiguous} of them close to another family: Needs Review 'Ambiguous symbol match'" : ""));
            if (r.ByName > 0) lines.Add($"  - {r.ByName} by name (no symbol to compare)");
            if (r.Suggested > 0) lines.Add($"\n{r.Suggested} block(s) have candidates scoring {w.Minimum:0}-{w.AutoMatch - 1:0}: pick one in 'Top 3 Candidates'.");
            if (!string.IsNullOrEmpty(_s.SymbolInfo)) lines.Add("\n" + _s.SymbolInfo);
            lines.Add("\nRows already mapped are not changed. Please check the selected families.");
            var report = WriteAutoMatchReport(r);
            if (report != null)
            {
                lines.Add("\nOpen the report (each family's symbol view, each block's scores)?");
                if (MessageBox.Show(this, string.Join("\n", lines), "SmartHost MEP - Auto-match", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(report) { UseShellExecute = true }); } catch (Exception) { }
            }
            else MessageBox.Show(this, string.Join("\n", lines), "SmartHost MEP - Auto-match");
        }

        /// <summary>%TEMP%\SmartHostMEP\AutoMatch-report.txt: what each family and block got. Null if it cannot be written.</summary>
        string WriteAutoMatchReport(MappingSession.AutoMatchSummary r)
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), AppStorage.FolderName, "AutoMatch-report.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var text = new List<string> { "SmartHost MEP - Auto-match report  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"), "", _s.SymbolInfo, "",
                                              "FAMILIES (family, symbol view: what was read)" };
                text.AddRange(_s.SymbolReport);
                text.Add("");
                text.Add("BLOCKS (block, its symbol, result, top 3 candidates)");
                text.AddRange(r.Lines);
                File.WriteAllLines(path, text);
                return path;
            }
            catch (Exception)
            {
                return null;
            }
        }

        void ClearFamilies()
        {
            CommitEdits();
            int n = _s.Rows.Count(r => !r.Family.IsSkip);
            if (n == 0) return;
            if (MessageBox.Show(this, $"Set the Revit Family of all {n} mapped row(s) back to (Skip)?", "SmartHost MEP",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            _s.ClearFamilies();
            UpdateStatus();
        }

        void TrySetInitialDir(FileDialog dlg)
        {
            try
            {
                var last = _settings.LastMappingPath;
                if (!string.IsNullOrEmpty(last) && Directory.Exists(Path.GetDirectoryName(last)))
                    dlg.InitialDirectory = Path.GetDirectoryName(last);
            }
            catch (Exception) { }
        }

        void RememberFolder(string path)
        {
            _settings.LastMappingPath = path;
            Settings.TrySave(_settings);
        }
    }
}
