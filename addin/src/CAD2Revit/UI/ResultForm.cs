using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CAD2Revit.UI
{
    /// <summary>Shows a text report (summary tables) with Copy / Open log / Close and an
    /// optional extra action button.</summary>
    public class ResultForm : Form
    {
        public ResultForm(string title, string text, string logPath = null, string actionText = null, Action action = null)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(860, 560);
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);
            var box = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9.5f), Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"), BackColor = SystemColors.Window,
            };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
            var close = new Button { Text = "Close", Width = 100, DialogResult = DialogResult.Cancel };
            var copy = new Button { Text = "Copy", Width = 100 };
            copy.Click += (s, e) => Clipboard.SetText(box.Text);
            buttons.Controls.Add(close);
            buttons.Controls.Add(copy);
            if (logPath != null && File.Exists(logPath))
            {
                var open = new Button { Text = "Open log", Width = 100 };
                open.Click += (s, e) => Start(logPath);
                var folder = new Button { Text = "Log folder", Width = 100 };
                folder.Click += (s, e) => Start("explorer.exe", "/select,\"" + logPath + "\"");
                buttons.Controls.Add(open);
                buttons.Controls.Add(folder);
            }
            if (action != null)
            {
                var act = new Button { Text = actionText, AutoSize = true, MinimumSize = new Size(100, 0) };
                act.Click += (s, e) => action();
                buttons.Controls.Add(act);
            }
            var header = new Label
            {
                Text = title.Replace("CAD2Revit - ", ""), Dock = DockStyle.Top, AutoSize = false, Height = 40,
                Font = new Font("Segoe UI Semibold", 11f), ForeColor = Color.FromArgb(32, 96, 176),
                BackColor = Color.FromArgb(232, 238, 247), Padding = new Padding(12, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft,
            };
            Controls.Add(box);
            Controls.Add(header);
            Controls.Add(buttons);
            CancelButton = close;
            box.Select(0, 0);
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
