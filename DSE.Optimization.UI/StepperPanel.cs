using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSOptimization;
using Eto.Drawing;
using Eto.Forms;
using Stepper;

namespace DSE.Optimization.UI
{
    public class StepperPanel : Panel
    {
        private readonly StepperVM _vm;
        private readonly Panel _chartHost = new Panel();
        private readonly DynamicLayout _gradients = new DynamicLayout();
        private readonly DynamicLayout _objectives = new DynamicLayout();
        private readonly List<Label> _gradientLabels = new List<Label>();
        private readonly List<Label> _objectiveLabels = new List<Label>();
        private readonly Slider _history = new Slider { MinValue = 0, MaxValue = 0, Width = 280 };
        private readonly Expander _gradientExpander;
        private readonly Expander _objectiveExpander;
        private bool _absolute;
        private bool _busy;
        private bool _syncingHistory;

        public StepperPanel(StepperVM vm)
        {
            _vm = vm;
            BuildReadouts();
            ShowChart();

            var up = new Button { Text = "STEP UP" };
            var iso = new Button { Text = "ISO-STEP" };
            var down = new Button { Text = "STEP DOWN" };
            var gradient = new Button { Text = "CALCULATE GRADIENT" };
            var reset = new Button { Text = "Reset" };
            var export = new Button { Text = "Export CSV" };
            up.Click += delegate { Run(StepperOptimizer.Direction.Maximize, false); };
            down.Click += delegate { Run(StepperOptimizer.Direction.Minimize, false); };
            iso.Click += delegate { Run(StepperOptimizer.Direction.Isoperformance, false); };
            gradient.Click += delegate { Run(StepperOptimizer.Direction.Maximize, true); };
            reset.Click += OnReset;
            export.Click += OnExport;

            var objective = new DropDown { DataStore = _vm.ObjectiveNames, SelectedIndex = Math.Max(0, _vm.ObjIndex) };
            objective.SelectedIndexChanged += delegate
            {
                if (objective.SelectedIndex >= 0)
                    _vm.ObjIndex = objective.SelectedIndex;
            };

            _history.ValueChanged += delegate
            {
                if (_syncingHistory)
                    return;
                _vm.TrackedStep = _history.Value;
                RefreshReadouts(_history.Value);
            };

            _gradientExpander = new Expander { Header = "Gradients", Expanded = false, Content = _gradients };
            _objectiveExpander = new Expander { Header = "Objective data", Expanded = true, Content = _objectives };

            var toolbar = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Padding = new Padding(8, 8, 8, 0),
                Items = { up, iso, down, gradient, reset, export }
            };
            var objectiveRow = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Padding = new Padding(8, 4),
                Items = { new Label { Text = "Active objective", VerticalAlignment = VerticalAlignment.Center }, objective }
            };
            var historyRow = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Padding = new Padding(8, 0),
                Items = { new Label { Text = "History", VerticalAlignment = VerticalAlignment.Center }, _history }
            };

            var variables = new Scrollable { Content = BuildVariables(), Width = 520, ExpandContentWidth = true };
            var center = new DynamicLayout { Padding = new Padding(4), DefaultSpacing = new Size(4, 4) };
            center.Add(objectiveRow);
            center.Add(_chartHost, yscale: true);
            center.Add(historyRow);
            center.Add(new StackLayout { Orientation = Orientation.Horizontal, Spacing = 6, Items = { up, iso, down } });
            center.Add(gradient);
            center.Add(_objectiveExpander);
            center.Add(_gradientExpander);

            var layout = new DynamicLayout { Padding = new Padding(4) };
            layout.Add(toolbar);
            layout.BeginHorizontal();
            layout.Add(variables);
            layout.Add(new Scrollable { Content = center, ExpandContentWidth = true }, xscale: true, yscale: true);
            layout.Add(new Scrollable { Content = BuildSettings(), Width = 280, ExpandContentWidth = true });
            layout.EndHorizontal();
            Content = layout;
        }

        public void ShowStartupNotices(Control parent)
        {
            if (_vm.OpenDialog)
            {
                MessageBox.Show(parent, "Stepper does not support constrained optimization and will ignore constraint inputs. If you would like your constraints to be considered, please use Radical.", "Notice");
                _vm.OpenDialog = false;
            }
        }

        public bool TryHandleKey(KeyEventArgs e, Control root)
        {
            Control focused = FindFocus(root);
            if (focused is TextBox || focused is NumericStepper || focused is DropDown)
                return false;
            if (e.Key == Keys.Up)
                Run(StepperOptimizer.Direction.Maximize, false);
            else if (e.Key == Keys.Down)
                Run(StepperOptimizer.Direction.Minimize, false);
            else if (e.Key == Keys.Left || e.Key == Keys.Right)
                Run(StepperOptimizer.Direction.Isoperformance, false);
            else
                return false;
            e.Handled = true;
            return true;
        }

        private Control BuildSettings()
        {
            var step = new NumericStepper { Value = _vm.StepSize, MinValue = 1e-6, MaxValue = 10, DecimalPlaces = 4, Increment = 0.01 };
            step.ValueChanged += delegate { _vm.StepSize = step.Value; };
            var finite = new NumericStepper { Value = _vm.FDStepSize, MinValue = 0.0001, MaxValue = 0.5, DecimalPlaces = 4, Increment = 0.01 };
            finite.ValueChanged += delegate { _vm.FDStepSize = finite.Value; };
            var absolute = new CheckBox { Text = "Absolute values", Checked = false };
            absolute.CheckedChanged += delegate
            {
                _absolute = absolute.Checked == true;
                ShowChart();
                RefreshReadouts(_history.Value);
            };
            var disabling = new CheckBox { Text = "Block component disabling", Checked = _vm.DisablingNotAllowed };
            disabling.CheckedChanged += delegate { _vm.DisablingNotAllowed = disabling.Checked == true; };

            var layout = new DynamicLayout { Padding = new Padding(8), DefaultSpacing = new Size(4, 6) };
            layout.Add(new Label { Text = "Gradient step size" });
            layout.Add(step);
            layout.Add(new Label { Text = "Internal / finite-difference step" });
            layout.Add(finite);
            layout.Add(new Label { Text = "Display mode" });
            layout.Add(new Label { Text = "Unchecked shows normalized objectives." });
            layout.Add(absolute);
            layout.Add(disabling);
            return layout;
        }

        private Control BuildVariables()
        {
            var layout = new DynamicLayout { Padding = new Padding(8), DefaultSpacing = new Size(4, 4) };
            if (_vm.NumVars != null && _vm.NumVars.Count > 0)
            {
                GroupVarVM all = new GroupVarVM(_vm, (int)VarVM.Direction.None);
                _vm.GroupVars.Add(all);
                var sliders = new DynamicLayout();
                sliders.Add(VariableEditors.Header());
                sliders.Add(VariableEditors.GroupRow(all, "All Variables"));
                foreach (VarVM variable in _vm.NumVars)
                    sliders.Add(VariableEditors.VariableRow(variable));
                layout.Add(new Expander { Header = "Sliders", Expanded = true, Content = sliders });
            }
            if (_vm.GeoVars != null)
            {
                for (int geoIndex = 0; geoIndex < _vm.GeoVars.Count; geoIndex++)
                {
                    List<VarVM> geometry = _vm.GeoVars[geoIndex];
                    if (geometry == null || geometry.Count == 0)
                        continue;
                    string title = geometry[0].Name ?? "Geometry";
                    int dot = title.IndexOf('.');
                    if (dot > 0)
                        title = title.Substring(0, dot);
                    var block = new DynamicLayout();
                    block.Add(VariableEditors.Header());
                    AddDirection(block, geoIndex, geometry, VarVM.Direction.X, "X Variables");
                    AddDirection(block, geoIndex, geometry, VarVM.Direction.Y, "Y Variables");
                    AddDirection(block, geoIndex, geometry, VarVM.Direction.Z, "Z Variables");
                    foreach (VarVM variable in geometry)
                        block.Add(VariableEditors.VariableRow(variable));
                    layout.Add(new Expander { Header = title, Expanded = true, Content = block });
                }
            }
            return layout;
        }

        private void AddDirection(DynamicLayout block, int geoIndex, List<VarVM> geometry, VarVM.Direction direction, string title)
        {
            if (!geometry.Any(v => v.Dir == (int)direction))
                return;
            GroupVarVM group = new GroupVarVM(_vm, (int)direction, geoIndex);
            _vm.GroupVars.Add(group);
            block.Add(VariableEditors.GroupRow(group, title));
        }

        private void BuildReadouts()
        {
            if (_vm.Variables != null)
            {
                foreach (VarVM variable in _vm.Variables)
                {
                    var label = new Label { Text = variable.Name };
                    _gradientLabels.Add(label);
                    _gradients.Add(label);
                }
            }
            if (_vm.Objectives != null)
            {
                foreach (ObjectiveVM objective in _vm.Objectives)
                {
                    var label = new Label { Text = objective.Name };
                    _objectiveLabels.Add(label);
                    _objectives.Add(label);
                }
            }
            RefreshReadouts(0);
        }

        private void ShowChart()
        {
            StepperGraphVM graph = _absolute ? _vm.ObjectiveChart_Abs : _vm.ObjectiveChart_Norm;
            _chartHost.Content = graph == null ? (Control)new Label { Text = "No objective chart" } : new ConvergenceChart(graph);
        }

        private async void Run(StepperOptimizer.Direction direction, bool gradientOnly)
        {
            if (_busy || _vm.Design == null)
                return;
            if (!_vm.Design.ActiveVariables.Any())
            {
                MessageBox.Show(this, "No variables selected!", "Stepper");
                return;
            }
            _busy = true;
            StepperOptimizer calculator = new StepperOptimizer(_vm.Design, _vm.FDStepSize, !_vm.DisablingNotAllowed);
            List<List<double>> gradientData = null;
            try
            {
                gradientData = await Task.Run(() =>
                {
                    List<List<double>> data = calculator.CalculateGradient();
                    if (!gradientOnly)
                    {
                        if (direction == StepperOptimizer.Direction.Isoperformance && !_vm.IsoperformancePossible())
                            return data;
                        _vm.Optimize(direction, data, calculator);
                    }
                    return data;
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Stepper");
                _busy = false;
                return;
            }

            if (!gradientOnly && direction == StepperOptimizer.Direction.Isoperformance && !_vm.IsoperformancePossible())
            {
                _vm.OpenIsoDialog = true;
                MessageBox.Show(this, "The isoperformance step is unavailable for this setup. You must have two or more variables and more variables than objectives.", "Notice");
                _vm.OpenIsoDialog = false;
                ApplyGradient(gradientData);
                _busy = false;
                return;
            }

            ApplyGradient(gradientData);
            if (gradientOnly)
            {
                _gradientExpander.Expanded = true;
                _objectiveExpander.Expanded = true;
            }
            else
            {
                _syncingHistory = true;
                _history.MaxValue = Math.Max(0, _vm.NumSteps);
                _history.Value = _history.MaxValue;
                _vm.TrackedStep = _history.Value;
                _syncingHistory = false;
                RefreshReadouts(_history.Value);
                ShowChart();
            }
            _busy = false;
        }

        private void ApplyGradient(List<List<double>> gradientData)
        {
            if (gradientData == null || _vm.ObjIndex < 0 || _vm.ObjIndex >= gradientData.Count)
                return;
            List<double> row = gradientData[_vm.ObjIndex];
            for (int i = 0; i < _gradientLabels.Count && i < row.Count; i++)
            {
                string name = _vm.Variables != null && i < _vm.Variables.Count ? _vm.Variables[i].Name : "Variable";
                _gradientLabels[i].Text = name + "  " + VariableEditors.FormatReadout(row[i]);
            }
        }

        private void RefreshReadouts(int step)
        {
            for (int i = 0; i < _objectiveLabels.Count && i < _vm.Objectives.Count; i++)
            {
                ChartSeries<double> series = _absolute ? _vm.ObjectiveEvolution_Abs[i] : _vm.ObjectiveEvolution_Norm[i];
                double value = series.Count == 0 ? 0 : series[Math.Min(step, series.Count - 1)];
                _objectiveLabels[i].Text = _vm.Objectives[i].Name + "  " + VariableEditors.FormatReadout(value);
            }
            if (_vm.GradientEvolution != null && _vm.ObjIndex >= 0 && _vm.ObjIndex < _vm.GradientEvolution.Count)
            {
                List<List<double?>> rows = _vm.GradientEvolution[_vm.ObjIndex];
                for (int i = 0; i < _gradientLabels.Count && i < rows.Count; i++)
                {
                    if (step < 0 || step >= rows[i].Count || !rows[i][step].HasValue)
                        continue;
                    string name = _vm.Variables != null && i < _vm.Variables.Count ? _vm.Variables[i].Name : "Variable";
                    _gradientLabels[i].Text = name + "  " + VariableEditors.FormatReadout(rows[i][step].Value);
                }
            }
        }

        private void OnReset(object sender, EventArgs e)
        {
            if (_history.Value == _history.MaxValue)
                return;
            int step = _history.Value;
            RefreshReadouts(step);
            DateTime start = DateTime.Now;
            _vm.Reset();
            _vm.Reset();
            _vm.UpdateEvolutionData(new List<List<double>>(), start);
            _syncingHistory = true;
            _history.MaxValue = Math.Max(0, _vm.NumSteps);
            _syncingHistory = false;
            ShowChart();
        }

        private void OnExport(object sender, EventArgs e)
        {
            Window owner = this.ParentWindow;
            var folder = new SelectFolderDialog { Title = "Choose a folder for the CSV export" };
            if (folder.ShowDialog(owner) != DialogResult.Ok || string.IsNullOrWhiteSpace(folder.Directory))
                return;
            string fileName = RadicalPanel.AskFileName(owner);
            if (string.IsNullOrWhiteSpace(fileName))
                return;
            string path = System.IO.Path.Combine(folder.Directory, fileName + "_" + DateTime.Now.ToString("yyyyMMddHHmmss"));
            try
            {
                _vm.ExportCSV_Log(path);
                _vm.ExportCSV_Raw(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Export CSV");
            }
        }

        private static Control FindFocus(Control control)
        {
            if (control == null)
                return null;
            if (control.HasFocus)
                return control;
            Container container = control as Container;
            if (container == null)
                return null;
            foreach (Control child in container.Controls)
            {
                Control found = FindFocus(child);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
