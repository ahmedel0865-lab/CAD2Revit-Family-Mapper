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
            Width = 1450;
            Height = 680;
            MinWidth = 1000;
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
                Text = $"DWG: {_s.DwgName}     Level: {_s.LevelName}     " +
                       $"{_s.Rows.Count} unique blocks, {_s.InstanceCount} instances (sorted by block name).\n" +
                       "Pick a family for each block (type in the box to search). (Skip) = do not place. " +
                       "Elevation is from the target level, in mm. " +
                       "Facing (Down/Up) applies to Reference Plane hosting.",
            };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // Find box: filters the rows by block name or chosen family.
            var findBar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(findBar, Dock.Top);
            findBar.Children.Add(new TextBlock { Text = "Find:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var find = new TextBox { Width = 320, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Show only rows whose block or family contains this text" };
            find.TextChanged += (o, e) => ApplyFind(find.Text);
            findBar.Children.Add(find);
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

            // Grid (the row view is shared between Preview round-trips: start unfiltered)
            CollectionViewSource.GetDefaultView(_s.Rows).Filter = null;
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
            _grid.ItemsSource = _s.Rows;

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

            // 3. Elevation From Level (mm), validated
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

        void ApplyFind(string text)
        {
            CommitEdits();   // changing the filter during a cell edit throws
            var view = CollectionViewSource.GetDefaultView(_s.Rows);
            var terms = (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            view.Filter = terms.Length == 0 ? null : (Predicate<object>)(o =>
            {
                var r = (BlockRow)o;
                var hay = r.BlockName + " " + r.FamilyLabel;
                return terms.All(t => hay.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
            });
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
