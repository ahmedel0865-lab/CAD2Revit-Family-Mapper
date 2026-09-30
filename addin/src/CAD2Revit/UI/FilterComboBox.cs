using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace CAD2Revit.UI
{
    /// <summary>
    /// Editable ComboBox that filters its items while you type (every typed word
    /// must appear in the item text, in any order, case-insensitive).
    /// ItemsSource must be an ICollectionView owned by this row (each row gets
    /// its own view, so filtering one row does not affect the others).
    /// Enter picks the first match; Esc or leaving the box restores the previous
    /// choice if nothing new was picked.
    /// </summary>
    public class FilterComboBox : ComboBox
    {
        TextBox _editor;
        bool _busy;

        public FilterComboBox()
        {
            IsEditable = true;
            IsTextSearchEnabled = false;
            StaysOpenOnEdit = true;
            MinWidth = 220;
        }

        ICollectionView View => ItemsSource as ICollectionView;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            if (_editor != null) _editor.TextChanged -= OnTextChanged;
            _editor = GetTemplateChild("PART_EditableTextBox") as TextBox;
            if (_editor != null) _editor.TextChanged += OnTextChanged;
        }

        void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_busy || View == null || !IsKeyboardFocusWithin) return;
            var text = _editor.Text ?? "";
            if (SelectedItem != null && SelectedItem.ToString() == text)
            {
                ClearFilter();
                return;
            }
            var terms = text.Split(new[] { ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
            _busy = true;
            try
            {
                int caret = _editor.CaretIndex;
                View.Filter = terms.Length == 0 ? null : (Predicate<object>)(o =>
                {
                    var s = o?.ToString() ?? "";
                    return terms.All(t => s.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
                });
                if (!IsDropDownOpen) IsDropDownOpen = true;
                // Filtering can reset the text box; put back what the user typed.
                if (_editor.Text != text) _editor.Text = text;
                _editor.CaretIndex = Math.Min(caret, text.Length);
                _editor.SelectionLength = 0;
            }
            finally
            {
                _busy = false;
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Enter && View != null)
            {
                var first = View.Cast<object>().FirstOrDefault();
                if (first != null && (SelectedItem == null || SelectedItem.ToString() != _editor?.Text))
                    SelectedItem = first;
                IsDropDownOpen = false;
                Restore();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                IsDropDownOpen = false;
                Restore();
                e.Handled = true;
                return;
            }
            base.OnPreviewKeyDown(e);
        }

        protected override void OnDropDownClosed(EventArgs e)
        {
            base.OnDropDownClosed(e);
            Restore();
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            if (!IsKeyboardFocusWithin) Restore();
        }

        void ClearFilter()
        {
            if (View != null && View.Filter != null) View.Filter = null;
        }

        /// <summary>Remove the filter and show the row's actual value again.</summary>
        void Restore()
        {
            _busy = true;
            try
            {
                ClearFilter();
                // A filter can push SelectedItem to null; the row model ignores null, so
                // re-read the bound value to resync the box with the row.
                GetBindingExpression(Selector.SelectedItemProperty)?.UpdateTarget();
                if (_editor != null && SelectedItem != null) _editor.Text = SelectedItem.ToString();
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
