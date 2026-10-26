using System;
using System.Collections.Generic;
using DSOptimization;

namespace Stepper
{
    public class StepperGraphVM : BaseVM
    {
        public ChartSeries<ChartSeries<double>> Data { get; private set; }
        public List<string> SeriesNames { get; private set; }
        public event EventHandler RedrawRequested;

        public StepperGraphVM(ChartSeries<ChartSeries<double>> data)
        {
            this.Data = data;
            this.SeriesNames = new List<string>();
            this.axissteps = 1;
            if (data != null)
                data.Changed += delegate { RequestRedraw(); };
        }

        private int step;
        public int GraphStep
        {
            get { return this.step; }
            set { CheckPropertyChanged<int>("GraphStep", ref step, ref value); }
        }

        private int axissteps;
        public int XAxisSteps
        {
            get { return axissteps; }
            set { CheckPropertyChanged<int>("XAxisSteps", ref axissteps, ref value); }
        }

        public void SetSeriesNames(IList<string> names)
        {
            this.SeriesNames = names == null ? new List<string>() : new List<string>(names);
            FirePropertyChanged("SeriesNames");
            RequestRedraw();
        }

        public void RequestRedraw()
        {
            UiInvoke.Invoke(delegate
            {
                EventHandler handler = RedrawRequested;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            });
        }
    }
}
