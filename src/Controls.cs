using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TimeTrack
{
    public class Switch : FrameworkElement
    {
        public static readonly DependencyProperty KnobProperty = DependencyProperty.Register(
            "Knob", typeof(double), typeof(Switch),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        private bool _isOn;
        private bool _hover;
        private readonly EventHandler _themeHandler;

        public event EventHandler Toggled;

        public double Knob
        {
            get { return (double)GetValue(KnobProperty); }
            set { SetValue(KnobProperty, value); }
        }

        public Switch()
        {
            Width = 44;
            Height = 24;
            Cursor = Cursors.Hand;
            _themeHandler = delegate { InvalidateVisual(); };
            Ui.ThemeChanged += _themeHandler;
            Unloaded += delegate { Ui.ThemeChanged -= _themeHandler; };
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
                double target = value ? 1.0 : 0.0;
                if (IsLoaded)
                {
                    DoubleAnimation animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(160));
                    animation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                    BeginAnimation(KnobProperty, animation);
                }
                else
                {
                    BeginAnimation(KnobProperty, null);
                    Knob = target;
                }
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            Brush track = _isOn ? Ui.Accent : (_hover ? Ui.TrackHover : Ui.Track);
            Opacity = IsEnabled ? 1 : 0.45;
            dc.DrawRoundedRectangle(track, null, new Rect(0, 0, w, h), h / 2, h / 2);

            double diameter = h - 6;
            double t = Math.Max(0.0, Math.Min(1.0, Knob));
            double x = 3 + t * (w - diameter - 6);
            dc.DrawEllipse(Brushes.White, null, new Point(x + diameter / 2, h / 2), diameter / 2, diameter / 2);
        }
    }

    public class UsageBar : FrameworkElement
    {
        private double _ratio;
        private readonly EventHandler _themeHandler;

        public UsageBar()
        {
            Height = 4;
            Margin = new Thickness(0, 6, 0, 0);
            _themeHandler = delegate { InvalidateVisual(); };
            Ui.ThemeChanged += _themeHandler;
            Unloaded += delegate { Ui.ThemeChanged -= _themeHandler; };
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
        private Point _mouse;
        private bool _hasMouse;
        private readonly Typeface _typeface;
        private readonly Typeface _typefaceBold;
        private readonly EventHandler _themeHandler;
        private static readonly Brush TipBg = Ui.TipBg;
        private static readonly Brush TipShadow = Ui.BrushFrom("#4D000000");
        private static readonly Pen TipBorder = new Pen(Ui.StrokeSoft, 1);

        public UsageChart()
        {
            Height = 228;
            MinHeight = 190;
            SnapsToDevicePixels = true;
            _typeface = new Typeface(Ui.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            _typefaceBold = new Typeface(Ui.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            _themeHandler = delegate { InvalidateVisual(); };
            Ui.ThemeChanged += _themeHandler;
            Unloaded += delegate { Ui.ThemeChanged -= _themeHandler; };
            MouseMove += OnChartMouseMove;
            MouseLeave += delegate
            {
                _hover = -1;
                _hasMouse = false;
                InvalidateVisual();
            };
        }

        public void SetData(List<ChartItem> items, bool hourly, long average)
        {
            _items = items ?? new List<ChartItem>();
            _hourly = hourly;
            _average = average;
            _hover = -1;
            _hasMouse = false;
            InvalidateVisual();
        }

        private void OnChartMouseMove(object sender, MouseEventArgs e)
        {
            _mouse = e.GetPosition(this);
            _hasMouse = true;
            if (_items.Count == 0)
            {
                SetHover(-1);
                return;
            }

            double right = 64;
            double left = 2;
            double top = 10;
            double bottom = 28;
            double plotWidth = ActualWidth - right - left;
            double plotHeight = ActualHeight - top - bottom;
            if (plotWidth <= 0 || plotHeight <= 0) return;
            if (_mouse.X < left || _mouse.X > left + plotWidth || _mouse.Y < top || _mouse.Y > top + plotHeight)
            {
                SetHover(-1);
                return;
            }

            double slot = plotWidth / _items.Count;
            int index = (int)((_mouse.X - left) / slot);
            if (index < 0) index = 0;
            if (index >= _items.Count) index = _items.Count - 1;
            if (_items[index].Seconds <= 0) index = -1;
            SetHover(index);
        }

        private void SetHover(int index)
        {
            if (_hover == index) return;
            _hover = index;
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
            Pen gridPen = new Pen(Ui.StrokeSoft, 1);
            Pen dashPen = new Pen(Ui.Muted, 1);
            dashPen.DashStyle = DashStyles.Dash;

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
                    Brush overlay = Ui.HoverOverlay;
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

            if (_hasMouse && _hover >= 0 && _hover < _items.Count && _items[_hover].Seconds > 0)
                DrawHoverCard(dc, width, height, left, top, plotWidth, plotHeight, baseY, max);
        }

        private void DrawHoverCard(DrawingContext dc, double width, double height,
            double left, double top, double plotWidth, double plotHeight, double baseY, long max)
        {
            ChartItem item = _items[_hover];
            double slot = plotWidth / _items.Count;
            double barWidth = Math.Min(16, Math.Max(4, slot * 0.46));
            double barHeight = (double)item.Seconds / max * plotHeight;
            if (barHeight < 2) barHeight = 2;
            double barX = left + _hover * slot + (slot - barWidth) / 2;
            double barY = baseY - barHeight;

            string title = item.Tip ?? item.Label;
            string value = string.Empty;
            int split = title.IndexOf("  ·  ", StringComparison.Ordinal);
            if (split >= 0)
            {
                value = title.Substring(split + 5);
                title = title.Substring(0, split);
            }
            if (string.IsNullOrEmpty(value)) value = Ui.Duration(item.Seconds);

            FormattedText titleText = Format(title, 11.5, Ui.Muted, false);
            FormattedText valueText = Format(value, 13.5, Ui.Text, true);
            double cardWidth = Math.Max(152, Math.Max(titleText.Width, valueText.Width) + 28);
            double cardHeight = 58;
            double x;
            double y;
            if (barY - cardHeight - 10 >= top + 2)
            {
                x = barX + barWidth / 2 - cardWidth / 2;
                y = barY - cardHeight - 10;
            }
            else
            {
                x = barX + barWidth + 10;
                if (x + cardWidth > width - 4) x = barX - cardWidth - 10;
                y = barY + barHeight / 2 - cardHeight / 2;
            }
            if (x < 4) x = 4;
            if (x + cardWidth > width - 4) x = width - cardWidth - 4;
            if (y < top + 4) y = top + 4;
            if (y + cardHeight > height - 4) y = height - cardHeight - 4;

            dc.DrawRoundedRectangle(TipShadow, null, new Rect(x + 2, y + 2, cardWidth, cardHeight), 9, 9);
            dc.DrawRoundedRectangle(TipBg, TipBorder, new Rect(x, y, cardWidth, cardHeight), 9, 9);
            dc.DrawRoundedRectangle(Ui.Accent, null, new Rect(x + 11, y + 11, 3, cardHeight - 22), 1.5, 1.5);
            dc.DrawText(titleText, new Point(x + 22, y + 9));
            dc.DrawText(valueText, new Point(x + 22, y + 28));
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

    public class PageScrollBar : FrameworkElement
    {
        private readonly ScrollViewer _viewer;
        private readonly EventHandler _themeHandler;
        private bool _dragging;
        private bool _thumbHover;
        private double _dragStartY;
        private double _dragStartOffset;

        public PageScrollBar(ScrollViewer viewer)
        {
            _viewer = viewer;
            Width = 12;
            Focusable = false;
            Cursor = Cursors.Arrow;
            _themeHandler = delegate { InvalidateVisual(); };
            Ui.ThemeChanged += _themeHandler;
            Unloaded += delegate { Ui.ThemeChanged -= _themeHandler; };
            SizeChanged += delegate { Refresh(); };
        }

        public void Refresh()
        {
            bool canScroll = _viewer != null && _viewer.ScrollableHeight > 0.5;
            Visibility = canScroll ? Visibility.Visible : Visibility.Collapsed;
            IsHitTestVisible = canScroll;
            if (!canScroll && _dragging)
            {
                _dragging = false;
                ReleaseMouseCapture();
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            Rect thumb = GetThumbRect();
            if (thumb.IsEmpty) return;

            double trackHeight = Math.Max(0, ActualHeight - 8);
            dc.PushOpacity(0.42);
            dc.DrawRoundedRectangle(Ui.StrokeSoft, null, new Rect(4, 4, 4, trackHeight), 2, 2);
            dc.Pop();
            Brush thumbBrush = _dragging ? Ui.Accent : (_thumbHover ? Ui.TextSoft : Ui.Muted);
            dc.DrawRoundedRectangle(thumbBrush, null, thumb, 3, 3);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_viewer == null) return;
            Point point = e.GetPosition(this);
            if (_dragging)
            {
                Rect thumb = GetThumbRect();
                double trackHeight = Math.Max(0, ActualHeight - 8);
                double maxTop = Math.Max(0, trackHeight - thumb.Height);
                if (maxTop > 0 && !thumb.IsEmpty)
                {
                    double offset = _dragStartOffset +
                        ((point.Y - _dragStartY) / maxTop) * _viewer.ScrollableHeight;
                    _viewer.ScrollToVerticalOffset(Math.Max(0, Math.Min(_viewer.ScrollableHeight, offset)));
                }
                e.Handled = true;
                return;
            }

            bool over = GetThumbRect().Contains(point);
            if (over != _thumbHover)
            {
                _thumbHover = over;
                Cursor = over ? Cursors.SizeNS : Cursors.Arrow;
                InvalidateVisual();
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (_viewer == null || _viewer.ScrollableHeight <= 0.5) return;
            Point point = e.GetPosition(this);
            Rect thumb = GetThumbRect();
            if (thumb.Contains(point))
            {
                _dragging = true;
                _dragStartY = point.Y;
                _dragStartOffset = _viewer.VerticalOffset;
                CaptureMouse();
                Cursor = Cursors.SizeNS;
                InvalidateVisual();
                e.Handled = true;
            }
            else if (point.Y >= 4 && point.Y <= ActualHeight - 4)
            {
                double page = Math.Max(48, _viewer.ViewportHeight * 0.9);
                double offset = _viewer.VerticalOffset + (point.Y < thumb.Top ? -page : page);
                _viewer.ScrollToVerticalOffset(Math.Max(0, Math.Min(_viewer.ScrollableHeight, offset)));
                e.Handled = true;
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!_dragging) return;
            _dragging = false;
            ReleaseMouseCapture();
            Cursor = _thumbHover ? Cursors.SizeNS : Cursors.Arrow;
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (_dragging) return;
            if (_thumbHover)
            {
                _thumbHover = false;
                Cursor = Cursors.Arrow;
                InvalidateVisual();
            }
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_viewer == null || _viewer.ScrollableHeight <= 0.5) return;
            double offset = _viewer.VerticalOffset - (e.Delta > 0 ? 90 : -90);
            _viewer.ScrollToVerticalOffset(Math.Max(0, Math.Min(_viewer.ScrollableHeight, offset)));
            e.Handled = true;
        }

        private Rect GetThumbRect()
        {
            if (_viewer == null || ActualHeight <= 0 || _viewer.ViewportHeight <= 0) return Rect.Empty;
            double extent = _viewer.ExtentHeight;
            double viewport = _viewer.ViewportHeight;
            if (extent <= viewport + 0.5) return Rect.Empty;
            double trackHeight = Math.Max(0, ActualHeight - 8);
            double thumbHeight = Math.Max(36, trackHeight * viewport / extent);
            if (thumbHeight > trackHeight) thumbHeight = trackHeight;
            double maxTop = Math.Max(0, trackHeight - thumbHeight);
            double ratio = _viewer.ScrollableHeight <= 0 ? 0 : _viewer.VerticalOffset / _viewer.ScrollableHeight;
            return new Rect(3, 4 + maxTop * ratio, 6, thumbHeight);
        }
    }
}