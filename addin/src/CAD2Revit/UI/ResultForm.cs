using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CAD2Revit.Core;

namespace CAD2Revit.UI
{
    /// <summary>
    /// Result of Place Families: a one-line headline (green when everything was placed),
    /// placed count per family, and plain warnings. Element ids, slopes, timings and the
    /// raw log stay behind "Show details". One Close button; owned by Revit, no minimize.
    /// </summary>
    public class ResultForm : Form
    {
        static readonly Color Green = Color.FromArgb(46, 125, 50), GreenBack = Color.FromArgb(232, 245, 233);
        static readonly Color Amber = Color.FromArgb(166, 98, 0), AmberBack = Color.FromArgb(255, 243, 224);

        readonly Panel _details;
        readonly Button _toggle;
        readonly int _compactHeight;

        public ResultForm(SimpleReport report, string detailsText, string logPath, string footnote = null)
        {
            Text = report.Preview ? "CAD2Revit - Preview" : "CAD2Revit - Done";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Color.White;
            ClientSize = new Size(620, 420);
            MinimumSize = new Size(480, 320);

            // ---- 1. Headline ----------------------------------------------------------
            bool ok = report.AllPlaced;
            var headline = new Label
            {
                Text = report.Headline + (report.Preview ? "  (preview - nothing was changed)" : ""),
                Dock = DockStyle.Top, Height = 46, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 0, 0), Font = new Font("Segoe UI Semibold", 12f),
                ForeColor = ok ? Green : Amber, BackColor = ok ? GreenBack : AmberBack,
            };

            // ---- 2. What was done + 3. Warnings ----------------------------------------
            var body = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Color.White,
                Font = new Font("Segoe UI", 10f), Text = BodyText(report, footnote),
            };
            var bodyHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 10, 4) };
            bodyHost.Controls.Add(body);

            // ---- Details (hidden until "Show details") ----------------------------------
            var detailsBox = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9f), BackColor = SystemColors.Window,
                Text = (detailsText ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n"),
            };
            var detailLinks = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
            detailLinks.Controls.Add(Link("Copy", () => Clipboard.SetText(detailsBox.Text)));
            if (logPath != null && File.Exists(logPath))
            {
                detailLinks.Controls.Add(Link("Open log", () => Start(logPath)));
                detailLinks.Controls.Add(Link("Log folder", () => Start("explorer.exe", "/select,\"" + logPath + "\"")));
            }
            _details = new Panel { Dock = DockStyle.Bottom, Height = 260, Padding = new Padding(14, 4, 14, 4), Visible = false };
            _details.Controls.Add(detailsBox);
            _details.Controls.Add(detailLinks);

            // ---- Buttons: Show details (left), Close (right) -----------------------------
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(14, 8, 14, 10) };
            var close = new Button { Text = "Close", Width = 100, Dock = DockStyle.Right, DialogResult = DialogResult.Cancel };
            _toggle = new Button { Text = "Show details", Width = 120, Dock = DockStyle.Left, FlatStyle = FlatStyle.System };
            _toggle.Click += (s, e) => ToggleDetails();
            bottom.Controls.Add(close);
            bottom.Controls.Add(_toggle);

            Controls.Add(bodyHost);
            Controls.Add(_details);
            Controls.Add(bottom);
            Controls.Add(headline);
            CancelButton = close;
            AcceptButton = close;
            _compactHeight = ClientSize.Height;
            Shown += (s, e) => { body.Select(0, 0); close.Focus(); };
        }

        /// <summary>"What was done" per family, then "Warnings" only if there are any.</summary>
        public static string BodyText(SimpleReport report, string footnote = null)
        {
            var nl = Environment.NewLine;
            var lines = new System.Collections.Generic.List<string>();
            if (report.SlabFallbacks > 0)
            {
                lines.AddRange(report.SlabFallbackLines);
                lines.Add("");
            }
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
            return string.Join(nl, lines);
        }

        void ToggleDetails()
        {
            _details.Visible = !_details.Visible;
            _toggle.Text = _details.Visible ? "Hide details" : "Show details";
            ClientSize = new Size(ClientSize.Width, _details.Visible ? _compactHeight + _details.Height : _compactHeight);
        }

        static LinkLabel Link(string text, Action action)
        {
            var l = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(0, 0, 14, 0), LinkColor = Color.FromArgb(32, 96, 176) };
            l.LinkClicked += (s, e) => action();
            return l;
        }

        static void Start(string file, string args = null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "CAD2Revit");
            }
        }
    }
}
