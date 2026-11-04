using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TimeTrack
{
    public class Switch : FrameworkElement
    {
        private bool _isOn;
        private bool _hover;

        public event EventHandler Toggled;

        public Switch()
        {
            Width = 44;
            Height = 24;
            Cursor = Cursors.Hand;
            MouseEnter += delegate { _hover = true; InvalidateVisual(); };
            MouseLeave += delegate { _hover = false; InvalidateVisual(); };
            MouseLeftButtonUp += delegate
            {
                if (!IsEnabled) return;
                IsOn = !IsOn;
                if (Toggled != null) Toggled(this, EventArgs.Empty);
            };
        }

        public bool IsOn
        {
            get { return _isOn; }
            set
            {
                if (_isOn == value) return;
                _isOn = value;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            Brush track = _isOn ? Ui.Accent : (_hover ? Ui.BrushFrom("#3B404A") : Ui.BrushFrom("#343841"));
            Opacity = IsEnabled ? 1 : 0.45;
            dc.DrawRoundedRectangle(track, null, new Rect(0, 0, w, h), h / 2, h / 2);

            double diameter = h - 6;
            double x = _isOn ? w - diameter - 3 : 3;
            dc.DrawEllipse(Brushes.White, null, new Point(x + diameter / 2, h / 2), diameter / 2, diameter / 2);
        }
    }

    public class UsageBar : FrameworkElement
    {
        private double _ratio;

        public UsageBar()
        {
            Height = 4;
            Margin = new Thickness(0, 6, 0, 0);
        }

        public double Ratio
        {
            get { return _ratio; }
            set
            {
                _ratio = Math.Max(0, Math.Min(1, value));
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            dc.DrawRoundedRectangle(Ui.Track, null, new Rect(0, 0, w, h), h / 2, h / 2);
            if (_ratio <= 0) return;
            double fill = Math.Max(3, w * _ratio);
            dc.DrawRoundedRectangle(Ui.Accent, null, new Rect(0, 0, fill, h), h / 2, h / 2);
        }
    }

    public class SegmentBar : Border
    {
        private readonly string[] _labels;
        private readonly Border[] _cells;
        private readonly TextBlock[] _texts;
        private int _selected;

        public event EventHandler SelectionChanged;

        public SegmentBar(string[] labels, int selected)
        {
            _labels = labels;
            _selected = selected;
            _cells = new Border[labels.Length];
            _texts = new TextBlock[labels.Length];

            Background = Ui.Field;
            BorderBrush = Ui.StrokeSoft;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(9);
            Padding = new Thickness(3);

            StackPanel panel = new StackPanel();
            panel.Orientation = Orientation.Horizontal;
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                Border cell = new Border();
                cell.CornerRadius = new CornerRadius(6);
                cell.Padding = new Thickness(13, 4, 13, 4);
                cell.Cursor = Cursors.Hand;
                TextBlock text = Ui.TextBlock(labels[i], 11.5, Ui.Muted);
                text.HorizontalAlignment = HorizontalAlignment.Center;
                cell.Child = text;
                cell.MouseLeftButtonUp += delegate
                {
                    SetSelected(index);
                    if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
                };
                _cells[i] = cell;
                _texts[i] = text;
                panel.Children.Add(cell);
            }
            Child = panel;
            UpdateVisuals();
        }

        public int SelectedIndex
        {
            get { return _selected; }
        }

        public void SetSelected(int index)
        {
            if (index < 0) index = 0;
            if (index >= _cells.Length) index = _cells.Length - 1;
            _selected = index;
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                bool on = i == _selected;
                _cells[i].Background = on ? Ui.Card2 : Brushes.Transparent;
                _texts[i].Foreground = on ? Ui.Text : Ui.Muted;
                _texts[i].FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
    }

    public class UsageChart : FrameworkElement
    {
        private List<ChartItem> _items = new List<ChartItem>();
        private bool _hourly;
        private long _average;
        private int _hover = -1;
        private readonly Typeface _typeface;
        private readonly Typeface _typefaceBold;

        public UsageChart()
        {
            Height = 228;
            MinHeight = 190;
            SnapsToDevicePixels = true;
            _typeface = new Typeface(Ui.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            _typefaceBold = new Typeface(Ui.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            ToolTipService.SetInitialShowDelay(this, 150);
            ToolTipService.SetShowDuration(this, 60000);
            MouseMove += OnChartMouseMove;
            MouseLeave += delegate
            {
                _hover = -1;
                ToolTip = null;
                InvalidateVisual();
            };
        }

        public void SetData(List<ChartItem> items, bool hourly, long average)
        {
            _items = items ?? new List<ChartItem>();
            _hourly = hourly;
            _average = average;
            _hover = -1;
            ToolTip = null;
            InvalidateVisual();
        }

        private void OnChartMouseMove(object sender, MouseEventArgs e)
        {
            if (_items.Count == 0)
            {
                ToolTip = null;
                return;
            }
            Point p = e.GetPosition(this);
            double right = 64;
            double left = 2;
            double plotWidth = ActualWidth - right - left;
            if (plotWidth <= 0) return;
            double slot = plotWidth / _items.Count;
            int index = (int)((p.X - left) / slot);
            if (index < 0) index = 0;
            if (index >= _items.Count) index = _items.Count - 1;
            if (_hover == index) return;
            _hover = index;
            ToolTip = _items[index].Tip;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= 0 || height <= 0) return;

            double left = 2;
            double right = 64;
            double top = 10;
            double bottom = 28;
            double plotWidth = width - left - right;
            double plotHeight = height - top - bottom;
            if (plotWidth < 20 || plotHeight < 20) return;

            bool hasData = false;
            long peak = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Seconds > 0) hasData = true;
                if (_items[i].Seconds > peak) peak = _items[i].Seconds;
            }

            if (!hasData)
            {
                FormattedText title = Format("还没有使用记录", 15, Ui.Muted, false);
                FormattedText sub = Format("正常使用电脑后，这里会自动开始统计", 11.5, Ui.Dim, false);
                dc.DrawText(title, new Point(left + (plotWidth - title.Width) / 2, top + plotHeight / 2 - 22));
                dc.DrawText(sub, new Point(left + (plotWidth - sub.Width) / 2, top + plotHeight / 2 + 4));
                return;
            }

            long step = _hourly ? 1800 : 3600;
            long max = ((peak + step - 1) / step) * step;
            if (max < step) max = step;
            if (max < 3600) max = 3600;

            double baseY = top + plotHeight;

            Pen axisPen = new Pen(Ui.Stroke, 1);
            axisPen.Freeze();
            Pen gridPen = new Pen(Ui.BrushFrom("#23262E"), 1);
            gridPen.Freeze();
            Pen dashPen = new Pen(Ui.BrushFrom("#59606D"), 1);
            dashPen.DashStyle = DashStyles.Dash;
            dashPen.Freeze();

            dc.DrawLine(axisPen, new Point(left, baseY), new Point(left + plotWidth, baseY));
            dc.DrawLine(axisPen, new Point(left, top), new Point(left + plotWidth, top));

            int count = _items.Count;
            if (count > 0)
            {
                double slot = plotWidth / count;
                int gridEvery = count <= 10 ? 1 : (_hourly ? 6 : 5);
                for (int i = 1; i < count; i++)
                {
                    if (i % gridEvery != 0) continue;
                    double x = left + i * slot;
                    dc.DrawLine(gridPen, new Point(x, top), new Point(x, baseY));
                }

                for (int i = 0; i < count; i++)
                {
                    ChartItem item = _items[i];
                    if (item.Seconds <= 0) continue;
                    double barWidth = Math.Min(16, Math.Max(4, slot * 0.46));
                    double barHeight = (double)item.Seconds / max * plotHeight;
                    if (barHeight < 2) barHeight = 2;
                    double x = left + i * slot + (slot - barWidth) / 2;
                    double y = baseY - barHeight;
                    Brush fill = item.Highlight ? Ui.AccentLight : Ui.Accent;
                    dc.DrawRoundedRectangle(fill, null, new Rect(x, y, barWidth, barHeight), 3, 3);
                }

                if (_hover >= 0 && _hover < count)
                {
                    double slotHover = plotWidth / count;
                    Brush overlay = Ui.BrushFrom("#14FFFFFF");
                    dc.DrawRoundedRectangle(overlay, null,
                        new Rect(left + _hover * slotHover + 1, top, Math.Max(1, slotHover - 2), plotHeight), 6, 6);
                }
            }

            if (!_hourly && _average > 0 && _average < max)
            {
                double averageY = baseY - (double)_average / max * plotHeight;
                dc.DrawLine(dashPen, new Point(left, averageY), new Point(left + plotWidth, averageY));
                FormattedText avgLabel = Format("平均", 10.5, Ui.Muted, false);
                dc.DrawText(avgLabel, new Point(width - avgLabel.Width - 2, averageY - avgLabel.Height / 2));
            }

            FormattedText topLabel = Format(Ui.DurationAxis(max), 10.5, Ui.Muted, false);
            dc.DrawText(topLabel, new Point(width - topLabel.Width - 2, top - 2));
            FormattedText zeroLabel = Format("0", 10.5, Ui.Muted, false);
            dc.DrawText(zeroLabel, new Point(width - zeroLabel.Width - 2, baseY - zeroLabel.Height + 3));

            if (count > 0)
            {
                double slotWidth = plotWidth / count;
                for (int i = 0; i < count; i++)
                {
                    bool show = count <= 10 || i == 0 || i == count - 1 || (count == 24 ? i % 6 == 0 : i % 5 == 0);
                    if (!show) continue;
                    FormattedText label = Format(_items[i].Label, 10.5, _items[i].Highlight ? Ui.TextSoft : Ui.Muted, false);
                    double x = left + i * slotWidth + (slotWidth - label.Width) / 2;
                    if (x < left) x = left;
                    if (x + label.Width > left + plotWidth) x = left + plotWidth - label.Width;
                    dc.DrawText(label, new Point(x, baseY + 8));
                }
            }
        }

        private FormattedText Format(string text, double size, Brush brush, bool bold)
        {
            return new FormattedText(
                text ?? string.Empty,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                bold ? _typefaceBold : _typeface,
                size,
                brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }
    }
}