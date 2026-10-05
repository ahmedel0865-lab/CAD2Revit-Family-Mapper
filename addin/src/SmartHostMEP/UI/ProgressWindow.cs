using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SmartHostMEP.UI
{
    /// <summary>
    /// Modeless progress bar with a Cancel button, shown while families are placed.
    /// Revit's API runs on the UI thread, so the window is refreshed by pumping messages a
    /// few times per second (<see cref="Report"/>). Revit's main window is disabled meanwhile,
    /// so only this window can be clicked. Cancel = the whole run is rolled back.
    /// </summary>
    public class ProgressWindow : Form
    {
        [DllImport("user32.dll")]
        static extern bool EnableWindow(IntPtr hWnd, bool enable);

        readonly ProgressBar _bar = new ProgressBar { Dock = DockStyle.Top, Height = 22, Minimum = 0, Maximum = 1000 };
        readonly Label _label = new Label { Dock = DockStyle.Top, Height = 34, TextAlign = ContentAlignment.MiddleLeft };
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly IntPtr _owner;
        long _lastPump = -1000;

        public bool Cancelled { get; private set; }

        public ProgressWindow(string title, IntPtr owner)
        {
            _owner = owner;
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(440, 120);
            Font = new Font("Segoe UI", 9f);
            Padding = new Padding(12);
            TopMost = true;
            var cancel = new Button { Text = "Cancel", Width = 100, Dock = DockStyle.Right };
            cancel.Click += (s, e) =>
            {
                Cancelled = true;
                cancel.Enabled = false;
                _label.Text = "Cancelling - rolling back...";
            };
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 30 };
            bottom.Controls.Add(cancel);
            Controls.Add(bottom);
            Controls.Add(_bar);
            Controls.Add(_label);
            _label.Text = "Starting...";
            if (_owner != IntPtr.Zero) EnableWindow(_owner, false);
            Show();
            Pump();
        }

        /// <summary>Update the bar. Returns false once Cancel was clicked.</summary>
        public bool Report(int done, int total)
        {
            if (Cancelled) return false;
            if (_clock.ElapsedMilliseconds - _lastPump < 150 && done < total) return true;
            _bar.Value = total > 0 ? Math.Min(1000, (int)(1000L * done / total)) : 0;
            var secs = _clock.Elapsed.TotalSeconds;
            string eta = done > 0 && done < total ? $"  -  about {Math.Max(1, secs / done * (total - done)):0} s left" : "";
            _label.Text = $"Placing block {Math.Min(done + 1, total):N0} of {total:N0}{eta}";
            Pump();
            return !Cancelled;
        }

        void Pump()
        {
            _lastPump = _clock.ElapsedMilliseconds;
            Refresh();
            Application.DoEvents();
        }

        protected override void Dispose(bool disposing)
        {
            if (_owner != IntPtr.Zero) EnableWindow(_owner, true);
            base.Dispose(disposing);
        }
    }
}
