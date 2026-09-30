using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CAD2Revit.UI
{
    public static class Pickers
    {
        /// <summary>Simple single-choice list. Returns null if cancelled.</summary>
        public static T PickOne<T>(string title, IList<T> items) where T : class
        {
            if (items.Count == 1) return items[0];
            using (var form = new Form
            {
                Text = title, StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(520, 320),
                MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, Font = new Font("Segoe UI", 9f),
            })
            {
                var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
                list.Items.AddRange(items.Cast<object>().ToArray());
                list.SelectedIndex = 0;
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
                buttons.Controls.AddRange(new Control[] { cancel, ok });
                list.DoubleClick += (s, e) => { form.DialogResult = DialogResult.OK; form.Close(); };
                form.Controls.Add(list);
                form.Controls.Add(buttons);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                return form.ShowDialog() == DialogResult.OK ? list.SelectedItem as T : null;
            }
        }
    }
}
