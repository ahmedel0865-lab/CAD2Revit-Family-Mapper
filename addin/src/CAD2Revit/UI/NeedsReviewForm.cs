using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CAD2Revit.Core;

namespace CAD2Revit.UI
{
    /// <summary>
    /// "Needs Review": every Slab (above) / Ceiling element that found no host and went on the fallback
    /// reference plane (or level-based fallback), and wall-hosted elements to check (no wall in range,
    /// moved more than 200 mm to the face, wall in a link). Clicking a row selects and zooms to it in Revit.
    /// </summary>
    public class NeedsReviewForm : Form
    {
        readonly List<ReviewItem> _items;
        readonly Action<IList<long>> _select;
        readonly ListView _list;

        /// <param name="select">Selects these element ids in Revit and zooms to them.</param>
        public NeedsReviewForm(List<ReviewItem> items, Action<IList<long>> select)
        {
            _items = items;
            _select = select;
            Text = "CAD2Revit - Needs Review";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Color.White;
            ClientSize = new Size(1020, 480);
            MinimumSize = new Size(640, 320);

            var header = new Label
            {
                Text = $"{items.Count} element(s) to check: no slab/beam, ceiling or wall in range (placed on a fallback " +
                       $"reference plane, or level-based), moved more than 200 mm to reach a wall face, or hosted on a wall in a " +
                       $"linked model, or placed farther than the review distance from its CAD block. Click a row to select and zoom to it in Revit. Fallback elements' Comments say \"{NeedsReview.CommentText}\".",
                Dock = DockStyle.Top, Height = 52, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 14, 0), ForeColor = Color.FromArgb(166, 98, 0), BackColor = Color.FromArgb(255, 243, 224),
            };

            _list = new ListView
            {
                View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false, MultiSelect = true,
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
            };
            _list.Columns.Add("Element ID", 90);
            _list.Columns.Add("Family : Type", 250);
            _list.Columns.Add("CAD Block", 170);
            _list.Columns.Add("X, Y (mm)", 150);
            _list.Columns.Add("Reason", 330);
            foreach (var it in items)
            {
                var lvi = new ListViewItem(it.ElementId?.ToString() ?? "") { Tag = it };
                lvi.SubItems.Add(it.FamilyType);
                lvi.SubItems.Add(it.Block);
                lvi.SubItems.Add(it.XY);
                lvi.SubItems.Add(it.Reason);
                _list.Items.Add(lvi);
            }
            _list.ItemActivate += (s, e) => SelectRows(Selected());
            _list.MouseClick += (s, e) => SelectRows(Selected());
            var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 4) };
            listHost.Controls.Add(_list);

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(12, 8, 12, 8),
            };
            var selectAll = new Button { Text = "Select All in Revit", AutoSize = true, MinimumSize = new Size(130, 0) };
            selectAll.Click += (s, e) => SelectRows(_items);
            var copy = new Button { Text = "Copy IDs", AutoSize = true, MinimumSize = new Size(100, 0) };
            copy.Click += (s, e) =>
            {
                var ids = NeedsReview.CopyIds(_items);
                if (ids.Length > 0) Clipboard.SetText(ids);
                copy.Text = "Copied";
            };
            var export = new Button { Text = "Export to Excel", AutoSize = true, MinimumSize = new Size(120, 0) };
            export.Click += (s, e) => Export();
            var close = new Button { Text = "Close", Width = 100, DialogResult = DialogResult.Cancel };
            bottom.Controls.AddRange(new Control[] { selectAll, copy, export, close });

            Controls.Add(listHost);
            Controls.Add(bottom);
            Controls.Add(header);
            CancelButton = close;
        }

        List<ReviewItem> Selected() => _list.SelectedItems.Cast<ListViewItem>().Select(i => (ReviewItem)i.Tag).ToList();

        void SelectRows(IEnumerable<ReviewItem> rows)
        {
            var ids = rows.Where(r => r.ElementId.HasValue).Select(r => r.ElementId.Value).ToList();
            if (ids.Count == 0) return;
            try { _select(ids); }
            catch (Exception ex) { MessageBox.Show(this, "Could not select in Revit:\n" + ex.Message, "CAD2Revit"); }
        }

        void Export()
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "Export Needs Review list",
                Filter = "Excel workbook (*.xlsx)|*.xlsx|CSV UTF-8 (*.csv)|*.csv",
                FileName = "cad2revit_needs_review.xlsx",
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Tables.WriteTable(dlg.FileName, NeedsReview.Header, NeedsReview.Rows(_items));
                    MessageBox.Show(this, "Saved:\n" + dlg.FileName, "CAD2Revit");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save:\n" + ex.Message, "CAD2Revit", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
