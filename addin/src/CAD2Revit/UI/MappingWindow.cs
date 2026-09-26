using System;
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

        public MappingWindow(MappingSession session)
        {
            _s = session;
            Title = "CAD2Revit - Map CAD blocks to Revit families";
            Width = 1680;
            Height = 680;
            MinWidth = 1150;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12;

            var root = new DockPanel { Margin = new Thickness(12) };

            // Header
            var header = new TextBlock
            {
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap,
                Text = $"DWG: {_s.DwgName}     Default level: {_s.LevelName}     " +
                       $"{_s.Rows.Count} unique blocks, {_s.InstanceCount} instances (sorted by block name).\n" +
                       "Pick a family for each block (type in the box to search). (Skip) = do not place. " +
                       "Elevation is from the row's Level, in mm (Level defaults to the one picked in step 1). " +
                       "Facing (Down/Up) applies to Reference Plane hosting.",
            };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // Find box: filters the rows by block name or chosen family.
            var findBar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(findBar, Dock.Top);
            findBar.Children.Add(new TextBlock { Text = "Find:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var find = new TextBox { Width = 320, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Show only rows whose block or family contains this text" };
            find.TextChanged += (o, e) => { _findText = find.Text; ApplyFilter(); };
            findBar.Children.Add(find);
            // Category filter: show one discipline at a time (rows stay grouped by category).
            findBar.Children.Add(new TextBlock { Text = "Show:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 6, 0) });
            var show = new ComboBox { Width = 190, VerticalContentAlignment = VerticalAlignment.Center };
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
            findBar.Children.Add(show);
            var skipShown = new Button
            {
                Content = "Skip shown rows", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 1, 8, 1),
                ToolTip = "Set every row currently shown (after Find / Show) to (Skip), e.g. all Architectural blocks",
            };
            skipShown.Click += (o, e) => SkipShownRows();
            findBar.Children.Add(skipShown);
            // Global switch: every row -> Reference Plane (unticking restores each row's previous host).
            var allPlanes = new CheckBox
            {
                Content = "Use reference planes for all rows",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(24, 0, 0, 0),
                ToolTip = "Sets Host Type to 'Reference Plane (auto-create)' for every row. " +
                          "Untick to restore the previous Host Types.",
                IsChecked = _s.Rows.Count > 0 && _s.Rows.All(r => r.Host == BlockRow.RefPlaneLabel),
            };
            allPlanes.Checked += (o, e) => SetAllReferencePlanes(true);
            allPlanes.Unchecked += (o, e) => SetAllReferencePlanes(false);
            findBar.Children.Add(allPlanes);
            findBar.Children.Add(new TextBlock());
            root.Children.Add(findBar);

            // Buttons
            var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
            DockPanel.SetDock(bottom, Dock.Bottom);
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(MakeButton("Load Mapping...", (s, e) => LoadMapping()));
            left.Children.Add(MakeButton("Save Mapping...", (s, e) => SaveMapping()));
            left.Children.Add(MakeButton("Auto-match", (s, e) => AutoMatch()));
            left.Children.Add(_status);
            DockPanel.SetDock(left, Dock.Left);
            bottom.Children.Add(left);
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            var preview = MakeButton("Preview", (s, e) => Finish(MappingAction.Preview));
            preview.IsDefault = false;
            right.Children.Add(preview);
            right.Children.Add(MakeButton("Run", (s, e) => Finish(MappingAction.Run)));
            var cancel = MakeButton("Cancel", (s, e) => { Action = MappingAction.Cancel; Close(); });
            cancel.IsCancel = true;
            right.Children.Add(cancel);
            DockPanel.SetDock(right, Dock.Right);
            bottom.Children.Add(right);
            root.Children.Add(bottom);

            // Grid (the row view is shared between Preview round-trips: start unfiltered,
            // grouped by category, Electrical first, then by block name).
            SetUpRowView();
            BuildGrid();
            root.Children.Add(_grid);
            Content = root;

            foreach (var row in _s.Rows) row.PropertyChanged += OnRowChanged;
            Closed += (o, e) => { foreach (var row in _s.Rows) row.PropertyChanged -= OnRowChanged; };
            UpdateStatus();
        }

        static Button MakeButton(string text, RoutedEventHandler click)
        {
            var b = new Button { Content = text, MinWidth = 90, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
            b.Click += click;
            return b;
        }

        void BuildGrid()
        {
            _grid.AutoGenerateColumns = false;
            _grid.CanUserAddRows = false;
            _grid.CanUserDeleteRows = false;
            _grid.CanUserReorderColumns = false;
            _grid.SelectionMode = DataGridSelectionMode.Single;
            _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            _grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            _grid.RowHeight = 28;
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

        static Style TrimmedTextStyle()
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(nameof(BlockRow.Display))));
            return style;
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
            _status.Text = $"{mapped} of {_s.Rows.Count} blocks mapped ({instances} instances)" +
                           (bad > 0 ? $"   -   {bad} row(s) with invalid numbers" : "");
            _status.Foreground = bad > 0 ? Brushes.Firebrick : Brushes.DimGray;
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
