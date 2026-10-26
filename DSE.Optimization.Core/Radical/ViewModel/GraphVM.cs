using System;
using System.Linq;
using DSOptimization;

namespace Radical
{
    public class GraphVM : BaseVM
    {
        public ChartSeries<double> ChartValues { get; set; }
        public int DefaultMaxXAxis { get; set; }
        public event EventHandler RedrawRequested;

        public GraphVM(ChartSeries<double> scores, string name)
        {
            ChartValues = scores;
            _linegraph_name = name;
            _y = "0";
            _graphaxislabelsvisibility = UiVisibility.Hidden;
            _graphVisibility = UiVisibility.Visible;
            _chartlinevisiblity = UiVisibility.Collapsed;
            _xaxisstep = 1;
            DefaultMaxXAxis = 5;
            _maxxaxis = DefaultMaxXAxis;
            _finaloptimizedvalue = double.NaN;
            if (scores != null)
                scores.Changed += delegate { ForceGraphUpdate(); };
        }

        private string _linegraph_name;
        public string LineGraphName
        {
            get { return _linegraph_name; }
            set { CheckPropertyChanged<string>("LineGraphName", ref _linegraph_name, ref value); }
        }

        private int _mouseiteration;
        public int MouseIteration
        {
            get { return _mouseiteration; }
            set { CheckPropertyChanged<int>("MouseIteration", ref _mouseiteration, ref value); }
        }

        private string _y;
        public string DisplayY
        {
            get { return string.Format("{0}: {1}", this.LineGraphName, _y); }
            set { CheckPropertyChanged<string>("DisplayY", ref _y, ref value); }
        }

        private double _graphgridheight;
        public double GraphGridHeight
        {
            get { return _graphgridheight; }
            set { CheckPropertyChanged<double>("GraphGridHeight", ref _graphgridheight, ref value); }
        }

        public double AvailableHeight { get; set; }

        private double? _xaxisstep;
        public double? XAxisStep
        {
            get { return _xaxisstep; }
            set { CheckPropertyChanged<double?>("XAxisStep", ref _xaxisstep, ref value); }
        }

        private double? _maxxaxis;
        public double? MaxXAxis
        {
            get { return _maxxaxis; }
            set { CheckPropertyChanged<double?>("MaxXAxis", ref _maxxaxis, ref value); }
        }

        private bool _optimizerdone;
        public bool OptimizerDone
        {
            get { return _optimizerdone; }
            set { _optimizerdone = value; }
        }

        public bool DisplayLine()
        {
            return ChartValues != null && ChartValues.Any() && OptimizerDone;
        }

        private double _finaloptimizedvalue;
        public double FinalOptimizedValue
        {
            get { return _finaloptimizedvalue; }
            set
            {
                if (CheckPropertyChanged<double>("FinalOptimizedValue", ref _finaloptimizedvalue, ref value))
                    FinalOptimizedValueString = string.Format("{0:0.00}", FinalOptimizedValue);
            }
        }

        private string _finaloptimizedvaluestring;
        public string FinalOptimizedValueString
        {
            get { return _finaloptimizedvaluestring; }
            set { CheckPropertyChanged<string>("FinalOptimizedValueString", ref _finaloptimizedvaluestring, ref value); }
        }

        public void UpdateHeightFullScreen()
        {
            double height = AvailableHeight > 0 ? AvailableHeight : 360;
            GraphGridHeight = height - 40;
        }

        public void UpdateHeightHalfScreen()
        {
            double height = AvailableHeight > 0 ? AvailableHeight : 360;
            GraphGridHeight = (height - 40) * 0.5;
        }

        public void ForceGraphUpdate()
        {
            UiInvoke.Invoke(delegate
            {
                EventHandler handler = RedrawRequested;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            });
        }

        private UiVisibility _graphVisibility;
        public UiVisibility GraphVisibility
        {
            get { return _graphVisibility; }
            set { CheckPropertyChanged<UiVisibility>("GraphVisibility", ref _graphVisibility, ref value); }
        }

        private UiVisibility _chartlinevisiblity;
        public UiVisibility ChartLineVisibility
        {
            get { return _chartlinevisiblity; }
            set { CheckPropertyChanged<UiVisibility>("ChartLineVisibility", ref _chartlinevisiblity, ref value); }
        }

        private UiVisibility _graphaxislabelsvisibility;
        public UiVisibility GraphAxisLabelsVisibility
        {
            get { return _graphaxislabelsvisibility; }
            set { CheckPropertyChanged<UiVisibility>("GraphAxisLabelsVisibility", ref _graphaxislabelsvisibility, ref value); }
        }
    }
}
