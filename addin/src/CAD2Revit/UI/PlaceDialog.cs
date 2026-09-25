using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
        public string MappingPath;
        public Level Level;
        public bool Preview;
        public bool IncludeNested;
    }

    /// <summary>The main dialog: DWG, mapping file, level, nested blocks, Preview / Run.</summary>
    public class PlaceDialog : Form
    {
        readonly ComboBox _dwg = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        readonly TextBox _map = new TextBox { Dock = DockStyle.Fill };
        readonly ComboBox _level = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        readonly CheckBox _nested = new CheckBox { Text = "Include blocks nested inside other blocks", AutoSize = true };

        public PlaceOptions Result { get; private set; }

        public PlaceDialog(Document doc, Settings settings)
        {
            Text = "CAD2Revit - Place Families";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new System.Drawing.Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(600, 290);

            var imports = Items.Imports(doc);
            _dwg.Items.AddRange(imports.ToArray());
            if (imports.Count > 0) _dwg.SelectedIndex = 0;
            var levels = Items.Levels(doc);
            _level.Items.AddRange(levels.ToArray());
            if (levels.Count > 0)
                _level.SelectedIndex = Items.DefaultLevelIndex(doc, levels, imports.Count > 0 ? imports[0].Element : null);
            _map.Text = settings.LastMappingPath ?? "";
            _nested.Checked = settings.IncludeNestedBlocks;

            var browse = new Button { Text = "Browse...", AutoSize = true };
            browse.Click += (s, e) => Browse();

            var grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, RowCount = 5, Padding = new Padding(12), AutoSize = true };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label L(string t) => new Label { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 6) };
            grid.Controls.Add(L("DWG link / import"), 0, 0);
            grid.Controls.Add(_dwg, 1, 0);
            grid.SetColumnSpan(_dwg, 2);
            grid.Controls.Add(L("Mapping file"), 0, 1);
            grid.Controls.Add(_map, 1, 1);
            grid.Controls.Add(browse, 2, 1);
            grid.Controls.Add(L("Target level"), 0, 2);
            grid.Controls.Add(_level, 1, 2);
            grid.SetColumnSpan(_level, 2);
            grid.Controls.Add(_nested, 1, 3);
            grid.SetColumnSpan(_nested, 2);
            var note = new Label
            {
                Text = "Preview runs the full placement and then undoes it, so the counts match a real run. " +
                       "Run places everything in one transaction (one Ctrl+Z).",
                ForeColor = Color.DimGray, AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 8, 0, 0),
            };
            grid.Controls.Add(note, 0, 4);
            grid.SetColumnSpan(note, 3);

            var preview = new Button { Text = "Preview", Width = 90 };
            var run = new Button { Text = "Run", Width = 90 };
            var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            preview.Click += (s, e) => Finish(true);
            run.Click += (s, e) => Finish(false);
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 0, 12, 12), AutoSize = true,
            };
            buttons.Controls.AddRange(new Control[] { cancel, run, preview });
            Controls.Add(grid);
            Controls.Add(buttons);
            AcceptButton = preview;
            CancelButton = cancel;
        }

        void Browse()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Select mapping file (CSV or XLSX)",
                Filter = "Mapping files (*.xlsx;*.csv)|*.xlsx;*.csv|All files (*.*)|*.*",
            })
            {
                try
                {
                    if (File.Exists(_map.Text)) dlg.InitialDirectory = Path.GetDirectoryName(_map.Text);
                }
                catch (Exception) { }
                if (dlg.ShowDialog(this) == DialogResult.OK) _map.Text = dlg.FileName;
            }
        }

        void Finish(bool preview)
        {
            var path = (_map.Text ?? "").Trim().Trim('"');
            if (_dwg.SelectedItem == null || _level.SelectedItem == null)
            {
                MessageBox.Show(this, "Select a DWG and a level.", Text);
                return;
            }
            if (!File.Exists(path))
            {
                MessageBox.Show(this, "Mapping file not found:\n" + path, Text);
                return;
            }
            Result = new PlaceOptions
            {
                Import = ((Item<ImportInstance>)_dwg.SelectedItem).Element,
                MappingPath = path,
                Level = ((Item<Level>)_level.SelectedItem).Element,
                Preview = preview,
                IncludeNested = _nested.Checked,
            };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
