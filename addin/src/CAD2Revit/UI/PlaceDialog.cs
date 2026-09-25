using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Settings = CAD2Revit.Core.Settings;
using Color = System.Drawing.Color;
using Form = System.Windows.Forms.Form;
using Control = System.Windows.Forms.Control;

namespace CAD2Revit.UI
{
    public class PlaceOptions
    {
        public ImportInstance Import;
        public Level Level;
        public bool IncludeNested;
    }

    /// <summary>Step 1 of Place Families: pick the DWG link and the target level.
    /// The mapping itself is done in the MappingWindow that opens next.</summary>
    public class PlaceDialog : Form
    {
        readonly ComboBox _dwg = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        readonly ComboBox _level = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        readonly CheckBox _nested = new CheckBox { Text = "Include blocks nested inside other blocks", AutoSize = true };

        public PlaceOptions Result { get; private set; }

        public PlaceDialog(Document doc, Settings settings)
        {
            Text = "CAD2Revit - Place Families (1/2)";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new System.Drawing.Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(560, 200);

            var imports = Items.Imports(doc);
            _dwg.Items.AddRange(imports.ToArray());
            if (imports.Count > 0) _dwg.SelectedIndex = 0;
            var levels = Items.Levels(doc);
            _level.Items.AddRange(levels.ToArray());
            if (levels.Count > 0)
                _level.SelectedIndex = Items.DefaultLevelIndex(doc, levels, imports.Count > 0 ? imports[0].Element : null);
            _nested.Checked = settings.IncludeNestedBlocks;

            var grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, RowCount = 4, Padding = new Padding(12), AutoSize = true };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label L(string t) => new Label { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 6) };
            grid.Controls.Add(L("DWG link / import"), 0, 0);
            grid.Controls.Add(_dwg, 1, 0);
            grid.SetColumnSpan(_dwg, 2);
            grid.Controls.Add(L("Target level"), 0, 1);
            grid.Controls.Add(_level, 1, 1);
            grid.SetColumnSpan(_level, 2);
            grid.Controls.Add(_nested, 1, 2);
            grid.SetColumnSpan(_nested, 2);
            var note = new Label
            {
                Text = "Next: map each CAD block to a Revit family type, then Preview or Run.",
                ForeColor = Color.DimGray, AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(0, 8, 0, 0),
            };
            grid.Controls.Add(note, 0, 3);
            grid.SetColumnSpan(note, 3);

            var next = new Button { Text = "Next >", Width = 90 };
            var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            next.Click += (s, e) => Finish();
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 0, 12, 12), AutoSize = true,
            };
            buttons.Controls.AddRange(new Control[] { cancel, next });
            Controls.Add(grid);
            Controls.Add(buttons);
            AcceptButton = next;
            CancelButton = cancel;
        }

        void Finish()
        {
            if (_dwg.SelectedItem == null || _level.SelectedItem == null)
            {
                MessageBox.Show(this, "Select a DWG and a level.", Text);
                return;
            }
            Result = new PlaceOptions
            {
                Import = ((Item<ImportInstance>)_dwg.SelectedItem).Element,
                Level = ((Item<Level>)_level.SelectedItem).Element,
                IncludeNested = _nested.Checked,
            };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
