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
        /// <summary>Starting Level for every row (the DWG's level); rows can change it.</summary>
        public Level Level;
        public bool IncludeNested;
    }

    /// <summary>Step 1 of Place Families: pick the DWG link. Levels, families and hosts are
    /// chosen per row in the MappingWindow that opens next.</summary>
    public class PlaceDialog : Form
    {
        readonly ComboBox _dwg = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        readonly CheckBox _nested = new CheckBox { Text = "Include blocks nested inside other blocks", AutoSize = true };

        public PlaceOptions Result { get; private set; }
        Document _doc;

        public PlaceDialog(Document doc, Settings settings)
        {
            Text = "CAD2Revit - Place Families (1/2)";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new System.Drawing.Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(560, 170);

            var imports = Items.Imports(doc);
            _dwg.Items.AddRange(imports.ToArray());
            if (imports.Count > 0) _dwg.SelectedIndex = 0;
            _doc = doc;
            _nested.Checked = settings.IncludeNestedBlocks;

            var grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, RowCount = 3, Padding = new Padding(12), AutoSize = true };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label L(string t) => new Label { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 6) };
            grid.Controls.Add(L("DWG link / import"), 0, 0);
            grid.Controls.Add(_dwg, 1, 0);
            grid.SetColumnSpan(_dwg, 2);
            grid.Controls.Add(_nested, 1, 1);
            grid.SetColumnSpan(_nested, 2);
            var note = new Label
            {
                Text = "Next: map each CAD block to a Revit family type, level and host, then Preview or Run. " +
                       "Each row's Level starts at the level the DWG is linked on.",
                ForeColor = Color.DimGray, AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(0, 8, 0, 0),
            };
            grid.Controls.Add(note, 0, 2);
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
            if (_dwg.SelectedItem == null)
            {
                MessageBox.Show(this, "Select a DWG.", Text);
                return;
            }
            var imp = ((Item<ImportInstance>)_dwg.SelectedItem).Element;
            Result = new PlaceOptions
            {
                Import = imp,
                Level = Items.DefaultLevel(_doc, imp),
                IncludeNested = _nested.Checked,
            };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
