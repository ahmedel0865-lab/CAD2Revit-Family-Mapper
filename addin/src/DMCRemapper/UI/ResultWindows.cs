using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using DMCRemapper.Core;
using Microsoft.Win32;

namespace DMCRemapper.UI
{
    /// <summary>Shared look of the small DMC Remapper windows: themed, resizable, owned by the
    /// mapping window when it is open, else by Revit.</summary>
    static class Dialogs
    {
        const double S = Theme.Space;

        public static void Prepare(Window win, string title, double width, double height, Window owner)
        {
            Theme.Apply(win);
            win.Title = title;
            win.Width = width;
            win.Height = height;
            win.ShowInTaskbar = false;
            if (owner != null && owner.IsLoaded)
            {
                win.Owner = owner;
                win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                RevitOwner.Attach(win);
                win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            win.PreviewKeyDown += (o, e) => { if (e.Key == Key.Escape) { win.Close(); e.Handled = true; } };
        }

        /// <summary>Coloured headline band (green = all good, amber = something to check).</summary>
        public static Border Headline(string text, bool good, string sub = null)
        {
            var stack = new StackPanel();
            var t = Theme.Text(text, 15, bold: true);
            t.TextWrapping = TextWrapping.Wrap;
            t.SetResourceReference(TextBlock.ForegroundProperty, good ? "Sh.Good" : "Sh.Warn");
            stack.Children.Add(t);
            if (!string.IsNullOrEmpty(sub))
            {
                var s = Theme.Text(sub, Theme.LabelSize, muted: true);
                s.TextWrapping = TextWrapping.Wrap;
                s.Margin = new Thickness(0, S / 2, 0, 0);
                stack.Children.Add(s);
            }
            var b = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(2 * S, 1.5 * S, 2 * S, 1.5 * S), Child = stack, Margin = new Thickness(0, 0, 0, 2 * S) };
            b.SetResourceReference(Border.BackgroundProperty, good ? "Sh.GoodSoft" : "Sh.WarnSoft");
            return b;
        }

        public static TextBox ReadOnlyText(string text, bool mono = false)
        {
            var box = new TextBox
            {
                Text = text ?? "", IsReadOnly = true, TextWrapping = mono ? TextWrapping.NoWrap : TextWrapping.Wrap, AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = mono ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(S),
            };
            if (mono) { box.FontFamily = new System.Windows.Media.FontFamily("Consolas"); box.FontSize = Theme.SmallSize; }
            return box;
        }

        public static void Start(Window owner, string file, string args = null)
        {
            try { Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(owner, ex.Message, "DMC Remapper"); }
        }
    }

    /// <summary>
    /// Result of Preview / Run: a one-line headline (green when everything was placed), placed count
    /// per family and plain warnings. Timings, ids and the raw log stay behind "Show details".
    /// </summary>
    public class ResultWindow : Window
    {
        const double S = Theme.Space;

        public ResultWindow(SimpleReport report, string detailsText, string logPath, string footnote = null, Window owner = null)
        {
            Dialogs.Prepare(this, report.Preview ? "DMC Remapper - Preview" : "DMC Remapper - Done", 680, 520, owner);
            MinWidth = 480;
            MinHeight = 340;

            var root = new Grid { Margin = new Thickness(2 * S) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = Dialogs.Headline(report.Headline, report.AllPlaced, report.Preview ? "Preview: nothing was changed in the model." : null);
            Grid.SetRow(head, 0);
            root.Children.Add(head);

            var body = Dialogs.ReadOnlyText(BodyText(report, footnote));
            body.BorderThickness = new Thickness(0);
            body.Background = System.Windows.Media.Brushes.Transparent;
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var details = Dialogs.ReadOnlyText(detailsText, mono: true);
            details.Height = 240;
            var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, S, 0, 0) };
            links.Children.Add(Theme.Button(this, "Copy", (o, e) => { try { Clipboard.SetText(details.Text); } catch (Exception) { } }));
            if (logPath != null && File.Exists(logPath))
            {
                links.Children.Add(Theme.Button(this, "Open log", (o, e) => Dialogs.Start(this, logPath)));
                links.Children.Add(Theme.Button(this, "Log folder", (o, e) => Dialogs.Start(this, "explorer.exe", "/select,\"" + logPath + "\"")));
            }
            var detailPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, S, 0, 0) };
            detailPanel.Children.Add(details);
            detailPanel.Children.Add(links);
            Grid.SetRow(detailPanel, 2);
            root.Children.Add(detailPanel);

            var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 2 * S, 0, 0) };
            Button toggle = null;
            toggle = Theme.Button(this, "Show details", (o, e) =>
            {
                bool show = detailPanel.Visibility != Visibility.Visible;
                detailPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                toggle.Content = show ? "Hide details" : "Show details";
                if (show && Height < 700) Height = 760;
            }, "Timings, warnings and the log file");
            DockPanel.SetDock(toggle, Dock.Left);
            footer.Children.Add(toggle);
            var close = Theme.Button(this, "Close", (o, e) => Close(), primary: true);
            close.Margin = new Thickness(0);
            close.IsDefault = true;
            DockPanel.SetDock(close, Dock.Right);
            footer.Children.Add(close);
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);
            Content = root;
            Loaded += (o, e) => close.Focus();
        }

        /// <summary>"What was done" per family, then "Warnings" only if there are any.</summary>
        public static string BodyText(SimpleReport report, string footnote = null)
        {
            var lines = new List<string>();
            lines.AddRange(report.HostCountLines);
            if (report.SlabFallbacks > 0) lines.AddRange(report.SlabFallbackLines);
            if (report.HostCountLines.Count > 0 || report.SlabFallbacks > 0) lines.Add("");
            lines.Add(report.Preview ? "What would be placed:" : "What was placed:");
            if (report.ByFamily.Count == 0) lines.Add("   nothing");
            int width = report.ByFamily.Count == 0 ? 0 : report.ByFamily.Max(kv => kv.Value.ToString().Length);
            foreach (var kv in report.ByFamily)
                lines.Add("   " + kv.Value.ToString().PadLeft(width) + "  x  " + kv.Key);
            if (report.Warnings.Count > 0)
            {
                lines.Add("");
                lines.Add($"Warnings ({report.Warnings.Count}):");
                lines.AddRange(report.Warnings.Select(w => "   - " + w));
            }
            if (!string.IsNullOrEmpty(footnote))
            {
                lines.Add("");
                lines.Add(footnote);
            }
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>
    /// "Needs Review": elements on a fallback host, moved far to a wall face, hosted in a link or
    /// placed farther than the review distance. Clicking a row selects and zooms to it in Revit.
    /// </summary>
    public class NeedsReviewWindow : Window
    {
        const double S = Theme.Space;
        readonly List<ReviewItem> _items;
        readonly Action<IList<long>> _select;
        readonly DataGrid _grid = new DataGrid();

        /// <param name="select">Selects these element ids in Revit and zooms to them.</param>
        public NeedsReviewWindow(List<ReviewItem> items, Action<IList<long>> select, Window owner = null)
        {
            _items = items;
            _select = select;
            Dialogs.Prepare(this, "DMC Remapper - Needs Review", 1080, 560, owner);
            MinWidth = 680;
            MinHeight = 360;

            var root = new Grid { Margin = new Thickness(2 * S) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var head = Dialogs.Headline($"{items.Count} element(s) to check", false,
                "No slab, ceiling or wall in range (placed on a fallback plane or level-based), moved more than 200 mm to reach a face, " +
                "hosted in a linked model, or placed farther than the review distance. Click a row to select and zoom to it in Revit.");
            Grid.SetRow(head, 0);
            root.Children.Add(head);

            _grid.AutoGenerateColumns = false;
            _grid.IsReadOnly = true;
            _grid.CanUserAddRows = false;
            _grid.SelectionMode = DataGridSelectionMode.Extended;
            _grid.SelectionUnit = DataGridSelectionUnit.FullRow;
            _grid.RowHeight = 30;
            DataGridTextColumn Column(string header, string path, double width, bool star = false) => new DataGridTextColumn
            {
                Header = header, Binding = new Binding(path) { Mode = BindingMode.OneWay },
                Width = star ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width),
            };
            _grid.Columns.Add(Column("Element ID", nameof(ReviewItem.ElementId), 100));
            _grid.Columns.Add(Column("Family : Type", nameof(ReviewItem.FamilyType), 260));
            _grid.Columns.Add(Column("CAD Block", nameof(ReviewItem.Block), 180));
            _grid.Columns.Add(Column("X, Y (mm)", nameof(ReviewItem.XY), 140));
            _grid.Columns.Add(Column("Reason", nameof(ReviewItem.Reason), 300, star: true));
            _grid.ItemsSource = items.Select(i => new Row(i)).ToList();
            _grid.SelectionChanged += (o, e) => SelectRows(_grid.SelectedItems.OfType<Row>().Select(r => r.Item));
            var rowStyle = new Style(typeof(DataGridRow));
            var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, TryFindResource("Sh.Selection")));
            rowStyle.Triggers.Add(selected);
            _grid.RowStyle = rowStyle;
            var frame = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = _grid, ClipToBounds = true };
            frame.SetResourceReference(Border.BorderBrushProperty, "Sh.Border");
            Grid.SetRow(frame, 1);
            root.Children.Add(frame);

            var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 2 * S, 0, 0) };
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(Theme.Button(this, "Select all in Revit", (o, e) => SelectRows(_items), "Select every listed element in Revit"));
            Button copy = null;
            copy = Theme.Button(this, "Copy IDs", (o, e) =>
            {
                var ids = NeedsReview.CopyIds(_items);
                if (ids.Length > 0) try { Clipboard.SetText(ids); copy.Content = "Copied"; } catch (Exception) { }
            }, "Copy the element ids (for Select by ID)");
            left.Children.Add(copy);
            left.Children.Add(Theme.Button(this, "Export to Excel...", (o, e) => Export(), "Save the list as XLSX or CSV"));
            DockPanel.SetDock(left, Dock.Left);
            footer.Children.Add(left);
            var close = Theme.Button(this, "Close", (o, e) => Close(), primary: true);
            close.Margin = new Thickness(0);
            DockPanel.SetDock(close, Dock.Right);
            footer.Children.Add(close);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);
            Content = root;
        }

        /// <summary>Grid row (ReviewItem has fields, which WPF cannot bind to).</summary>
        public class Row
        {
            public readonly ReviewItem Item;
            public Row(ReviewItem item) { Item = item; }
            public string ElementId => Item.ElementId?.ToString() ?? "";
            public string FamilyType => Item.FamilyType;
            public string Block => Item.Block;
            public string XY => Item.XY;
            public string Reason => Item.Reason;
        }

        void SelectRows(IEnumerable<ReviewItem> rows)
        {
            var ids = rows.Where(r => r.ElementId.HasValue).Select(r => r.ElementId.Value).ToList();
            if (ids.Count == 0) return;
            try { _select(ids); }
            catch (Exception ex) { MessageBox.Show(this, "Could not select in Revit:\n" + ex.Message, "DMC Remapper"); }
        }

        void Export()
        {
            var dlg = new SaveFileDialog
            {
                Title = "Export Needs Review list",
                Filter = "Excel workbook (*.xlsx)|*.xlsx|CSV UTF-8 (*.csv)|*.csv",
                FileName = "dmc_needs_review.xlsx",
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                Tables.WriteTable(dlg.FileName, NeedsReview.Header, NeedsReview.Rows(_items));
                MessageBox.Show(this, "Saved:\n" + dlg.FileName, "DMC Remapper");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save:\n" + ex.Message, "DMC Remapper", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>A question with a few large choice buttons (title + one line each); Esc = the last choice.</summary>
    public class ChoiceDialog : Window
    {
        const double S = Theme.Space;
        public int Choice { get; private set; }

        public ChoiceDialog(string title, string instruction, string content, (string text, string detail)[] choices, Window owner = null)
        {
            Dialogs.Prepare(this, title, 560, 420, owner);
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            Choice = choices.Length - 1;
            var stack = new StackPanel { Margin = new Thickness(2 * S) };
            var head = Theme.Text(instruction, 15, bold: true);
            head.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(head);
            var body = Theme.Text(content, Theme.LabelSize, muted: true);
            body.TextWrapping = TextWrapping.Wrap;
            body.Margin = new Thickness(0, S, 0, 2 * S);
            stack.Children.Add(body);
            for (int i = 0; i < choices.Length; i++)
            {
                int index = i;
                var label = new StackPanel();
                label.Children.Add(Theme.Text(choices[i].text, 13, bold: true));
                var d = Theme.Text(choices[i].detail, Theme.SmallSize, muted: true);
                d.TextWrapping = TextWrapping.Wrap;
                label.Children.Add(d);
                var b = new Button
                {
                    Content = label, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(2 * S, S, 2 * S, S),
                    Margin = new Thickness(0, 0, 0, S), IsDefault = i == 0,
                };
                b.Click += (o, e) => { Choice = index; Close(); };
                stack.Children.Add(b);
            }
            Content = stack;
        }
    }
}
