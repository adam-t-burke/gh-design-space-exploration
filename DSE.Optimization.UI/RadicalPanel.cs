using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSOptimization;
using Eto.Drawing;
using Eto.Forms;
using NLoptNet;
using Radical;

namespace DSE.Optimization.UI
{
    public class RadicalPanel : Panel
    {
        private readonly RadicalVM _vm;
        private readonly DynamicLayout _charts = new DynamicLayout { Padding = new Padding(8), DefaultSpacing = new Size(6, 6) };
        private readonly Scrollable _chartScroll;
        private readonly Scrollable _variableScroll;
        private readonly Panel _settingsHost;
        private Button _play;
        private Button _pause;

        public RadicalPanel(RadicalVM vm)
        {
            _vm = vm;
            _chartScroll = new Scrollable { Content = _charts, ExpandContentWidth = true };
            _variableScroll = new Scrollable { Content = BuildVariables(), ExpandContentWidth = true, Width = 520 };
            _settingsHost = new Panel { Content = new RadicalSettingsPanel(vm), Width = 280, Visible = false };
            ReloadCharts();

            _play = new Button { Text = "Start" };
            _pause = new Button { Text = "Stop", Visible = false };
            var reset = new Button { Text = "Reset" };
            var best = new Button { Text = "Best" };
            var export = new Button { Text = "Export CSV" };
            var variables = new Button { Text = "Variables" };
            var settings = new Button { Text = "Settings" };
            _play.Click += OnPlay;
            _pause.Click += OnPause;
            reset.Click += delegate { ResetToOriginal(); };
            best.Click += delegate { ApplyBest(); };
            export.Click += OnExport;
            variables.Click += delegate { _variableScroll.Visible = !_variableScroll.Visible; };
            settings.Click += delegate { _settingsHost.Visible = !_settingsHost.Visible; };

            var toolbar = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Padding = new Padding(8, 8, 8, 0),
                Items = { _play, _pause, reset, best, export, variables, settings }
            };
            var layout = new DynamicLayout { DefaultSpacing = new Size(4, 4), Padding = new Padding(4) };
            layout.Add(toolbar);
            layout.BeginHorizontal();
            layout.Add(_variableScroll);
            layout.Add(_chartScroll, xscale: true, yscale: true);
            layout.Add(_settingsHost);
            layout.EndHorizontal();
            Content = layout;
        }

        public void ShowStartupNotices(Control parent)
        {
            if (_vm.OpenDialog)
            {
                MessageBox.Show(parent, "You've entered multiple objectives.\n\nRadical does not support multi-objective optimization and will consider only your first objective.", "Notice");
                _vm.OpenDialog = false;
            }
        }

        public bool IsRunning
        {
            get { return _pause.Visible; }
        }

        public void CancelRunning()
        {
            if (!IsRunning)
                return;
            _vm.OptimizationFinished();
            if (_vm.Cancellation != null)
                _vm.Cancellation.Cancel();
            _pause.Visible = false;
            _play.Visible = true;
        }

        private Control BuildVariables()
        {
            var layout = new DynamicLayout { Padding = new Padding(8), DefaultSpacing = new Size(4, 4) };
            bool any = false;
            if (_vm.Constraints != null && _vm.Constraints.Count > 0)
            {
                any = true;
                var constraints = new DynamicLayout();
                constraints.Add(new Label { Text = "Name, current value, include, comparison, limit" });
                foreach (ConstVM constraint in _vm.Constraints)
                    constraints.Add(VariableEditors.ConstraintRow(constraint, ReloadCharts));
                layout.Add(new Expander { Header = "Constraints", Expanded = true, Content = constraints });
            }
            if (_vm.NumVars != null && _vm.NumVars.Count > 0)
            {
                any = true;
                var sliders = new DynamicLayout();
                GroupVarVM all = new GroupVarVM(_vm, (int)VarVM.Direction.None);
                _vm.GroupVars.Add(all);
                sliders.Add(new Label { Text = "Group" });
                sliders.Add(VariableEditors.Header());
                sliders.Add(VariableEditors.GroupRow(all, "All Variables"));
                sliders.Add(new Label { Text = "Single variables" });
                sliders.Add(VariableEditors.Header());
                foreach (VarVM variable in _vm.NumVars)
                    sliders.Add(VariableEditors.VariableRow(variable));
                layout.Add(new Expander { Header = "Sliders", Expanded = true, Content = sliders });
            }
            if (_vm.GeoVars != null && _vm.GeoVars.Count > 0)
            {
                any = true;
                var geometries = new DynamicLayout();
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
                    AddDirection(block, _vm, geoIndex, geometry, VarVM.Direction.X, "X Variables");
                    AddDirection(block, _vm, geoIndex, geometry, VarVM.Direction.Y, "Y Variables");
                    AddDirection(block, _vm, geoIndex, geometry, VarVM.Direction.Z, "Z Variables");
                    block.Add(new Label { Text = "Control points" });
                    foreach (VarVM variable in geometry)
                        block.Add(VariableEditors.VariableRow(variable));
                    geometries.Add(new Expander { Header = title, Expanded = true, Content = block });
                }
                layout.Add(new Expander { Header = "Geometries", Expanded = true, Content = geometries });
            }
            if (!any)
                layout.Add(new Label { Text = "No variables are connected." });
            return layout;
        }

        private static void AddDirection(DynamicLayout block, RadicalVM vm, int geoIndex, List<VarVM> geometry, VarVM.Direction direction, string title)
        {
            if (!geometry.Any(v => v.Dir == (int)direction))
                return;
            GroupVarVM group = new GroupVarVM(vm, (int)direction, geoIndex);
            vm.GroupVars.Add(group);
            block.Add(VariableEditors.GroupRow(group, title));
        }

        private void ReloadCharts()
        {
            _charts.Clear();
            if (_vm.Graphs != null && _vm.Graphs.ContainsKey("Main") && _vm.Graphs["Main"].Count > 0)
            {
                GraphVM main = _vm.Graphs["Main"][0];
                main.AvailableHeight = 420;
                _charts.Add(new ConvergenceChart(main, _vm));
            }
            if (_vm.Constraints != null)
            {
                foreach (ConstVM constraint in _vm.Constraints)
                {
                    if (constraint.GraphVM != null && constraint.IsActive && constraint.GraphVM.GraphVisibility == UiVisibility.Visible)
                        _charts.Add(new ConvergenceChart(constraint.GraphVM, _vm));
                }
            }
            _charts.Create();
        }

        private async void OnPlay(object sender, EventArgs e)
        {
            if (_vm.Design == null || !_vm.Design.ActiveVariables.Any())
            {
                MessageBox.Show(this, "No variables selected!", "Radical");
                return;
            }
            _play.Visible = false;
            _pause.Visible = true;
            _vm.Cancellation = new CancellationTokenSource();
            try
            {
                await Task.Run(() => _vm.Optimize(), _vm.Cancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (AggregateException ex)
            {
                if (!ex.InnerExceptions.All(inner => inner is OperationCanceledException))
                    MessageBox.Show(this, ex.GetBaseException().Message, "Radical");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Radical");
            }
            _pause.Visible = false;
            _play.Visible = true;
            ReloadCharts();
        }

        private void OnPause(object sender, EventArgs e)
        {
            _vm.OptimizationFinished();
            if (_vm.Cancellation != null)
                _vm.Cancellation.Cancel();
            _pause.Visible = false;
            _play.Visible = true;
        }

        private void ResetToOriginal()
        {
            _vm.ResetObjective();
            foreach (VarVM variable in _vm.NumVars)
                variable.ResetValue();
            foreach (List<VarVM> geometry in _vm.GeoVars)
                foreach (VarVM variable in geometry)
                    variable.ResetValue();
            UpdateGeometries();
            _vm.UpdateCurrentScoreDisplay();
            _vm.ClearGraphs();
            ReloadCharts();
        }

        private void ApplyBest()
        {
            foreach (VarVM variable in _vm.NumVars)
                variable.SetBestSolution();
            foreach (List<VarVM> geometry in _vm.GeoVars)
                foreach (VarVM variable in geometry)
                    variable.SetBestSolution();
            UpdateGeometries();
            _vm.UpdateCurrentScoreDisplay();
            _vm.ClearGraphs();
            ReloadCharts();
        }

        private void UpdateGeometries()
        {
            foreach (IDesignGeometry geometry in _vm.Design.Geometries)
                geometry.Update();
            UiInvoke.Invoke(delegate
            {
                Grasshopper.Kernel.GH_Document document = GhDocuments.Active();
                if (document != null)
                    document.NewSolution(false, Grasshopper.Kernel.GH_SolutionMode.Default);
            });
        }

        private void OnExport(object sender, EventArgs e)
        {
            Window owner = this.ParentWindow;
            var folder = new SelectFolderDialog { Title = "Choose a folder for the CSV export" };
            if (folder.ShowDialog(owner) != DialogResult.Ok || string.IsNullOrWhiteSpace(folder.Directory))
                return;
            string fileName = AskFileName(owner);
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

        internal static string AskFileName(Window owner)
        {
            var dialog = new Dialog { Title = "CSV file name", ClientSize = new Size(420, 120), Padding = 12 };
            var name = new TextBox { Text = "Untitled" };
            string chosen = null;
            var ok = new Button { Text = "Export" };
            var cancel = new Button { Text = "Cancel" };
            ok.Click += delegate { chosen = name.Text; dialog.Close(); };
            cancel.Click += delegate { dialog.Close(); };
            dialog.Content = new StackLayout
            {
                Spacing = 8,
                Items =
                {
                    new Label { Text = "File name" },
                    name,
                    new StackLayout { Orientation = Orientation.Horizontal, Spacing = 6, Items = { ok, cancel } }
                }
            };
            dialog.ShowModal(owner);
            return chosen;
        }
    }

    public class RadicalSettingsPanel : Panel
    {
        private readonly RadicalVM _vm;
        private readonly DropDown _primary = new DropDown();
        private readonly DropDown _secondary = new DropDown();
        private readonly Label _secondaryLabel = new Label { Text = "Secondary algorithm" };
        private List<NLoptAlgorithm> _primaryAlgs = new List<NLoptAlgorithm>();

        public RadicalSettingsPanel(RadicalVM vm)
        {
            _vm = vm;
            _vm.Niterations = _vm.Niterations == 0 ? 1000 : _vm.Niterations;
            if (_vm.ConvCrit == 0)
                _vm.ConvCrit = 0.001;
            if (_vm.Mode == 0)
                _vm.Mode = RefreshMode.Live;
            if (_vm.PrimaryAlgorithm == 0)
                _vm.PrimaryAlgorithm = NLoptAlgorithm.LN_COBYLA;
            if (_vm.SecondaryAlgorithm == 0)
                _vm.SecondaryAlgorithm = NLoptAlgorithm.LN_BOBYQA;

            var mode = new DropDown();
            var modes = new[] { RefreshMode.Live, RefreshMode.Data, RefreshMode.Silent };
            mode.DataStore = new[] { "Live Geometry and Data", "Live Data", "Silent" };
            mode.SelectedIndex = Math.Max(0, Array.IndexOf(modes, _vm.Mode));
            mode.SelectedIndexChanged += delegate
            {
                if (mode.SelectedIndex >= 0 && mode.SelectedIndex < modes.Length)
                    _vm.Mode = modes[mode.SelectedIndex];
            };

            var iterations = new NumericStepper { Value = _vm.Niterations, MinValue = 1, MaxValue = 1000000, DecimalPlaces = 0, Increment = 1 };
            iterations.ValueChanged += delegate { _vm.Niterations = (int)iterations.Value; };
            var convergence = new NumericStepper { Value = _vm.ConvCrit, MinValue = 1e-12, MaxValue = 1, DecimalPlaces = 6, Increment = 0.0001 };
            convergence.ValueChanged += delegate { _vm.ConvCrit = convergence.Value; };

            var advanced = new CheckBox { Text = "Show advanced algorithms", Checked = _vm.AdvancedOptions };
            var disabling = new CheckBox { Text = "Block component disabling", Checked = _vm.DisablingNotAllowed };
            advanced.CheckedChanged += delegate
            {
                _vm.AdvancedOptions = advanced.Checked == true;
                FillAlgorithms();
            };
            disabling.CheckedChanged += delegate { _vm.DisablingNotAllowed = disabling.Checked == true; };
            _primary.SelectedIndexChanged += delegate
            {
                if (_primary.SelectedIndex >= 0 && _primary.SelectedIndex < _primaryAlgs.Count)
                    _vm.PrimaryAlgorithm = _primaryAlgs[_primary.SelectedIndex];
                UpdateSecondaryVisibility();
            };
            _secondary.SelectedIndexChanged += delegate
            {
                var algs = _vm.AvailableSecondaryAlgs;
                if (_secondary.SelectedIndex >= 0 && _secondary.SelectedIndex < algs.Count)
                    _vm.SecondaryAlgorithm = algs[_secondary.SelectedIndex];
            };

            var help = new Button { Text = "Algorithm help" };
            help.Click += delegate
            {
                Process.Start(new ProcessStartInfo("https://nlopt.readthedocs.io/en/latest/NLopt_Algorithms/") { UseShellExecute = true });
            };

            FillAlgorithms();
            FillSecondary();

            var layout = new DynamicLayout { Padding = new Padding(8), DefaultSpacing = new Size(4, 6) };
            layout.Add(new Label { Text = "Refresh mode" });
            layout.Add(mode);
            layout.Add(new Label { Text = "Iterations" });
            layout.Add(iterations);
            layout.Add(new Label { Text = "Relative convergence" });
            layout.Add(convergence);
            layout.Add(new Label { Text = "Algorithm" });
            layout.Add(_primary);
            layout.Add(_secondaryLabel);
            layout.Add(_secondary);
            layout.Add(advanced);
            layout.Add(disabling);
            layout.Add(help);
            layout.Add(new Label
            {
                Text = "Radical may temporarily disable components that are not needed to recompute the objective. Turn blocking on if that interferes with your definition.",
                Wrap = WrapMode.Word
            });
            Content = new Scrollable { Content = layout, ExpandContentWidth = true };
            UpdateSecondaryVisibility();
        }

        private void FillAlgorithms()
        {
            _primaryAlgs = _vm.AvailableAlgs ?? new List<NLoptAlgorithm>();
            _primary.DataStore = _primaryAlgs.Select(a => a.ToString()).ToList();
            int index = _primaryAlgs.IndexOf(_vm.PrimaryAlgorithm);
            if (index < 0 && _primaryAlgs.Count > 0)
            {
                index = 0;
                _vm.PrimaryAlgorithm = _primaryAlgs[0];
            }
            _primary.SelectedIndex = index;
        }

        private void FillSecondary()
        {
            var algs = _vm.AvailableSecondaryAlgs;
            _secondary.DataStore = algs.Select(a => a.ToString()).ToList();
            int index = algs.IndexOf(_vm.SecondaryAlgorithm);
            _secondary.SelectedIndex = index < 0 ? 0 : index;
        }

        private void UpdateSecondaryVisibility()
        {
            bool show = _vm.DFreeAlgs_ReqSec.Contains(_vm.PrimaryAlgorithm);
            _secondary.Visible = show;
            _secondaryLabel.Visible = show;
        }
    }
}
