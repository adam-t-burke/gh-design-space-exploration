using System;
using DSOptimization;
using Eto.Drawing;
using Eto.Forms;
using Radical;
using Rhino.UI;
using Stepper;

namespace DSE.Optimization.UI
{
    public static class OptimizationWindows
    {
        public static void ShowRadical(IOptimizationHost host)
        {
            Prepare();
            Design design = new Design(host);
            RadicalVM viewModel = new RadicalVM(design, host);
            RadicalPanel panel = new RadicalPanel(viewModel);
            Form form = Create("Radical", panel, delegate
            {
                panel.CancelRunning();
                viewModel.OnWindowClosing();
            });
            Show(form);
            panel.ShowStartupNotices(form);
        }

        public static void ShowStepper(IOptimizationHost host)
        {
            Prepare();
            Design design = new Design(host);
            StepperVM viewModel = new StepperVM(design, host);
            StepperPanel panel = new StepperPanel(viewModel);
            Form form = Create("Stepper", panel, delegate { viewModel.OnWindowClosing(); });
            form.KeyDown += (sender, e) => panel.TryHandleKey(e, form);
            Show(form);
            panel.ShowStartupNotices(form);
        }

        public static void ShowDSOptimize(IOptimizationHost host)
        {
            Prepare();
            Design design = new Design(host);
            StepperVM stepper = new StepperVM(design, host);
            RadicalVM radical = new RadicalVM(design, host);
            StepperPanel stepperPanel = new StepperPanel(stepper);
            RadicalPanel radicalPanel = new RadicalPanel(radical);
            var tabs = new TabControl();
            tabs.Pages.Add(new TabPage { Text = "Stepper", Content = stepperPanel });
            tabs.Pages.Add(new TabPage { Text = "Radical", Content = radicalPanel });
            Form form = Create("DS Optimizer", tabs, delegate
            {
                radicalPanel.CancelRunning();
                radical.OnWindowClosing();
                stepper.OnWindowClosing();
            });
            form.KeyDown += (sender, e) => stepperPanel.TryHandleKey(e, form);
            Show(form);
            stepperPanel.ShowStartupNotices(form);
            radicalPanel.ShowStartupNotices(form);
        }

        private static void Prepare()
        {
            UiInvoke.Current = RhinoUiInvoker.Instance;
            NLoptNative.Register();
        }

        private static Form Create(string title, Control content, Action onClosed)
        {
            var form = new Form
            {
                Title = title,
                ClientSize = new Size(1180, 760),
                MinimumSize = new Size(860, 520),
                Content = content,
                Owner = RhinoEtoApp.MainWindow
            };
            form.Closed += delegate { onClosed(); };
            return form;
        }

        private static void Show(Form form)
        {
            try
            {
                form.Show();
            }
            catch
            {
                form.Dispose();
                throw;
            }
        }
    }
}
