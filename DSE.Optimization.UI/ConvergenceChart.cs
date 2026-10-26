using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimization;
using Eto.Drawing;
using Eto.Forms;
using Radical;
using Stepper;

namespace DSE.Optimization.UI
{
    /// <summary>
    /// Line chart for one Radical series or several Stepper objective series.
    /// </summary>
    public class ConvergenceChart : Drawable
    {
        private static readonly Color[] Palette = new[]
        {
            Colors.DarkOrange, Colors.SteelBlue, Colors.ForestGreen, Colors.IndianRed,
            Colors.MediumPurple, Colors.Teal, Colors.Goldenrod, Colors.DimGray
        };

        private readonly GraphVM _single;
        private readonly StepperGraphVM _many;
        private readonly RadicalVM _radical;
        private float _left = 52;
        private float _top = 22;
        private float _plotW = 1;
        private float _plotH = 1;
        private int _count = 1;

        public ConvergenceChart(GraphVM graph, RadicalVM radical)
        {
            _single = graph;
            _radical = radical;
            graph.RedrawRequested += delegate { Invalidate(); };
            graph.PropertyChanged += delegate { Invalidate(); };
            Size = new Size(480, 220);
            BackgroundColor = Colors.White;
        }

        public ConvergenceChart(StepperGraphVM graph)
        {
            _many = graph;
            graph.RedrawRequested += delegate { Invalidate(); };
            graph.PropertyChanged += delegate { Invalidate(); };
            Size = new Size(480, 260);
            BackgroundColor = Colors.White;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_single == null || !_single.DisplayLine() || _count < 1)
                return;
            int index = IndexAt(e.Location.X);
            _single.MouseIteration = index;
            if (_radical != null)
                _radical.UpdateGraphLines(index);
            Invalidate();
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            if (_single != null)
                _single.GraphAxisLabelsVisibility = UiVisibility.Visible;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (_single != null)
                _single.GraphAxisLabelsVisibility = UiVisibility.Hidden;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float width = (float)Width;
            float height = (float)Height;
            if (width < 8 || height < 8)
                return;
            g.AntiAlias = true;
            g.FillRectangle(Colors.White, 0, 0, width, height);
            if (_single != null)
                PaintSingle(g, width, height);
            else if (_many != null)
                PaintMany(g, width, height);
        }

        private void PaintSingle(Graphics g, float width, float height)
        {
            double[] data = _single.ChartValues == null ? new double[0] : _single.ChartValues.Snapshot();
            string title = _single.LineGraphName ?? "Series";
            if (!string.IsNullOrEmpty(_single.FinalOptimizedValueString))
                title += "   " + _single.FinalOptimizedValueString;
            DrawSeries(g, width, height, title, new[] { data }, new[] { title }, _single.ChartLineVisibility == UiVisibility.Visible && _single.DisplayLine(), _single.MouseIteration, _single.GraphAxisLabelsVisibility != UiVisibility.Hidden);
        }

        private void PaintMany(Graphics g, float width, float height)
        {
            var series = new List<double[]>();
            if (_many.Data != null)
            {
                foreach (ChartSeries<double> child in _many.Data.Snapshot())
                    series.Add(child == null ? new double[0] : child.Snapshot());
            }
            var names = _many.SeriesNames ?? new List<string>();
            string[] labels = new string[series.Count];
            for (int i = 0; i < labels.Length; i++)
                labels[i] = i < names.Count ? names[i] : "Objective " + (i + 1);
            DrawSeries(g, width, height, "Objectives", series, labels, _many.GraphStep >= 0, _many.GraphStep, true);
        }

        private void DrawSeries(Graphics g, float width, float height, string title, IList<double[]> series, IList<string> names, bool showMarker, int markerIndex, bool showAxisLabels)
        {
            Font font = SystemFonts.Default();
            g.DrawText(font, Colors.Black, 8, 2, title ?? "");
            _left = 52;
            float right = 12;
            float bottom = 26;
            _top = 22;
            _plotW = Math.Max(1, width - _left - right);
            _plotH = Math.Max(1, height - _top - bottom);
            Pen axis = new Pen(Colors.Gray, 1);
            g.DrawLine(axis, _left, _top, _left, _top + _plotH);
            g.DrawLine(axis, _left, _top + _plotH, _left + _plotW, _top + _plotH);

            double min = double.MaxValue;
            double max = double.MinValue;
            _count = 1;
            for (int s = 0; s < series.Count; s++)
            {
                double[] data = series[s];
                if (data == null || data.Length == 0)
                    continue;
                _count = Math.Max(_count, data.Length);
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i] < min) min = data[i];
                    if (data[i] > max) max = data[i];
                }
            }
            if (min == double.MaxValue)
            {
                g.DrawText(font, Colors.Gray, _left, _top + 8, "No samples yet");
                return;
            }
            if (Math.Abs(max - min) < 1e-12)
            {
                max = min + 1;
                min = min - 1;
            }

            if (showAxisLabels)
            {
                g.DrawText(font, Colors.DimGray, 2, _top - 2, max.ToString("0.##"));
                g.DrawText(font, Colors.DimGray, 2, _top + _plotH - 14, min.ToString("0.##"));
                g.DrawText(font, Colors.DimGray, _left, _top + _plotH + 4, "0");
                g.DrawText(font, Colors.DimGray, _left + _plotW - 24, _top + _plotH + 4, (_count - 1).ToString());
            }

            int denom = Math.Max(1, _count - 1);
            for (int s = 0; s < series.Count; s++)
            {
                double[] data = series[s];
                if (data == null || data.Length == 0)
                    continue;
                Color color = Palette[s % Palette.Length];
                var pen = new Pen(color, 2);
                for (int i = 1; i < data.Length; i++)
                {
                    float x0 = XAt(i - 1, denom);
                    float x1 = XAt(i, denom);
                    float y0 = YAt(data[i - 1], min, max);
                    float y1 = YAt(data[i], min, max);
                    g.DrawLine(pen, x0, y0, x1, y1);
                }
                int step = data.Length > 80 ? Math.Max(1, data.Length / 40) : 1;
                for (int i = 0; i < data.Length; i += step)
                {
                    float x = XAt(i, denom);
                    float y = YAt(data[i], min, max);
                    g.FillEllipse(new SolidBrush(color), x - 2.5f, y - 2.5f, 5, 5);
                }
                if (names != null && s < names.Count && !string.IsNullOrEmpty(names[s]))
                    g.DrawText(font, color, _left + 8 + (s % 4) * 110, _top + 2 + (s / 4) * 14, names[s]);
            }

            if (showMarker && _count > 0)
            {
                int index = markerIndex;
                if (index < 0) index = 0;
                if (index > _count - 1) index = _count - 1;
                float x = XAt(index, denom);
                g.DrawLine(new Pen(Colors.SteelBlue, 1), x, _top, x, _top + _plotH);
            }
        }

        private float XAt(int index, int denom)
        {
            return _left + _plotW * index / denom;
        }

        private float YAt(double value, double min, double max)
        {
            float t = (float)((value - min) / (max - min));
            return _top + _plotH * (1f - t);
        }

        private int IndexAt(float x)
        {
            if (_count <= 1)
                return 0;
            float t = (x - _left) / _plotW;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return (int)Math.Round(t * (_count - 1));
        }
    }
}
