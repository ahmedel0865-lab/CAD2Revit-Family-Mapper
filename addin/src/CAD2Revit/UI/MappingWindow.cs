using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using CAD2Revit.Core;
using Microsoft.Win32;

namespace CAD2Revit.UI
{
    public enum MappingAction { Cancel, Preview, Run }

    /// <summary>
    /// The mapping window: one row per unique CAD block name, with a searchable
    /// "Revit Family" dropdown, elevation, rotation and host type.
    /// Built in code (no XAML) so the add-in compiles on any .NET SDK.
    /// </summary>
    public class MappingWindow : Window
    {
        readonly MappingSession _s;
        readonly DataGrid _grid = new DataGrid();
        readonly TextBlock _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

        public MappingAction Action { get; private set; } = MappingAction.Cancel;

        // Colours shared by the window (match the ribbon icons).
        static readonly SolidColorBrush Accent = Frozen(Color.FromRgb(32, 96, 176));
        static readonly SolidColorBrush AccentLight = Frozen(Color.FromRgb(232, 238, 247));
        static readonly SolidColorBrush Panel = Frozen(Color.FromRgb(246, 248, 251));
        static readonly SolidColorBrush PanelBorder = Frozen(Color.FromRgb(214, 222, 234));
        static readonly SolidColorBrush Muted = Frozen(Color.FromRgb(110, 118, 130));

        static SolidColorBrush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        readonly TextBlock _statMapped = new TextBlock(), _statInstances = new TextBlock(), _statSkipped = new TextBlock(),
                           _statErrors = new TextBlock();

        public MappingWindow(MappingSession session)
        {
            _s = session;
            Title = "CAD2Revit - Map CAD blocks to Revit families";
            Width = 1680;
            Height = 760;
            MinWidth = 1150;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12;
            Background = Brushes.White;

            var root = new DockPanel { Margin = new Thickness(14) };

            // ---- Header card: what is being mapped + live counts ----------------------
            var header = new Border
            {
                Background = AccentLight, CornerRadius = new CornerRadius(4), Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 10), BorderBrush = PanelBorder, BorderThickness = new Thickness(1),
            };
            var headerGrid = new DockPanel();
            var titles = new StackPanel();
            titles.Children.Add(new TextBlock { Text = "Map CAD blocks to Revit families", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Accent });
            titles.Children.Add(new TextBlock
            {
                Text = $"DWG: {_s.DwgName}     Starting level: {_s.LevelName}     {_s.Rows.Count} unique blocks, {_s.InstanceCount} instances",
                Foreground = Muted, Margin = new Thickness(0, 2, 0, 0),
            });
            titles.Children.Add(new TextBlock
            {
                Text = "Pick a Revit family for each block (type in the box to search). (Skip) = do not place. " +
                       "Elevation is measured from the row's Level. Select several rows to edit them together.",
                Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
            });
            var stats = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            stats.Children.Add(Chip("Mapped", _statMapped));
            stats.Children.Add(Chip("Instances to place", _statInstances));
            stats.Children.Add(Chip("Skipped", _statSkipped));
            stats.Children.Add(Chip("Invalid", _statErrors));
            DockPanel.SetDock(stats, Dock.Right);
            headerGrid.Children.Add(stats);
            headerGrid.Children.Add(titles);
            header.Child = headerGrid;
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // ---- Section 1: filter -----------------------------------------------------
            var filterBar = new WrapPanel();
            filterBar.Children.Add(BarLabel("Find"));
            var find = new TextBox { Width = 300, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0),
                                     ToolTip = "Show only rows whose block or family contains this text" };
            find.TextChanged += (o, e) => { _findText = find.Text; ApplyFilter(); };
            filterBar.Children.Add(find);
            filterBar.Children.Add(BarLabel("Show"));
            var show = new ComboBox { Width = 190, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            show.Items.Add(AllCategories);
            foreach (var c in BlockCategories.All)
            {
                int n = _s.Rows.Count(r => r.Category == c);
                if (n > 0) show.Items.Add(new ComboBoxItem { Content = $"{c} ({n})", Tag = c });
            }
            show.SelectedIndex = 0;
            show.SelectionChanged += (o, e) =>
            {
                _showCategory = (show.SelectedItem as ComboBoxItem)?.Tag as string;
                ApplyFilter();
            };
            filterBar.Children.Add(show);
            var skipShown = MakeButton("Skip shown rows", (o, e) => SkipShownRows());
            skipShown.ToolTip = "Set every row currently shown (after Find / Show) to (Skip), e.g. all Architectural blocks";
            filterBar.Children.Add(skipShown);
            var filterSection = Section("1 · Filter", filterBar);
            DockPanel.SetDock(filterSection, Dock.Top);
            root.Children.Add(filterSection);

            // ---- Section 2: edit selected rows -----------------------------------------
            var editSection = Section("2 · Edit selected rows", BuildBulkBar());
            DockPanel.SetDock(editSection, Dock.Top);
            root.Children.Add(editSection);

            // ---- Footer: mapping file (left), actions (right) ---------------------------
            var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
            DockPanel.SetDock(bottom, Dock.Bottom);
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(new TextBlock { Text = "Mapping file:", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            left.Children.Add(MakeButton("Load...", (s, e) => LoadMapping(), "Load a mapping (XLSX or CSV) into the grid"));
            left.Children.Add(MakeButton("Save...", (s, e) => SaveMapping(), "Save the grid as a mapping file (XLSX or CSV)"));
            left.Children.Add(MakeButton("Auto-match", (s, e) => AutoMatch(), "Pre-select families whose names match the block names (rows still on Skip)"));
            left.Children.Add(_status);
            DockPanel.SetDock(left, Dock.Left);
            bottom.Children.Add(left);
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(MakeButton("Preview", (s, e) => Finish(MappingAction.Preview),
                "Run the placement and undo it: see exactly what Run would do, then come back here"));
            var run = MakeButton("Run", (s, e) => Finish(MappingAction.Run), "Place the families (one Ctrl+Z undoes the whole run)", primary: true);
            right.Children.Add(run);
            var cancel = MakeButton("Cancel", (s, e) => { Action = MappingAction.Cancel; Close(); });
            cancel.IsCancel = true;
            right.Children.Add(cancel);
            DockPanel.SetDock(right, Dock.Right);
            bottom.Children.Add(right);
            root.Children.Add(bottom);

            // ---- Grid (grouped by category, Electrical first) ---------------------------
            SetUpRowView();
            BuildGrid();
            var body = new DockPanel();
            var preview = BuildPreviewPanel();
            DockPanel.SetDock(preview, Dock.Right);
            body.Children.Add(preview);
            body.Children.Add(new Border { BorderBrush = PanelBorder, BorderThickness = new Thickness(1), Child = _grid });
            root.Children.Add(body);
            Content = root;

            foreach (var row in _s.Rows) row.PropertyChanged += OnRowChanged;
            Closed += (o, e) => { foreach (var row in _s.Rows) row.PropertyChanged -= OnRowChanged; };
            UpdateStatus();
        }

        // ---- Symbol preview -----------------------------------------------------------
        readonly Image _previewImage = new Image { Width = 210, Height = 210, Stretch = Stretch.Uniform };
        readonly TextBlock _previewName = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 2) };
        readonly TextBlock _previewInfo = new TextBlock { Foreground = Muted, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock _previewEmpty = new TextBlock
        {
            Foreground = Muted, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(12),
        };

        Border BuildPreviewPanel()
        {
            var frame = new Grid { Width = 220, Height = 220, Background = Brushes.White };
            frame.Children.Add(_previewImage);
            frame.Children.Add(_previewEmpty);
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "SYMBOL PREVIEW", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Muted, Margin = new Thickness(0, 0, 0, 6) });
            stack.Children.Add(new Border { BorderBrush = PanelBorder, BorderThickness = new Thickness(1), Child = frame });
            stack.Children.Add(_previewName);
            stack.Children.Add(_previewInfo);
            ShowPreview(null);
            return new Border
            {
                Width = 246, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 8, 10, 8),
                Background = Panel, BorderBrush = PanelBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Child = stack,
            };
        }

        /// <summary>Show a row's symbol: the hovered row, else the selected row.</summary>
        void ShowPreview(BlockRow row)
        {
            row = row ?? _grid.SelectedItem as BlockRow;
            if (row == null)
            {
                _previewImage.Source = null;
                _previewEmpty.Text = "Hover over or select a CAD block to see its 2D symbol.";
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
            _previewInfo.Text = $"{row.Count} instance(s)  ·  {row.Category}" +
                                (size.Length > 0 ? $"\nSize: {size}" : "") +
                                (row.IsSkipped ? "\nNot mapped (Skip)" : "\n" + row.FamilyLabel) +
                                (row.Symbol != null && row.Symbol.Truncated ? "\n(large block: preview simplified)" : "");
        }

        /// <summary>A titled, lightly framed block of controls.</summary>
        static Border Section(string title, UIElement content)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold,
                                               Foreground = Muted, Margin = new Thickness(0, 0, 0, 6) });
            stack.Children.Add(content);
            return new Border
            {
                Background = Panel, BorderBrush = PanelBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 8), Child = stack,
            };
        }

        /// <summary>Small statistic in the header: big number over a caption.</summary>
        static Border Chip(string caption, TextBlock value)
        {
            value.FontSize = 18;
            value.FontWeight = FontWeights.SemiBold;
            value.HorizontalAlignment = HorizontalAlignment.Center;
            var stack = new StackPanel();
            stack.Children.Add(value);
            stack.Children.Add(new TextBlock { Text = caption, FontSize = 10.5, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center });
            return new Border
            {
                Background = Brushes.White, BorderBrush = PanelBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), MinWidth = 90, Child = stack,
            };
        }

        static Button MakeButton(string text, RoutedEventHandler click, string tooltip = null, bool primary = false)
        {
            var b = new Button { Content = text, MinWidth = 90, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
            if (tooltip != null) b.ToolTip = tooltip;
            if (primary)
            {
                b.Background = Accent;
                b.Foreground = Brushes.White;
                b.BorderBrush = Accent;
                b.FontWeight = FontWeights.SemiBold;
                b.MinWidth = 110;
            }
            b.Click += click;
            return b;
        }

        void BuildGrid()
        {
            _grid.AutoGenerateColumns = false;
            _grid.CanUserAddRows = false;
            _grid.CanUserDeleteRows = false;
            _grid.CanUserReorderColumns = false;
            // Several rows can be selected (Ctrl+click, Shift+click, Ctrl+A) and edited together.
            _grid.SelectionMode = DataGridSelectionMode.Extended;
            _grid.SelectionUnit = DataGridSelectionUnit.FullRow;
            _grid.SelectionChanged += (o, e) => { UpdateSelectionLabel(); ShowPreview(null); };
            _grid.MouseLeave += (o, e) => ShowPreview(null);
            _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            _grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            _grid.HorizontalGridLinesBrush = PanelBorder;
            _grid.BorderThickness = new Thickness(0);
            _grid.Background = Brushes.White;
            _grid.RowBackground = Brushes.White;
            _grid.AlternatingRowBackground = Panel;
            _grid.RowHeight = 30;
            _grid.ColumnHeaderStyle = HeaderStyle();
            _grid.RowStyle = RowStyle();
            _grid.EnableRowVirtualization = true;
            VirtualizingPanel.SetVirtualizationMode(_grid, VirtualizationMode.Recycling);
            VirtualizingPanel.SetIsVirtualizingWhenGrouping(_grid, true);
            _grid.ItemsSource = _s.Rows;
            _grid.GroupStyle.Add(new GroupStyle { HeaderTemplate = GroupHeaderTemplate() });

            // 1. CAD Block (read-only)
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = "CAD Block",
                Binding = new Binding(nameof(BlockRow.Display)) { Mode = BindingMode.OneWay },
                IsReadOnly = true,
                SortMemberPath = nameof(BlockRow.BlockName),
                // Takes the remaining width; long names end in "..." (full name in the tooltip).
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 200,
                ElementStyle = TrimmedTextStyle(),
            });

            // Category (auto-detected from the block name; change it if the guess is wrong)
            _grid.Columns.Add(new DataGridComboBoxColumn
            {
                Header = "Category",
                ItemsSource = BlockCategories.All,
                SelectedItemBinding = new Binding(nameof(BlockRow.Category)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                SortMemberPath = nameof(BlockRow.CategoryOrder),
                Width = new DataGridLength(115),
            });

            // 2. Revit Family (searchable dropdown, always editable in the cell)
            var combo = new FrameworkElementFactory(typeof(FilterComboBox));
            combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(BlockRow.Options)) { Mode = BindingMode.OneWay });
            combo.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(BlockRow.Family))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
            combo.SetValue(FrameworkElement.MarginProperty, new Thickness(1));
            combo.SetValue(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center);
            _grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Revit Family  (Family : Type)",
                CellTemplate = new DataTemplate { VisualTree = combo },
                SortMemberPath = nameof(BlockRow.FamilyLabel),
                // Fixed width so long block names can never squeeze the dropdown.
                Width = new DataGridLength(400),
                MinWidth = 250,
            });

            // 3. Level + Elevation From Level (mm), validated
            _grid.Columns.Add(new DataGridComboBoxColumn
            {
                Header = "Level",
                ItemsSource = _s.LevelNames,
                SelectedItemBinding = new Binding(nameof(BlockRow.Level)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = new DataGridLength(140),
            });
            _grid.Columns.Add(NumberColumn("Elevation From Level (mm)", nameof(BlockRow.Elevation), 160));

            // Optional extras at the end
            _grid.Columns.Add(NumberColumn("Rotation (deg)", nameof(BlockRow.Rotation), 95));
            _grid.Columns.Add(new DataGridComboBoxColumn
            {
                Header = "Host Type",
                ItemsSource = BlockRow.HostChoices,
                SelectedItemBinding = new Binding(nameof(BlockRow.Host)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = new DataGridLength(200),
            });
            _grid.Columns.Add(new DataGridComboBoxColumn
            {
                Header = "Facing",
                ItemsSource = BlockRow.FacingChoices,
                SelectedItemBinding = new Binding(nameof(BlockRow.Facing)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = new DataGridLength(70),
            });
        }

        static Style HeaderStyle()
        {
            var style = new Style(typeof(DataGridColumnHeader));
            style.Setters.Add(new Setter(Control.BackgroundProperty, AccentLight));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 6, 6, 6)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, PanelBorder));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            return style;
        }

        /// <summary>Skipped rows are greyed so the mapped ones stand out.</summary>
        Style RowStyle()
        {
            var style = new Style(typeof(DataGridRow));
            // Hovering a row shows its symbol in the preview panel.
            style.Setters.Add(new EventSetter(UIElement.MouseEnterEvent, new System.Windows.Input.MouseEventHandler(
                (o, e) => ShowPreview((o as DataGridRow)?.Item as BlockRow))));
            var skipped = new DataTrigger { Binding = new Binding(nameof(BlockRow.IsSkipped)), Value = true };
            skipped.Setters.Add(new Setter(Control.ForegroundProperty, Muted));
            style.Triggers.Add(skipped);
            return style;
        }

        static Style TrimmedTextStyle()
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(nameof(BlockRow.SymbolToolTip))));
            return style;
        }

        const string Keep = "(keep)";
        readonly TextBlock _selectedLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 10, 0) };
        ComboBox _bulkHost, _bulkLevel, _bulkFacing, _bulkCategory;
        TextBox _bulkElevation;

        static ComboBox KeepCombo(IEnumerable<string> items, double width)
        {
            var cb = new ComboBox { Width = width, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            cb.Items.Add(Keep);
            foreach (var i in items) cb.Items.Add(i);
            cb.SelectedIndex = 0;
            return cb;
        }

        static TextBlock BarLabel(string text) =>
            new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };

        UIElement BuildBulkBar()
        {
            var bar = new WrapPanel();
            _bulkHost = KeepCombo(BlockRow.HostChoices, 200);
            _bulkLevel = KeepCombo(_s.LevelNames, 140);
            _bulkElevation = new TextBox { Width = 70, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
                                           ToolTip = "Leave empty to keep each row's elevation" };
            _bulkFacing = KeepCombo(BlockRow.FacingChoices, 70);
            _bulkCategory = KeepCombo(BlockCategories.All, 115);
            var apply = MakeButton("Apply to selected rows", (o, e) => ApplyToSelected(),
                "Set the chosen values on every selected row; '(keep)' fields are not changed", primary: true);
            var selectAll = MakeButton("Select all shown", (o, e) => { _grid.Focus(); _grid.SelectAll(); },
                "Select every row currently shown (same as Ctrl+A in the grid)");
            // Global switch: every row -> Reference Plane (unticking restores each row's previous host).
            var allPlanes = new CheckBox
            {
                Content = "Reference planes for all rows",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                ToolTip = "Sets Host Type to 'Reference Plane (auto-create)' for every row. Untick to restore the previous Host Types.",
                IsChecked = _s.Rows.Count > 0 && _s.Rows.All(r => r.Host == BlockRow.RefPlaneLabel),
            };
            allPlanes.Checked += (o, e) => SetAllReferencePlanes(true);
            allPlanes.Unchecked += (o, e) => SetAllReferencePlanes(false);

            bar.Children.Add(_selectedLabel);
            bar.Children.Add(BarLabel("Host Type"));
            bar.Children.Add(_bulkHost);
            bar.Children.Add(BarLabel("Level"));
            bar.Children.Add(_bulkLevel);
            bar.Children.Add(BarLabel("Elevation (mm)"));
            bar.Children.Add(_bulkElevation);
            bar.Children.Add(BarLabel("Facing"));
            bar.Children.Add(_bulkFacing);
            bar.Children.Add(BarLabel("Category"));
            bar.Children.Add(_bulkCategory);
            bar.Children.Add(apply);
            bar.Children.Add(selectAll);
            bar.Children.Add(allPlanes);
            UpdateSelectionLabel();
            return bar;
        }

        void UpdateSelectionLabel()
        {
            int n = _grid.SelectedItems.Count;
            _selectedLabel.Text = n == 0 ? "Select rows (Ctrl/Shift+click, Ctrl+A), then set:" : $"{n} row(s) selected - set:";
        }

        void ApplyToSelected()
        {
            CommitEdits();
            var rows = _grid.SelectedItems.OfType<BlockRow>().ToList();
            if (rows.Count == 0)
            {
                MessageBox.Show(this, "Select one or more rows first (Ctrl+click, Shift+click, or Ctrl+A for all shown rows).",
                    "CAD2Revit", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string host = _bulkHost.SelectedItem as string, level = _bulkLevel.SelectedItem as string,
                   facing = _bulkFacing.SelectedItem as string, category = _bulkCategory.SelectedItem as string;
            var elevText = (_bulkElevation.Text ?? "").Trim();
            if (elevText.Length > 0 && Mapping.ParseNumber(elevText) == null)
            {
                MessageBox.Show(this, "Elevation must be a number (mm), or empty to keep each row's value.",
                    "CAD2Revit", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            bool any = host != Keep || level != Keep || facing != Keep || category != Keep || elevText.Length > 0;
            if (!any)
            {
                MessageBox.Show(this, "Choose at least one value to set (Host Type, Level, Elevation, Facing or Category).",
                    "CAD2Revit", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            foreach (var r in rows)
            {
                if (host != Keep) { r.Host = host; r.HostBeforeAll = null; }
                if (level != Keep) r.Level = level;
                if (elevText.Length > 0) r.Elevation = elevText;
                if (facing != Keep) r.Facing = facing;
                if (category != Keep) r.Category = category;
            }
        }

        void SetAllReferencePlanes(bool on)
        {
            CommitEdits();
            foreach (var r in _s.Rows)
            {
                if (on)
                {
                    if (r.Host != BlockRow.RefPlaneLabel) r.HostBeforeAll = r.Host;
                    r.Host = BlockRow.RefPlaneLabel;
                }
                else if (r.HostBeforeAll != null)
                {
                    r.Host = r.HostBeforeAll;
                    r.HostBeforeAll = null;
                }
            }
        }

        const string AllCategories = "All categories";
        string _findText = "";
        string _showCategory;   // null = all

        void SetUpRowView()
        {
            var view = CollectionViewSource.GetDefaultView(_s.Rows);
            view.Filter = null;
            view.GroupDescriptions.Clear();
            view.SortDescriptions.Clear();
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BlockRow.Category)));
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(BlockRow.CategoryOrder), System.ComponentModel.ListSortDirection.Ascending));
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(BlockRow.BlockName), System.ComponentModel.ListSortDirection.Ascending));
            // Re-group / re-sort when a row's category is changed in the grid.
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
            }
        }

        /// <summary>Group header: "Electrical  -  23 blocks".</summary>
        static DataTemplate GroupHeaderTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(232, 238, 247)));
            border.SetValue(Border.PaddingProperty, new Thickness(6, 3, 6, 3));
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            var name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            name.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            var count = new FrameworkElementFactory(typeof(TextBlock));
            count.SetBinding(TextBlock.TextProperty, new Binding("ItemCount") { StringFormat = "   -   {0} block(s)" });
            count.SetValue(TextBlock.ForegroundProperty, Brushes.DimGray);
            panel.AppendChild(name);
            panel.AppendChild(count);
            border.AppendChild(panel);
            return new DataTemplate { VisualTree = border };
        }

        void ApplyFilter()
        {
            CommitEdits();   // changing the filter during a cell edit throws
            var view = CollectionViewSource.GetDefaultView(_s.Rows);
            var terms = (_findText ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var cat = _showCategory;
            view.Filter = terms.Length == 0 && cat == null ? null : (Predicate<object>)(o =>
            {
                var r = (BlockRow)o;
                if (cat != null && r.Category != cat) return false;
                var hay = r.BlockName + " " + r.FamilyLabel;
                return terms.All(t => hay.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
            });
        }

        void SkipShownRows()
        {
            CommitEdits();
            var shown = CollectionViewSource.GetDefaultView(_s.Rows).Cast<BlockRow>().Where(r => !r.Family.IsSkip).ToList();
            if (shown.Count == 0) return;
            if (MessageBox.Show(this, $"Set {shown.Count} shown row(s) to (Skip)?", "CAD2Revit",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            foreach (var r in shown) r.Family = FamilyOption.Skip;
        }

        static DataGridTextColumn NumberColumn(string header, string property, double width)
        {
            var errorStyle = new Style(typeof(TextBlock));
            errorStyle.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right));
            errorStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            var trigger = new Trigger { Property = Validation.HasErrorProperty, Value = true };
            trigger.Setters.Add(new Setter(TextBlock.BackgroundProperty, new SolidColorBrush(Color.FromRgb(255, 220, 220))));
            trigger.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,
                new Binding("(Validation.Errors)[0].ErrorContent") { RelativeSource = RelativeSource.Self }));
            errorStyle.Triggers.Add(trigger);
            return new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(property)
                {
                    Mode = BindingMode.TwoWay,
                    ValidatesOnDataErrors = true,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                },
                ElementStyle = errorStyle,
                Width = new DataGridLength(width),
            };
        }

        void OnRowChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateStatus();

        void UpdateStatus()
        {
            int mapped = _s.Rows.Count(r => !r.Family.IsSkip);
            int instances = _s.Rows.Where(r => !r.Family.IsSkip).Sum(r => r.Count);
            int bad = _s.Rows.Count(r => r.Error != null);
            _statMapped.Text = $"{mapped} / {_s.Rows.Count}";
            _statMapped.Foreground = Accent;
            _statInstances.Text = instances.ToString();
            _statSkipped.Text = (_s.Rows.Count - mapped).ToString();
            _statSkipped.Foreground = Muted;
            _statErrors.Text = bad.ToString();
            _statErrors.Foreground = bad > 0 ? Brushes.Firebrick : Muted;
            _status.Text = bad > 0 ? $"{bad} row(s) have invalid numbers (red cells)" : "";
            _status.Foreground = Brushes.Firebrick;
        }

        void CommitEdits()
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        void Finish(MappingAction action)
        {
            CommitEdits();
            var bad = _s.Rows.Where(r => r.Error != null).ToList();
            if (bad.Count > 0)
            {
                MessageBox.Show(this, "Fix the numbers in these rows first:\n\n" +
                    string.Join("\n", bad.Take(15).Select(r => $"{r.BlockName}: {r.Error}")),
                    "CAD2Revit", MessageBoxButton.OK, MessageBoxImage.Warning);
                _grid.ScrollIntoView(bad[0]);
                return;
            }
            if (_s.Rows.All(r => r.Family.IsSkip))
            {
                MessageBox.Show(this, "Every block is set to (Skip). Pick a family for at least one block.",
                    "CAD2Revit", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Action = action;
            Close();
        }

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
            RememberFolder(dlg.FileName);
            UpdateStatus();
            var text = $"Applied {applied} of {_s.Rows.Count} blocks from\n{dlg.FileName}";
            int notInDwg = mapping.Rows.Keys.Count(k => !_s.Rows.Any(r => r.BlockName.Equals(k, StringComparison.OrdinalIgnoreCase)));
            if (notInDwg > 0) text += $"\n\n{notInDwg} mapped block(s) in the file are not in this DWG (ignored).";
            if (messages.Count > 0) text += "\n\n" + string.Join("\n", messages.Take(20)) + (messages.Count > 20 ? "\n..." : "");
            MessageBox.Show(this, text, "CAD2Revit", MessageBoxButton.OK,
                messages.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        void SaveMapping()
        {
            CommitEdits();
            var dlg = new SaveFileDialog
            {
                Title = "Save mapping",
                Filter = "Excel workbook (*.xlsx)|*.xlsx|CSV UTF-8 (*.csv)|*.csv",
                FileName = "cad2revit_mapping.xlsx",
            };
            TrySetInitialDir(dlg);
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                Mapping.Save(dlg.FileName, _s.Rows.Select(r => r.ToMapRow()));
                RememberFolder(dlg.FileName);
                MessageBox.Show(this, "Mapping saved:\n" + dlg.FileName, "CAD2Revit");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the mapping:\n" + ex.Message, "CAD2Revit", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void AutoMatch()
        {
            CommitEdits();
            int n = _s.AutoMatch();
            UpdateStatus();
            MessageBox.Show(this, n == 0
                    ? "No further close matches found. Rows already mapped are not changed."
                    : $"Pre-selected a family for {n} block(s) whose names closely match. Please check them.",
                "CAD2Revit");
        }

        void TrySetInitialDir(FileDialog dlg)
        {
            try
            {
                var last = _s.Settings?.LastMappingPath;
                if (!string.IsNullOrEmpty(last) && Directory.Exists(Path.GetDirectoryName(last)))
                    dlg.InitialDirectory = Path.GetDirectoryName(last);
            }
            catch (Exception) { }
        }

        void RememberFolder(string path)
        {
            if (_s.Settings == null) return;
            _s.Settings.LastMappingPath = path;
            Settings.TrySave(_s.Settings);
        }
    }
}
