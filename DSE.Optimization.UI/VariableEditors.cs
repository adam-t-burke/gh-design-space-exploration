using System;
using System.Globalization;
using DSOptimization;
using Eto.Drawing;
using Eto.Forms;

namespace DSE.Optimization.UI
{
    public static class VariableEditors
    {
        public static Control Header()
        {
            return new TableLayout
            {
                Spacing = new Size(6, 0),
                Rows =
                {
                    Row(Label("Name", true), Label("Value", false), new Panel { Width = 24 }, Label("Min", false), new Panel { Width = 120 }, Label("Max", false))
                }
            };
        }

        public static Control VariableRow(VarVM variable)
        {
            TextBox name = new TextBox { Text = variable.Name, Width = 140 };
            TextBox value = Box(variable.Value);
            TextBox min = Box(variable.Min);
            TextBox max = Box(variable.Max);
            CheckBox active = new CheckBox { Checked = variable.IsActive, ToolTip = "Include in optimization" };
            MappedSlider slider = new MappedSlider();
            slider.Set(variable.Min, variable.Max, variable.Value);
            bool updating = false;

            void Pull()
            {
                if (updating)
                    return;
                updating = true;
                name.Text = variable.Name;
                value.Text = variable.Value.ToString("0.00", CultureInfo.CurrentCulture);
                min.Text = variable.Min.ToString("0.00", CultureInfo.CurrentCulture);
                max.Text = variable.Max.ToString("0.00", CultureInfo.CurrentCulture);
                active.Checked = variable.IsActive;
                active.Enabled = variable.ChangesEnabled;
                slider.Control.Enabled = variable.ChangesEnabled;
                slider.Set(variable.Min, variable.Max, variable.Value);
                updating = false;
            }

            name.LostFocus += delegate { variable.Name = name.Text ?? ""; };
            Commit(value, v => variable.Value = v, () => variable.Value);
            Commit(min, v => variable.Min = v, () => variable.Min);
            Commit(max, v => variable.Max = v, () => variable.Max);
            active.CheckedChanged += delegate
            {
                if (!updating)
                    variable.IsActive = active.Checked == true;
            };
            slider.Control.ValueChanged += delegate
            {
                if (!updating)
                    variable.Value = slider.Read();
                Pull();
            };
            variable.PropertyChanged += delegate
            {
                if (variable.OpenDialog)
                {
                    variable.OpenDialog = false;
                    MessageBox.Show("That value is outside the current bounds.", "Variable", MessageBoxType.Warning);
                }
                Pull();
            };
            return new TableLayout { Spacing = new Size(6, 2), Rows = { Row(name, value, active, min, slider.Control, max) } };
        }

        public static Control GroupRow(GroupVarVM group, string title)
        {
            Label name = new Label { Text = title, VerticalAlignment = VerticalAlignment.Center, Width = 140 };
            TextBox value = Box(group.Value);
            TextBox min = Box(group.Min);
            TextBox max = Box(group.Max);
            CheckBox active = new CheckBox { Checked = true, ToolTip = "Activate or deactivate every variable in this group" };
            MappedSlider slider = new MappedSlider();
            slider.Set(group.Min, group.Max, group.Value);
            bool updating = false;

            void Pull()
            {
                if (updating)
                    return;
                updating = true;
                value.Text = group.Value.ToString("0.00", CultureInfo.CurrentCulture);
                min.Text = group.Min.ToString("0.00", CultureInfo.CurrentCulture);
                max.Text = group.Max.ToString("0.00", CultureInfo.CurrentCulture);
                slider.Control.Enabled = group.ChangesEnabled;
                active.Enabled = group.ChangesEnabled;
                slider.Set(group.Min, group.Max, group.Value);
                updating = false;
            }

            Commit(value, v => group.Value = v, () => group.Value);
            Commit(min, v => group.Min = v, () => group.Min);
            Commit(max, v => group.Max = v, () => group.Max);
            active.CheckedChanged += delegate
            {
                if (updating || group.MyVars == null)
                    return;
                foreach (VarVM variable in group.MyVars)
                    variable.IsActive = active.Checked == true;
            };
            slider.Control.ValueChanged += delegate
            {
                if (!updating)
                    group.Value = slider.Read();
                Pull();
            };
            group.PropertyChanged += delegate
            {
                if (group.OpenDialog)
                {
                    group.OpenDialog = false;
                    MessageBox.Show("One or more variables could not be updated. Only those with compatible bounds were updated.", "Heads Up", MessageBoxType.Warning);
                }
                Pull();
            };
            return new TableLayout { Spacing = new Size(6, 2), Rows = { Row(name, value, active, min, slider.Control, max) } };
        }

        public static Control ConstraintRow(ConstVM constraint, Action changed)
        {
            TextBox name = new TextBox { Text = constraint.Name, Width = 80 };
            Label current = new Label { Text = constraint.CurrentValue.ToString("0.00"), VerticalAlignment = VerticalAlignment.Center, Width = 70 };
            CheckBox active = new CheckBox { Checked = constraint.IsActive, ToolTip = "Show this constraint and include it" };
            DropDown kind = new DropDown { DataStore = new[] { "Less Than", "Greater Than", "Equal To" }, SelectedIndex = constraint.ConstraintType, Width = 120 };
            TextBox limit = Box(constraint.ConstraintLimit);
            name.LostFocus += delegate { constraint.Name = name.Text ?? ""; };
            Commit(limit, v => constraint.ConstraintLimit = v, () => constraint.ConstraintLimit);
            kind.SelectedIndexChanged += delegate
            {
                if (kind.SelectedIndex >= 0)
                    constraint.ConstraintType = kind.SelectedIndex;
            };
            active.CheckedChanged += delegate
            {
                constraint.IsActive = active.Checked == true;
                constraint.GraphVM.GraphVisibility = constraint.IsActive ? UiVisibility.Visible : UiVisibility.Collapsed;
                if (changed != null)
                    changed();
            };
            constraint.PropertyChanged += delegate
            {
                current.Text = constraint.CurrentValue.ToString("0.00");
                active.Enabled = constraint.ChangesEnabled;
                kind.Enabled = constraint.ChangesEnabled;
            };
            return new TableLayout
            {
                Spacing = new Size(6, 2),
                Rows = { Row(name, current, active, kind, limit, new Panel()) }
            };
        }

        public static string FormatReadout(double value)
        {
            if (Math.Abs(value) > 99999)
                return value.ToString("0.0e0", CultureInfo.CurrentCulture);
            return value.ToString("0.00", CultureInfo.CurrentCulture);
        }

        private static void Commit(TextBox box, Action<double> set, Func<double> get)
        {
            EventHandler<EventArgs> commit = delegate
            {
                double parsed;
                if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed) ||
                    double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                    set(parsed);
                box.Text = get().ToString("0.00", CultureInfo.CurrentCulture);
            };
            box.LostFocus += commit;
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Keys.Enter)
                    commit(s, e);
            };
        }

        private static TextBox Box(double value)
        {
            return new TextBox { Text = value.ToString("0.00", CultureInfo.CurrentCulture), Width = 72 };
        }

        private static Label Label(string text, bool expand)
        {
            return new Label { Text = text, Width = expand ? 140 : 72, TextColor = Colors.Gray };
        }

        private static TableRow Row(Control c0, Control c1, Control c2, Control c3, Control c4, Control c5)
        {
            var row = new TableRow();
            row.Cells.Add(new TableCell(c0, true));
            row.Cells.Add(new TableCell(c1));
            row.Cells.Add(new TableCell(c2));
            row.Cells.Add(new TableCell(c3));
            row.Cells.Add(new TableCell(c4));
            row.Cells.Add(new TableCell(c5));
            return row;
        }

        private sealed class MappedSlider
        {
            public readonly Slider Control = new Slider { MinValue = 0, MaxValue = 1000, Width = 120 };
            private double _min;
            private double _max;

            public void Set(double min, double max, double value)
            {
                _min = min;
                _max = max;
                double span = max - min;
                if (span < 1e-12)
                {
                    Control.Value = 0;
                    return;
                }
                double t = (value - min) / span;
                if (t < 0) t = 0;
                if (t > 1) t = 1;
                Control.Value = (int)Math.Round(t * 1000.0);
            }

            public double Read()
            {
                double span = _max - _min;
                if (span < 1e-12)
                    return _min;
                return _min + Control.Value / 1000.0 * span;
            }
        }
    }
}
