using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace TimeTrack
{
    public class MainWindow : Window
    {
        private DataStore _store;
        private Tracker _tracker;
        private WinForms.NotifyIcon _tray;
        private WinForms.ContextMenuStrip _trayMenu;
        private WinForms.ToolStripMenuItem _pauseItem;
        private WinForms.ToolStripMenuItem _autoStartItem;
        private Drawing.Icon _trayIcon;
        private DispatcherTimer _tickTimer;
        private DispatcherTimer _refreshTimer;
        private ScrollViewer _scroller;
        private Border _content;
        private Border _statusPill;
        private Border _statusDot;
        private TextBlock _statusLabel;

        private string _view = "dashboard";
        private int _rangeMode;
        private string _renderedView;
        private int _renderedRangeMode;
        private DateTime _renderedAnchor;
        private DateTime _anchor = DateTime.Today;
        private DateTime _lastToday = DateTime.Today;
        private bool _showAllApps;
        private string _detailKey;
        private string _detailName;
        private string _detailPath;
        private long _lastVersion;
        private bool _exiting;
        private bool _disposed;
        private bool _trayHintShown;
        private readonly bool _startHidden;
        private bool? _statusPaused;
        private int _trimTicks;

        private class AppFilterEntry
        {
            public FrameworkElement Container;
            public string Search;
        }

        public MainWindow(bool startHidden)
        {
            _startHidden = startHidden;
            Title = "时间统计";
            Width = Math.Min(1180, Math.Max(940, SystemParameters.WorkArea.Width - 160));
            Height = Math.Min(840, Math.Max(640, SystemParameters.WorkArea.Height - 120));
            MinWidth = 940;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Ui.Bg;
            FontFamily = Ui.Font;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            WindowChrome chrome = new WindowChrome();
            chrome.CaptionHeight = 56;
            chrome.ResizeBorderThickness = new Thickness(6);
            chrome.CornerRadius = new CornerRadius(0);
            chrome.GlassFrameThickness = new Thickness(0);
            chrome.UseAeroCaptionButtons = false;
            WindowChrome.SetWindowChrome(this, chrome);
            WindowFx.Apply(this);

            _store = new DataStore();
            _tracker = new Tracker(_store);
            _lastVersion = _store.Version;

            Content = BuildShell();
            SetupTray();
            UpdateStatus();

            Loaded += delegate
            {
                UpdateContentWidth();
                if (_view == "dashboard") Rebuild(false);
            };
            SizeChanged += delegate { UpdateContentWidth(); };
            Activated += delegate
            {
                if (_disposed) return;
                UpdateStatus();
                if (_view == "dashboard" && _store.Version != _lastVersion) Rebuild(true);
            };
            Closing += OnClosing;
            Closed += OnClosed;
            PreviewKeyDown += OnPreviewKeyDown;
            if (Application.Current != null)
            {
                Application.Current.SessionEnding += delegate(object sender, SessionEndingCancelEventArgs e)
                {
                    _exiting = true;
                };
            }

            _tickTimer = new DispatcherTimer();
            _tickTimer.Interval = TimeSpan.FromSeconds(1);
            _tickTimer.Tick += OnTick;
            _tickTimer.Start();

            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(15);
            _refreshTimer.Tick += OnRefreshTimer;
            _refreshTimer.Start();

            ApplyLightweightMode();

            if (!startHidden)
            {
                // The window is shown by the application entry point.
            }
        }

        private UIElement BuildShell()
        {
            Grid root = new Grid();
            root.Background = Ui.Bg;
            root.ClipToBounds = true;
            TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions[0].Height = new GridLength(56);
            root.RowDefinitions.Add(new RowDefinition());

            System.Windows.Shapes.Ellipse glow = new System.Windows.Shapes.Ellipse();
            glow.Width = 1100;
            glow.Height = 560;
            glow.HorizontalAlignment = HorizontalAlignment.Center;
            glow.VerticalAlignment = VerticalAlignment.Top;
            glow.Margin = new Thickness(0, -320, 0, 0);
            RadialGradientBrush glowBrush = new RadialGradientBrush();
            glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(70, 59, 130, 246), 0));
            glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(18, 59, 130, 246), 0.55));
            glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 59, 130, 246), 1));
            glowBrush.Freeze();
            glow.Fill = glowBrush;
            glow.IsHitTestVisible = false;
            Grid.SetRow(glow, 1);
            root.Children.Add(glow);

            _scroller = new ScrollViewer();
            _scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            _scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _scroller.CanContentScroll = false;
            _scroller.Focusable = false;
            _scroller.PanningMode = PanningMode.VerticalOnly;
            Grid.SetRow(_scroller, 1);
            root.Children.Add(_scroller);

            _content = new Border();
            _content.Width = 880;
            _content.HorizontalAlignment = HorizontalAlignment.Center;
            _content.Margin = new Thickness(0, 26, 0, 36);
            _content.Child = BuildCurrentView();
            _scroller.Content = _content;

            Border titleBar = BuildTitleBar();
            Grid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);
            return root;
        }

        private Border BuildTitleBar()
        {
            Border bar = new Border();
            bar.Height = 56;
            bar.Background = Ui.BrushFrom("#0D0F13");
            bar.BorderBrush = Ui.BrushFrom("#1C1F26");
            bar.BorderThickness = new Thickness(0, 0, 0, 1);
            bar.Padding = new Thickness(18, 0, 10, 0);

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions[0].Width = GridLength.Auto;
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions[2].Width = GridLength.Auto;

            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(Ui.BuildLogo(28));

            StackPanel names = new StackPanel();
            names.Margin = new Thickness(10, 0, 0, 0);
            names.VerticalAlignment = VerticalAlignment.Center;
            names.Children.Add(Ui.TextBlock("时间统计", 13.5, Ui.Text, FontWeights.SemiBold));
            TextBlock english = Ui.TextBlock("TIME TRACKER", 8.5, Ui.Dim, FontWeights.Medium);
            english.Margin = new Thickness(0, 1, 0, 0);
            names.Children.Add(english);
            left.Children.Add(names);
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.VerticalAlignment = VerticalAlignment.Center;
            right.HorizontalAlignment = HorizontalAlignment.Right;

            _statusDot = new Border();
            _statusDot.Width = 6;
            _statusDot.Height = 6;
            _statusDot.CornerRadius = new CornerRadius(3);
            _statusDot.Background = Ui.Green;
            _statusDot.VerticalAlignment = VerticalAlignment.Center;
            _statusDot.Margin = new Thickness(0, 0, 6, 0);

            _statusLabel = Ui.TextBlock("正在记录", 10.5, Ui.TextSoft);
            StackPanel statusContent = new StackPanel();
            statusContent.Orientation = Orientation.Horizontal;
            statusContent.Children.Add(_statusDot);
            statusContent.Children.Add(_statusLabel);

            _statusPill = new Border();
            _statusPill.Background = Ui.GreenSoft;
            _statusPill.CornerRadius = new CornerRadius(11);
            _statusPill.Padding = new Thickness(9, 4, 10, 4);
            _statusPill.VerticalAlignment = VerticalAlignment.Center;
            _statusPill.Margin = new Thickness(0, 0, 10, 0);
            _statusPill.Child = statusContent;
            right.Children.Add(_statusPill);

            Border settings = ChromeButton("\uE713", "设置", Ui.BrushFrom("#1D2129"), Brushes.White, ToggleSettings);
            settings.Margin = new Thickness(2, 0, 2, 0);
            right.Children.Add(settings);

            Border minimize = ChromeButton("\uE921", "最小化", Ui.BrushFrom("#1D2129"), Brushes.White,
                delegate { WindowState = WindowState.Minimized; });
            minimize.Margin = new Thickness(2, 0, 2, 0);
            right.Children.Add(minimize);

            Border close = ChromeButton("\uE8BB", "关闭到托盘", Ui.BrushFrom("#C42B1C"), Brushes.White,
                delegate { Close(); });
            close.Margin = new Thickness(2, 0, 0, 0);
            right.Children.Add(close);

            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
            bar.Child = grid;
            return bar;
        }

        private Border ChromeButton(string glyph, string tooltip, Brush hover, Brush glyphHover, Action action)
        {
            Border button = new Border();
            button.Width = 38;
            button.Height = 30;
            button.CornerRadius = new CornerRadius(8);
            button.Background = Brushes.Transparent;
            button.Cursor = Cursors.Hand;
            button.VerticalAlignment = VerticalAlignment.Center;
            button.Focusable = false;
            TextBlock icon = Ui.Glyph(glyph, 13, Ui.Muted);
            button.Child = icon;
            if (!string.IsNullOrEmpty(tooltip)) button.ToolTip = tooltip;
            button.MouseEnter += delegate
            {
                button.Background = hover;
                icon.Foreground = glyphHover;
            };
            button.MouseLeave += delegate
            {
                button.Background = Brushes.Transparent;
                icon.Foreground = Ui.Muted;
            };
            button.MouseLeftButtonUp += delegate
            {
                if (action != null) action();
            };
            WindowChrome.SetIsHitTestVisibleInChrome(button, true);
            return button;
        }

        private void UpdateContentWidth()
        {
            if (_scroller == null || _content == null) return;
            double viewport = _scroller.ViewportWidth;
            if (double.IsNaN(viewport) || viewport <= 0) viewport = ActualWidth;
            if (viewport <= 0) return;
            double width = Math.Min(920, Math.Max(600, viewport - 56));
            if (Math.Abs(_content.Width - width) > 0.5) _content.Width = width;
        }

        private UIElement BuildCurrentView()
        {
            if (_view == "settings") return BuildSettingsPage();
            if (_view == "detail") return BuildDetailPage();
            return BuildDashboardPage();
        }

        private void Rebuild(bool preserveScroll)
        {
            if (_disposed || _content == null) return;
            bool pageChanged = _renderedView != null && !string.Equals(_renderedView, _view, StringComparison.Ordinal);
            bool rangeChanged = _renderedView == "dashboard" && _view == "dashboard" &&
                (_renderedRangeMode != _rangeMode || _renderedAnchor != _anchor);
            int direction = 1;
            if (pageChanged) direction = _view == "dashboard" ? -1 : 1;
            else if (rangeChanged)
            {
                direction = _rangeMode != _renderedRangeMode
                    ? (_rangeMode > _renderedRangeMode ? 1 : -1)
                    : (_anchor > _renderedAnchor ? 1 : -1);
            }

            double offset = preserveScroll ? _scroller.VerticalOffset : 0;
            _content.Child = BuildCurrentView();
            _content.UpdateLayout();
            _scroller.ScrollToVerticalOffset(offset);
            _lastVersion = _store.Version;

            _renderedView = _view;
            _renderedRangeMode = _rangeMode;
            _renderedAnchor = _anchor;
            if (pageChanged || rangeChanged) AnimatePage(direction);
        }

        private void AnimatePage(int direction)
        {
            if (_store != null && _store.Lightweight) return;
            UIElement child = _content == null ? null : _content.Child as UIElement;
            if (child == null) return;

            TranslateTransform slide = new TranslateTransform();
            child.RenderTransform = slide;
            child.Opacity = 0;

            Duration duration = new Duration(TimeSpan.FromMilliseconds(230));
            CubicEase fadeEase = new CubicEase();
            fadeEase.EasingMode = EasingMode.EaseOut;
            DoubleAnimation fade = new DoubleAnimation(0, 1, duration);
            fade.EasingFunction = fadeEase;

            CubicEase slideEase = new CubicEase();
            slideEase.EasingMode = EasingMode.EaseOut;
            DoubleAnimation move = new DoubleAnimation(direction * 26, 0, duration);
            move.EasingFunction = slideEase;

            child.BeginAnimation(UIElement.OpacityProperty, fade);
            slide.BeginAnimation(TranslateTransform.XProperty, move);
        }

        private void ToggleSettings()
        {
            if (_view == "settings") _view = "dashboard";
            else _view = "settings";
            Rebuild(false);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _view != "dashboard")
            {
                _view = "dashboard";
                Rebuild(false);
                e.Handled = true;
            }
            else if (e.Key == Key.F5)
            {
                Rebuild(true);
                e.Handled = true;
            }
        }

        private UIElement BuildDashboardPage()
        {
            StackPanel page = new StackPanel();
            page.Children.Add(BuildDashboardHeader());

            DateTime start;
            DateTime dataEnd;
            DateTime displayEnd;
            GetRange(out start, out dataEnd, out displayEnd);
            page.Children.Add(BuildRangeRow(start, displayEnd));

            RangeData data = _store.BuildRange(start, dataEnd, null);
            page.Children.Add(BuildSummaryCard(data));
            page.Children.Add(BuildRankingCard(data));
            page.Children.Add(BuildFooter());
            return page;
        }

        private UIElement BuildDashboardHeader()
        {
            Grid header = new Grid();
            header.Margin = new Thickness(0, 0, 0, 18);
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions[1].Width = GridLength.Auto;

            StackPanel titles = new StackPanel();
            titles.Children.Add(Ui.TextBlock("使用统计", 26, Ui.Text, FontWeights.Bold));
            TextBlock subtitle = Ui.TextBlock("看看时间都去哪儿了", 12, Ui.Muted);
            subtitle.Margin = new Thickness(0, 6, 0, 0);
            titles.Children.Add(subtitle);
            Grid.SetColumn(titles, 0);
            header.Children.Add(titles);

            SegmentBar tabs = new SegmentBar(new string[] { "每天", "近7天", "本月" }, _rangeMode);
            tabs.HorizontalAlignment = HorizontalAlignment.Right;
            tabs.VerticalAlignment = VerticalAlignment.Bottom;
            tabs.SelectionChanged += delegate
            {
                if (tabs.SelectedIndex == _rangeMode) return;
                _rangeMode = tabs.SelectedIndex;
                _anchor = DateTime.Today;
                _showAllApps = false;
                Rebuild(false);
            };
            Grid.SetColumn(tabs, 1);
            header.Children.Add(tabs);
            return header;
        }

        private void GetRange(out DateTime start, out DateTime dataEnd, out DateTime displayEnd)
        {
            DateTime today = DateTime.Today;
            if (_anchor > today) _anchor = today;
            if (_rangeMode == 0)
            {
                start = _anchor.Date;
                dataEnd = start;
                displayEnd = start;
            }
            else if (_rangeMode == 1)
            {
                dataEnd = _anchor.Date;
                start = dataEnd.AddDays(-6);
                displayEnd = dataEnd;
            }
            else
            {
                start = new DateTime(_anchor.Year, _anchor.Month, 1);
                displayEnd = start.AddMonths(1).AddDays(-1);
                dataEnd = displayEnd > today ? today : displayEnd;
            }
        }

        private bool IsCurrentAnchor()
        {
            DateTime today = DateTime.Today;
            if (_rangeMode == 0 || _rangeMode == 1) return _anchor.Date == today;
            return _anchor.Year == today.Year && _anchor.Month == today.Month;
        }

        private UIElement BuildRangeRow(DateTime start, DateTime displayEnd)
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 0, 0, 16);
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions[1].Width = GridLength.Auto;
            row.ColumnDefinitions.Add(new ColumnDefinition());

            StackPanel nav = new StackPanel();
            nav.Orientation = Orientation.Horizontal;
            nav.HorizontalAlignment = HorizontalAlignment.Center;
            nav.VerticalAlignment = VerticalAlignment.Center;

            Border previous = SmallGlyphButton("\uE76B", "上一段", delegate { ShiftRange(-1); });
            previous.Margin = new Thickness(0, 0, 6, 0);
            nav.Children.Add(previous);

            TextBlock label = Ui.TextBlock(Ui.RangeLabel(start, displayEnd, _rangeMode == 0), 12.5, Ui.TextSoft, FontWeights.Medium);
            label.Width = 230;
            label.TextAlignment = TextAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            nav.Children.Add(label);

            bool canForward = !IsCurrentAnchor();
            Border next = SmallGlyphButton("\uE76C", "下一段", delegate { ShiftRange(1); });
            next.Margin = new Thickness(6, 0, 0, 0);
            next.IsEnabled = canForward;
            if (!canForward) next.Opacity = 0.3;
            nav.Children.Add(next);

            Grid.SetColumn(nav, 1);
            row.Children.Add(nav);

            if (!IsCurrentAnchor())
            {
                TextButton today = new TextButton("回到今天", Ui.Accent);
                today.VerticalAlignment = VerticalAlignment.Center;
                today.HorizontalAlignment = HorizontalAlignment.Right;
                today.Click += delegate
                {
                    _anchor = DateTime.Today;
                    Rebuild(false);
                };
                Grid.SetColumn(today, 2);
                row.Children.Add(today);
            }
            return row;
        }

        private void ShiftRange(int direction)
        {
            if (_rangeMode == 0) _anchor = _anchor.AddDays(direction);
            else if (_rangeMode == 1) _anchor = _anchor.AddDays(direction * 7);
            else _anchor = _anchor.AddMonths(direction);
            if (_anchor > DateTime.Today) _anchor = DateTime.Today;
            Rebuild(false);
        }

        private UIElement BuildSummaryCard(RangeData data)
        {
            StackPanel inner = new StackPanel();
            bool background = _store.TrackBackground;

            Grid summary = new Grid();
            summary.ColumnDefinitions.Add(new ColumnDefinition());
            summary.ColumnDefinitions.Add(new ColumnDefinition());
            summary.ColumnDefinitions[1].Width = GridLength.Auto;

            StackPanel left = new StackPanel();
            string averageText = _rangeMode == 0 ? "电脑使用时间" : "日均电脑使用时间";
            TextBlock averageLabel = Ui.TextBlock(averageText, 11.5, Ui.Muted);
            left.Children.Add(averageLabel);
            long bigValue = _rangeMode == 0 ? data.Total : data.Average;
            FrameworkElement big = Ui.BigDuration(bigValue, 52);
            big.Margin = new Thickness(0, 4, 0, 0);
            left.Children.Add(big);

            StackPanel subline = new StackPanel();
            subline.Orientation = Orientation.Horizontal;
            subline.Margin = new Thickness(0, 8, 0, 0);
            if (_rangeMode == 0)
            {
                subline.Children.Add(Ui.TextBlock("共 " + data.Apps.Count + " 个应用", 11.5, Ui.Dim));
                subline.Children.Add(Ui.TextBlock("  ·  更新于 " + DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture), 11.5, Ui.Dim));
            }
            else
            {
                subline.Children.Add(Ui.TextBlock((background ? "累计时长 " : "总时长 "), 11.5, Ui.Dim));
                subline.Children.Add(Ui.TextBlock(Ui.Duration(data.Total), 11.5, Ui.Accent, FontWeights.SemiBold));
                subline.Children.Add(Ui.TextBlock("  ·  记录 " + data.DayCount + " 天", 11.5, Ui.Dim));
            }
            left.Children.Add(subline);

            long splitKnown = data.ForegroundTotal + data.BackgroundTotal;
            if (splitKnown > 0 || data.UnclassifiedTotal > 0)
            {
                StackPanel split = new StackPanel();
                split.Orientation = Orientation.Horizontal;
                split.Margin = new Thickness(0, 7, 0, 0);
                if (splitKnown > 0)
                {
                    split.Children.Add(SplitLegend("前台", Ui.Duration(data.ForegroundTotal), Ui.Accent));
                    split.Children.Add(Ui.TextBlock("     ", 10.5, Ui.Dim));
                    split.Children.Add(SplitLegend("后台", Ui.Duration(data.BackgroundTotal), Ui.Green));
                }
                if (data.UnclassifiedTotal > 0)
                {
                    if (splitKnown > 0) split.Children.Add(Ui.TextBlock("  ·  ", 10, Ui.Dim));
                    string legacy = splitKnown > 0
                        ? "历史未区分 " + Ui.Duration(data.UnclassifiedTotal)
                        : "旧记录未区分前台/后台（" + Ui.Duration(data.UnclassifiedTotal) + "）";
                    split.Children.Add(Ui.TextBlock(legacy, 10, Ui.Dim));
                }
                left.Children.Add(split);
            }
            Grid.SetColumn(left, 0);
            summary.Children.Add(left);

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Center;

            if (_rangeMode == 0)
            {
                int peakHour = PeakHour(data.HourBins);
                string peakValue = peakHour >= 0 ? peakHour.ToString("00", CultureInfo.InvariantCulture) + ":00" : "--";
                right.Children.Add(MiniStat("高峰时段", peakValue, "最活跃的 1 小时"));
                right.Children.Add(VLine());
                right.Children.Add(MiniStat("使用应用", data.Apps.Count.ToString(CultureInfo.InvariantCulture), background ? "个应用" : "个前台应用"));
                right.Children.Add(VLine());
                string topName = data.Apps.Count > 0 && !string.IsNullOrEmpty(data.Apps[0].Name) ? data.Apps[0].Name : "--";
                string topSub = data.Apps.Count > 0 ? Ui.Duration(data.Apps[0].Seconds) : "暂无记录";
                right.Children.Add(MiniStat("最常使用", topName, topSub));
            }
            else
            {
                DayBar peak = PeakDay(data);
                right.Children.Add(MiniStat("单日最高", peak == null ? "--" : Ui.Duration(peak.Seconds),
                    peak == null ? "暂无记录" : peak.Date.ToString("M月d日", CultureInfo.InvariantCulture) + " " + Ui.WeekdayLong(peak.Date)));
                right.Children.Add(VLine());
                right.Children.Add(MiniStat("使用应用", data.Apps.Count.ToString(CultureInfo.InvariantCulture), background ? "个应用" : "个前台应用"));
                right.Children.Add(VLine());
                int activeDays = ActiveDays(data);
                int totalDays = _rangeMode == 2 ? DateTime.DaysInMonth(data.Start.Year, data.Start.Month) : data.DayCount;
                right.Children.Add(MiniStat("记录天数", activeDays + " / " + totalDays, "有使用记录"));
            }

            Grid.SetColumn(right, 1);
            summary.Children.Add(right);
            inner.Children.Add(summary);

            Grid chartHead = new Grid();
            chartHead.Margin = new Thickness(0, 22, 0, 0);
            chartHead.ColumnDefinitions.Add(new ColumnDefinition());
            chartHead.ColumnDefinitions.Add(new ColumnDefinition());
            chartHead.ColumnDefinitions[1].Width = GridLength.Auto;
            TextBlock chartTitle = Ui.TextBlock("时间分布", 12.5, Ui.TextSoft, FontWeights.SemiBold);
            chartHead.Children.Add(chartTitle);
            TextBlock chartMode = Ui.TextBlock(_rangeMode == 0 ? (background ? "按小时累计" : "按小时") : (background ? "按天累计" : "按天"), 10.5, Ui.Dim);
            chartMode.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(chartMode, 1);
            chartHead.Children.Add(chartMode);
            inner.Children.Add(chartHead);

            bool hourly;
            long average;
            List<ChartItem> items = BuildChartItems(data, out hourly, out average);
            UsageChart chart = new UsageChart();
            chart.Height = 214;
            chart.Margin = new Thickness(0, 4, 0, 0);
            chart.SetData(items, hourly, average);
            inner.Children.Add(chart);

            Border card = Ui.CardSurface(inner, 24);
            card.CornerRadius = new CornerRadius(20);
            return card;
        }

        private List<ChartItem> BuildChartItems(RangeData data, out bool hourly, out long average)
        {
            List<ChartItem> items = new List<ChartItem>();
            hourly = _rangeMode == 0;
            average = 0;
            DateTime today = DateTime.Today;

            if (hourly)
            {
                DateTime date = data.Start.Date;
                for (int hour = 0; hour < 24; hour++)
                {
                    ChartItem item = new ChartItem();
                    item.Label = hour.ToString(CultureInfo.InvariantCulture);
                    item.Seconds = data.HourBins[hour];
                    item.Tip = hour.ToString("00", CultureInfo.InvariantCulture) + ":00 - " + hour.ToString("00", CultureInfo.InvariantCulture) + ":59  ·  " + Ui.Duration(item.Seconds);
                    item.Highlight = date == today && hour == DateTime.Now.Hour;
                    items.Add(item);
                }
            }
            else
            {
                DateTime last = _rangeMode == 2
                    ? new DateTime(data.Start.Year, data.Start.Month, 1).AddMonths(1).AddDays(-1)
                    : data.Start.Date.AddDays(6);
                Dictionary<DateTime, long> byDate = new Dictionary<DateTime, long>();
                foreach (DayBar bar in data.Days) byDate[bar.Date.Date] = bar.Seconds;

                for (DateTime date = data.Start.Date; date <= last; date = date.AddDays(1))
                {
                    long seconds = 0;
                    byDate.TryGetValue(date, out seconds);
                    ChartItem item = new ChartItem();
                    item.Label = _rangeMode == 1 ? Ui.Weekday(date) : date.Day.ToString(CultureInfo.InvariantCulture);
                    item.Seconds = seconds;
                    item.Tip = date.ToString("M月d日", CultureInfo.InvariantCulture) + " " + Ui.WeekdayLong(date) +
                        "  ·  " + (seconds > 0 ? Ui.Duration(seconds) : "暂无记录");
                    item.Highlight = date == today;
                    items.Add(item);
                }
                average = data.Average;
            }
            return items;
        }

        private static int PeakHour(long[] bins)
        {
            int index = -1;
            long peak = 0;
            for (int i = 0; i < bins.Length; i++)
            {
                if (bins[i] > peak)
                {
                    peak = bins[i];
                    index = i;
                }
            }
            return index;
        }

        private static DayBar PeakDay(RangeData data)
        {
            DayBar peak = null;
            foreach (DayBar bar in data.Days)
            {
                if (bar.Seconds <= 0) continue;
                if (peak == null || bar.Seconds > peak.Seconds) peak = bar;
            }
            return peak;
        }

        private static int ActiveDays(RangeData data)
        {
            int count = 0;
            foreach (DayBar bar in data.Days)
            {
                if (bar.Seconds > 0) count++;
            }
            return count;
        }

        private static StackPanel MiniStat(string label, string value, string sub)
        {
            StackPanel panel = new StackPanel();
            panel.Width = 132;
            panel.VerticalAlignment = VerticalAlignment.Center;
            TextBlock labelBlock = Ui.TextBlock(label, 10, Ui.Dim);
            panel.Children.Add(labelBlock);
            TextBlock valueBlock = Ui.TextBlock(value, 15, Ui.Text, FontWeights.SemiBold);
            valueBlock.Margin = new Thickness(0, 5, 0, 0);
            valueBlock.MaxWidth = 132;
            valueBlock.TextTrimming = TextTrimming.CharacterEllipsis;
            panel.Children.Add(valueBlock);
            if (!string.IsNullOrEmpty(sub))
            {
                TextBlock subBlock = Ui.TextBlock(sub, 9.5, Ui.Muted);
                subBlock.Margin = new Thickness(0, 4, 0, 0);
                subBlock.MaxWidth = 132;
                subBlock.TextTrimming = TextTrimming.CharacterEllipsis;
                panel.Children.Add(subBlock);
            }
            return panel;
        }

        private static Border VLine()
        {
            Border line = new Border();
            line.Width = 1;
            line.Height = 38;
            line.Background = Ui.StrokeSoft;
            line.Margin = new Thickness(18, 0, 18, 0);
            line.VerticalAlignment = VerticalAlignment.Center;
            return line;
        }

        private UIElement BuildRankingCard(RangeData data)
        {
            StackPanel inner = new StackPanel();

            Grid header = new Grid();
            header.Margin = new Thickness(12, 10, 10, 6);
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions[1].Width = GridLength.Auto;

            TextBlock title = Ui.TextBlock("使用排行", 15, Ui.Text, FontWeights.SemiBold);
            title.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(title);

            StackPanel headerRight = new StackPanel();
            headerRight.Orientation = Orientation.Horizontal;
            headerRight.HorizontalAlignment = HorizontalAlignment.Right;
            headerRight.VerticalAlignment = VerticalAlignment.Center;
            TextBlock count = Ui.TextBlock(data.Apps.Count > 0 ? "共 " + data.Apps.Count + " 个" : "暂无记录", 10.5, Ui.Dim);
            count.VerticalAlignment = VerticalAlignment.Center;
            headerRight.Children.Add(count);
            if (data.Apps.Count > 8)
            {
                TextButton toggle = new TextButton(_showAllApps ? "收起" : "显示全部", Ui.Accent);
                toggle.Margin = new Thickness(4, 0, 0, 0);
                toggle.Click += delegate
                {
                    _showAllApps = !_showAllApps;
                    Rebuild(true);
                };
                headerRight.Children.Add(toggle);
            }
            Grid.SetColumn(headerRight, 1);
            header.Children.Add(headerRight);
            inner.Children.Add(header);

            if (data.Apps.Count == 0)
            {
                StackPanel empty = new StackPanel();
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Margin = new Thickness(20, 22, 20, 28);
                TextBlock icon = Ui.Glyph("\uE823", 22, Ui.Dim);
                icon.Margin = new Thickness(0, 0, 0, 10);
                empty.Children.Add(icon);
                TextBlock emptyTitle = Ui.TextBlock("还没有使用记录", 13, Ui.TextSoft);
                emptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Children.Add(emptyTitle);
                TextBlock emptySub = Ui.TextBlock("把窗口留在后台，正常使用电脑即可自动记录。", 10.5, Ui.Dim);
                emptySub.Margin = new Thickness(0, 5, 0, 0);
                emptySub.TextAlignment = TextAlignment.Center;
                emptySub.TextWrapping = TextWrapping.Wrap;
                emptySub.MaxWidth = 420;
                empty.Children.Add(emptySub);
                inner.Children.Add(empty);
            }
            else
            {
                StackPanel rows = new StackPanel();
                rows.Margin = new Thickness(0, 0, 0, 2);
                int limit = _showAllApps ? data.Apps.Count : Math.Min(8, data.Apps.Count);
                for (int i = 0; i < limit; i++)
                {
                    AppAgg app = data.Apps[i];
                    HoverBorder row = new HoverBorder();
                    row.NormalBackground = Brushes.Transparent;
                    row.HoverBackground = Ui.Card2;
                    row.CornerRadius = new CornerRadius(10);
                    row.Padding = new Thickness(10, 7, 10, 7);
                    row.Margin = new Thickness(2, 1, 2, 1);
                    row.Cursor = Cursors.Hand;
                    row.ToolTip = string.IsNullOrEmpty(app.Path) ? app.Key : app.Path;

                    Grid grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition());
                    grid.ColumnDefinitions[0].Width = new GridLength(42);
                    grid.ColumnDefinitions.Add(new ColumnDefinition());
                    grid.ColumnDefinitions.Add(new ColumnDefinition());
                    grid.ColumnDefinitions[2].Width = GridLength.Auto;

                    Border icon = Ui.AppIcon(app.Key, app.Name, app.Path, 34);
                    icon.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(icon, 0);
                    grid.Children.Add(icon);

                    StackPanel mid = new StackPanel();
                    mid.VerticalAlignment = VerticalAlignment.Center;
                    TextBlock name = Ui.TextBlock(string.IsNullOrEmpty(app.Name) ? "未知应用" : app.Name, 13, Ui.Text, FontWeights.Medium);
                    name.TextTrimming = TextTrimming.CharacterEllipsis;
                    mid.Children.Add(name);
                    UsageBar bar = new UsageBar();
                    bar.Ratio = data.Apps[0].Seconds > 0 ? (double)app.Seconds / data.Apps[0].Seconds : 0;
                    mid.Children.Add(bar);
                    Grid.SetColumn(mid, 1);
                    grid.Children.Add(mid);

                    TextBlock duration = Ui.TextBlock(Ui.Duration(app.Seconds), 12.5, Ui.TextSoft, FontWeights.SemiBold);
                    duration.VerticalAlignment = VerticalAlignment.Center;
                    duration.Margin = new Thickness(12, 0, 2, 0);
                    Grid.SetColumn(duration, 2);
                    grid.Children.Add(duration);

                    row.Child = grid;
                    AppAgg captured = app;
                    Ui.AttachClick(row, delegate { OpenDetail(captured); });
                    rows.Children.Add(row);

                    if (i < limit - 1)
                    {
                        Border separator = Ui.Separator();
                        separator.Margin = new Thickness(52, 0, 12, 0);
                        rows.Children.Add(separator);
                    }
                }
                inner.Children.Add(rows);
            }

            Border card = Ui.CardSurface(inner, 8);
            card.CornerRadius = new CornerRadius(18);
            return card;
        }

        private UIElement BuildFooter()
        {
            string text = _store.TrackBackground
                ? "前台与后台应用均计时  ·  多应用并行累计  ·  全部数据保存在本机"
                : "仅统计前台活跃窗口  ·  空闲时自动暂停  ·  全部数据保存在本机";
            TextBlock footer = Ui.TextBlock(text, 10.5, Ui.Dim);
            footer.HorizontalAlignment = HorizontalAlignment.Center;
            footer.Margin = new Thickness(0, 18, 0, 0);
            return footer;
        }

        private void OpenDetail(AppAgg app)
        {
            _view = "detail";
            _detailKey = app.Key;
            _detailName = app.Name;
            _detailPath = app.Path;
            Rebuild(false);
        }

        private UIElement BuildDetailPage()
        {
            StackPanel page = new StackPanel();
            page.Children.Add(BackLink("返回排行", delegate
            {
                _view = "dashboard";
                Rebuild(false);
            }));

            TextBlock eyebrow = Ui.TextBlock("应用详情", 10.5, Ui.Accent, FontWeights.SemiBold);
            eyebrow.Margin = new Thickness(2, 0, 0, 9);
            page.Children.Add(eyebrow);

            DateTime start;
            DateTime dataEnd;
            DateTime displayEnd;
            GetRange(out start, out dataEnd, out displayEnd);
            long rangeTotal = _store.TotalForKey(_detailKey, start, dataEnd);
            UsageBreakdown breakdown = _store.GetBreakdown(_detailKey, start, dataEnd);

            Border headerCard = Ui.CardSurface(null, 20);
            headerCard.CornerRadius = new CornerRadius(18);
            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions[0].Width = GridLength.Auto;
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions[2].Width = GridLength.Auto;

            Border icon = Ui.AppIcon(_detailKey, _detailName, _detailPath, 54);
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new Thickness(0, 0, 16, 0);
            Grid.SetColumn(icon, 0);
            header.Children.Add(icon);

            StackPanel names = new StackPanel();
            names.VerticalAlignment = VerticalAlignment.Center;
            TextBlock name = Ui.TextBlock(string.IsNullOrEmpty(_detailName) ? "未知应用" : _detailName, 22, Ui.Text, FontWeights.Bold);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            names.Children.Add(name);
            TextBlock path = Ui.TextBlock(Ui.ShortPath(_detailPath), 10.5, Ui.Dim);
            path.Margin = new Thickness(0, 5, 0, 0);
            path.MaxWidth = 470;
            path.TextTrimming = TextTrimming.CharacterEllipsis;
            names.Children.Add(path);
            Grid.SetColumn(names, 1);
            header.Children.Add(names);

            StackPanel totalPane = new StackPanel();
            totalPane.HorizontalAlignment = HorizontalAlignment.Right;
            totalPane.VerticalAlignment = VerticalAlignment.Center;
            TextBlock totalLabel = Ui.TextBlock("所选范围", 10, Ui.Dim);
            totalLabel.HorizontalAlignment = HorizontalAlignment.Right;
            totalPane.Children.Add(totalLabel);
            FrameworkElement totalValue = Ui.BigDuration(rangeTotal, 32);
            totalValue.HorizontalAlignment = HorizontalAlignment.Right;
            totalValue.Margin = new Thickness(0, 3, 0, 0);
            totalPane.Children.Add(totalValue);
            TextBlock totalRange = Ui.TextBlock(Ui.RangeLabel(start, displayEnd, _rangeMode == 0), 10, Ui.Muted);
            totalRange.HorizontalAlignment = HorizontalAlignment.Right;
            totalRange.Margin = new Thickness(0, 2, 0, 0);
            totalPane.Children.Add(totalRange);
            Grid.SetColumn(totalPane, 2);
            header.Children.Add(totalPane);

            headerCard.Child = header;
            page.Children.Add(headerCard);
            page.Children.Add(BuildBreakdownCard(breakdown));

            DateTime today = DateTime.Today;
            Grid stats = new Grid();
            stats.Margin = new Thickness(0, 14, 0, 0);
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            stats.ColumnDefinitions.Add(new ColumnDefinition());

            Border todayCard = SmallStatCard("今日", Ui.Duration(_store.TotalForKey(_detailKey, today, today)), today.ToString("M月d日", CultureInfo.InvariantCulture));
            todayCard.Margin = new Thickness(0, 0, 7, 0);
            Grid.SetColumn(todayCard, 0);
            stats.Children.Add(todayCard);

            DateTime weekStart = today.AddDays(-6);
            Border weekCard = SmallStatCard("近 7 天", Ui.Duration(_store.TotalForKey(_detailKey, weekStart, today)),
                Ui.RangeLabel(weekStart, today, false));
            weekCard.Margin = new Thickness(3, 0, 3, 0);
            Grid.SetColumn(weekCard, 1);
            stats.Children.Add(weekCard);

            DateTime monthStart = new DateTime(today.Year, today.Month, 1);
            Border monthCard = SmallStatCard("本月", Ui.Duration(_store.TotalForKey(_detailKey, monthStart, today)),
                Ui.RangeLabel(monthStart, today, false));
            monthCard.Margin = new Thickness(7, 0, 0, 0);
            Grid.SetColumn(monthCard, 2);
            stats.Children.Add(monthCard);
            page.Children.Add(stats);

            Border chartCard = Ui.CardSurface(null, 22);
            chartCard.CornerRadius = new CornerRadius(18);
            chartCard.Margin = new Thickness(0, 14, 0, 0);
            StackPanel chartInner = new StackPanel();

            Grid chartHeader = new Grid();
            chartHeader.ColumnDefinitions.Add(new ColumnDefinition());
            chartHeader.ColumnDefinitions.Add(new ColumnDefinition());
            chartHeader.ColumnDefinitions[1].Width = GridLength.Auto;
            TextBlock chartTitle = Ui.TextBlock("最近 7 天", 13, Ui.Text, FontWeights.SemiBold);
            chartHeader.Children.Add(chartTitle);

            List<ChartItem> items = new List<ChartItem>();
            long weekTotal = 0;
            for (int i = 0; i < 7; i++)
            {
                DateTime date = today.AddDays(i - 6);
                long seconds = _store.TotalForKey(_detailKey, date, date);
                weekTotal += seconds;
                ChartItem item = new ChartItem();
                item.Label = Ui.Weekday(date);
                item.Seconds = seconds;
                item.Tip = date.ToString("M月d日", CultureInfo.InvariantCulture) + " " + Ui.WeekdayLong(date) + "  ·  " + Ui.Duration(seconds);
                item.Highlight = date == today;
                items.Add(item);
            }
            long average = weekTotal / 7;
            TextBlock averageLabel = Ui.TextBlock("平均 " + Ui.Duration(average) + " / 天", 10.5, Ui.Dim);
            averageLabel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(averageLabel, 1);
            chartHeader.Children.Add(averageLabel);
            chartInner.Children.Add(chartHeader);

            UsageChart chart = new UsageChart();
            chart.Height = 220;
            chart.Margin = new Thickness(0, 8, 0, 0);
            chart.SetData(items, false, average);
            chartInner.Children.Add(chart);
            chartCard.Child = chartInner;
            page.Children.Add(chartCard);

            string detailFooter = _store.TrackBackground
                ? "前台与后台运行时间均累计，多个应用可同时计时，空闲时间不计入"
                : "仅统计该应用处于前台活跃窗口的时间，空闲时间不计入";
            TextBlock footer = Ui.TextBlock(detailFooter, 10.5, Ui.Dim);
            footer.HorizontalAlignment = HorizontalAlignment.Center;
            footer.Margin = new Thickness(0, 18, 0, 0);
            page.Children.Add(footer);
            return page;
        }

        private Border BuildBreakdownCard(UsageBreakdown data)
        {
            Border card = Ui.CardSurface(null, 20);
            card.CornerRadius = new CornerRadius(18);
            card.Margin = new Thickness(0, 14, 0, 0);
            StackPanel inner = new StackPanel();

            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions[1].Width = GridLength.Auto;
            TextBlock title = Ui.TextBlock("前台与后台", 13, Ui.Text, FontWeights.SemiBold);
            head.Children.Add(title);
            TextBlock scope = Ui.TextBlock("所选范围", 10, Ui.Dim);
            scope.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(scope, 1);
            head.Children.Add(scope);
            inner.Children.Add(head);

            Grid columns = new Grid();
            columns.Margin = new Thickness(0, 14, 0, 0);
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions.Add(new ColumnDefinition());

            bool onlyLegacy = data.Known == 0 && data.Total > 0;
            StackPanel fg = SplitStat("前台时间", data.Foreground, onlyLegacy, Ui.Accent);
            Grid.SetColumn(fg, 0);
            columns.Children.Add(fg);

            StackPanel bg = SplitStat("后台时间", data.Background, onlyLegacy, Ui.Green);
            Grid.SetColumn(bg, 1);
            columns.Children.Add(bg);
            inner.Children.Add(columns);

            Grid bar = new Grid();
            bar.Height = 7;
            bar.Margin = new Thickness(0, 14, 0, 0);
            bar.Background = Ui.Field;
            bar.ClipToBounds = true;
            long total = data.Total > 0 ? data.Total : 1;
            AddBarSegment(bar, data.Foreground, total, Ui.Accent);
            AddBarSegment(bar, data.Background, total, Ui.Green);
            AddBarSegment(bar, data.Unclassified, total, Ui.Dim);
            inner.Children.Add(bar);

            if (data.Unclassified > 0)
            {
                TextBlock legacy = Ui.TextBlock(
                    onlyLegacy
                        ? "旧记录只有合计，暂无法拆分前台/后台；从本次更新后开始记录明细。"
                        : "其中 " + Ui.Duration(data.Unclassified) + " 来自旧版记录，未区分前台/后台。",
                    10, Ui.Dim);
                legacy.Margin = new Thickness(0, 8, 0, 0);
                legacy.TextWrapping = TextWrapping.Wrap;
                inner.Children.Add(legacy);
            }

            card.Child = inner;
            return card;
        }

        private static void AddBarSegment(Grid bar, long seconds, long total, Brush brush)
        {
            if (seconds <= 0) return;
            ColumnDefinition column = new ColumnDefinition();
            column.Width = new GridLength(seconds, GridUnitType.Star);
            bar.ColumnDefinitions.Add(column);
            Border segment = new Border();
            segment.Background = brush;
            segment.Margin = new Thickness(0, 0, 1, 0);
            Grid.SetColumn(segment, bar.ColumnDefinitions.Count - 1);
            bar.Children.Add(segment);
        }

        private static StackPanel SplitStat(string label, long seconds, bool legacy, Brush color)
        {
            StackPanel panel = new StackPanel();
            StackPanel caption = new StackPanel();
            caption.Orientation = Orientation.Horizontal;
            System.Windows.Shapes.Ellipse dot = Ui.Dot(color, 6);
            dot.Margin = new Thickness(0, 0, 7, 0);
            caption.Children.Add(dot);
            caption.Children.Add(Ui.TextBlock(label, 10.5, Ui.Muted));
            panel.Children.Add(caption);
            FrameworkElement number;
            if (legacy) number = Ui.TextBlock("--", 27, Ui.TextSoft, FontWeights.Bold);
            else number = Ui.BigDuration(seconds, 27);
            number.Margin = new Thickness(0, 4, 0, 0);
            panel.Children.Add(number);
            return panel;
        }

        private static StackPanel SplitLegend(string label, string value, Brush color)
        {
            StackPanel panel = new StackPanel();
            panel.Orientation = Orientation.Horizontal;
            panel.VerticalAlignment = VerticalAlignment.Center;
            System.Windows.Shapes.Ellipse dot = Ui.Dot(color, 5);
            dot.Margin = new Thickness(0, 0, 6, 0);
            panel.Children.Add(dot);
            panel.Children.Add(Ui.TextBlock(label + " ", 10.5, Ui.Muted));
            panel.Children.Add(Ui.TextBlock(value, 10.5, Ui.TextSoft, FontWeights.SemiBold));
            return panel;
        }

        private Border SmallStatCard(string label, string value, string sub)
        {
            StackPanel stack = new StackPanel();
            stack.Children.Add(Ui.TextBlock(label, 10.5, Ui.Dim));
            TextBlock valueBlock = Ui.TextBlock(value, 19, Ui.Text, FontWeights.SemiBold);
            valueBlock.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(valueBlock);
            if (!string.IsNullOrEmpty(sub))
            {
                TextBlock subBlock = Ui.TextBlock(sub, 10, Ui.Muted);
                subBlock.Margin = new Thickness(0, 4, 0, 0);
                subBlock.TextTrimming = TextTrimming.CharacterEllipsis;
                stack.Children.Add(subBlock);
            }
            Border card = new Border();
            card.Background = Ui.Card2;
            card.BorderBrush = Ui.StrokeSoft;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(14);
            card.Padding = new Thickness(17, 14, 17, 14);
            card.Child = stack;
            return card;
        }

        private UIElement BuildSettingsPage()
        {
            StackPanel page = new StackPanel();
            page.Children.Add(BackLink("返回统计", delegate
            {
                _view = "dashboard";
                Rebuild(false);
            }));

            StackPanel heading = new StackPanel();
            heading.Margin = new Thickness(0, 0, 0, 6);
            heading.Children.Add(Ui.TextBlock("设置", 26, Ui.Text, FontWeights.Bold));
            TextBlock subtitle = Ui.TextBlock("调整记录方式、应用名称与过滤", 12, Ui.Muted);
            subtitle.Margin = new Thickness(0, 6, 0, 0);
            heading.Children.Add(subtitle);
            page.Children.Add(heading);

            page.Children.Add(SectionHeader("记录设置"));
            StackPanel settings = new StackPanel();

            Switch pauseSwitch = new Switch();
            pauseSwitch.IsOn = _store.IsPaused;
            pauseSwitch.Toggled += delegate
            {
                _store.SetPaused(pauseSwitch.IsOn);
                UpdateStatus();
            };
            AddSettingRow(settings, SettingRow("暂停统计", "暂停后不再记录任何应用的使用时间", pauseSwitch), true);

            Switch backgroundSwitch = new Switch();
            backgroundSwitch.IsOn = _store.TrackBackground;
            backgroundSwitch.Toggled += delegate
            {
                _store.SetTrackBackground(backgroundSwitch.IsOn);
            };
            AddSettingRow(settings, SettingRow("记录后台应用", "应用最小化或在后台运行时也累计时长；多个应用可同时计时", backgroundSwitch), true);

            Switch lightweightSwitch = new Switch();
            lightweightSwitch.IsOn = _store.Lightweight;
            lightweightSwitch.Toggled += delegate
            {
                _store.SetLightweight(lightweightSwitch.IsOn);
                ApplyLightweightMode();
            };
            AddSettingRow(settings, SettingRow("轻量模式", "关闭页面过渡动画，降低界面刷新和后台扫描频率，减少处理器与内存占用", lightweightSwitch), true);

            int[] idleValues = new int[] { 60, 120, 300, 600 };
            string[] idleLabels = new string[] { "1 分钟", "2 分钟", "5 分钟", "10 分钟" };
            int idleIndex = 0;
            int closest = int.MaxValue;
            for (int i = 0; i < idleValues.Length; i++)
            {
                int difference = Math.Abs(_store.IdleSeconds - idleValues[i]);
                if (difference < closest)
                {
                    closest = difference;
                    idleIndex = i;
                }
            }
            SegmentBar idleBar = new SegmentBar(idleLabels, idleIndex);
            idleBar.SelectionChanged += delegate { _store.SetIdleSeconds(idleValues[idleBar.SelectedIndex]); };
            AddSettingRow(settings, SettingRow("空闲判定", "超过所选时间没有操作时，自动停止计时", idleBar), true);

            Switch autoSwitch = new Switch();
            autoSwitch.IsOn = _store.AutoStart;
            autoSwitch.Toggled += delegate
            {
                if (!_store.SetAutoStart(autoSwitch.IsOn))
                {
                    autoSwitch.IsOn = !autoSwitch.IsOn;
                    MessageBox.Show(this, "无法修改开机启动设置，请检查系统权限。", "时间统计",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            AddSettingRow(settings, SettingRow("开机自动启动", "登录 Windows 后在后台自动开始记录", autoSwitch), false);

            Border settingsCard = Ui.CardSurface(settings, 0);
            settingsCard.CornerRadius = new CornerRadius(16);
            page.Children.Add(settingsCard);

            page.Children.Add(SectionHeader("应用管理"));
            page.Children.Add(BuildExclusionCard());

            page.Children.Add(SectionHeader("数据与关于"));
            page.Children.Add(BuildDataCard());
            page.Children.Add(BuildFooter());
            return page;
        }

        private UIElement BuildExclusionCard()
        {
            Dictionary<string, long> totals;
            List<AppMeta> apps = GatherApps(out totals);
            int excludedCount = 0;
            foreach (AppMeta app in apps)
            {
                if (_store.IsExcluded(app.Key)) excludedCount++;
            }

            StackPanel cardContent = new StackPanel();

            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions[1].Width = GridLength.Auto;
            StackPanel titleStack = new StackPanel();
            titleStack.Children.Add(Ui.TextBlock("应用列表", 13, Ui.TextSoft, FontWeights.SemiBold));
            TextBlock hint = Ui.TextBlock("关闭开关不再统计；点击「改名」自定义显示名称", 10.5, Ui.Dim);
            hint.Margin = new Thickness(0, 3, 0, 0);
            titleStack.Children.Add(hint);
            header.Children.Add(titleStack);

            TextBlock excludedLabel = Ui.TextBlock("已忽略 " + excludedCount + " 个应用", 10.5, Ui.Muted);
            excludedLabel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(excludedLabel, 1);
            header.Children.Add(excludedLabel);
            cardContent.Children.Add(header);

            TextBox searchBox;
            Grid searchHost = CreateSearchBox("搜索应用", out searchBox);
            searchHost.Margin = new Thickness(0, 13, 0, 0);
            searchHost.HorizontalAlignment = HorizontalAlignment.Left;
            cardContent.Children.Add(searchHost);

            StackPanel rows = new StackPanel();
            List<AppFilterEntry> entries = new List<AppFilterEntry>();
            for (int i = 0; i < apps.Count; i++)
            {
                AppMeta app = apps[i];
                long total;
                totals.TryGetValue(app.Key, out total);
                bool excluded = _store.IsExcluded(app.Key);

                HoverBorder row = new HoverBorder();
                row.NormalBackground = Brushes.Transparent;
                row.HoverBackground = Ui.Card2;
                row.CornerRadius = new CornerRadius(9);
                row.Padding = new Thickness(8, 7, 8, 7);
                row.Margin = new Thickness(0, 1, 0, 1);
                row.ToolTip = string.IsNullOrEmpty(app.Path) ? app.Key : app.Path;

                Grid grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions[0].Width = new GridLength(36);
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions[2].Width = GridLength.Auto;
                grid.ColumnDefinitions[3].Width = GridLength.Auto;

                Border icon = Ui.AppIcon(app.Key, DisplayName(app), app.Path, 30);
                icon.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(icon, 0);
                grid.Children.Add(icon);

                StackPanel middle = new StackPanel();
                middle.VerticalAlignment = VerticalAlignment.Center;
                TextBlock name = Ui.TextBlock(DisplayName(app), 12, Ui.Text, FontWeights.Medium);
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                middle.Children.Add(name);
                TextBlock usage = Ui.TextBlock(excluded ? "已忽略 · 不计入统计" : UsageSummary(total), 10, Ui.Dim);
                usage.Margin = new Thickness(0, 2, 0, 0);
                usingTextTrimming(usage);
                middle.Children.Add(usage);
                Grid.SetColumn(middle, 1);
                grid.Children.Add(middle);

                TextButton rename = new TextButton("改名", Ui.Accent);
                rename.VerticalAlignment = VerticalAlignment.Center;
                rename.Margin = new Thickness(4, 0, 8, 0);
                rename.ToolTip = "自定义这个应用显示的名称";
                AppMeta renameApp = app;
                rename.Click += delegate { RenameApp(renameApp); };
                Grid.SetColumn(rename, 2);
                grid.Children.Add(rename);

                Switch monitor = new Switch();
                monitor.IsOn = !excluded;
                monitor.VerticalAlignment = VerticalAlignment.Center;
                monitor.Toggled += delegate
                {
                    bool enabled = monitor.IsOn;
                    _store.SetExcluded(app.Key, !enabled);
                    usage.Text = enabled ? UsageSummary(total) : "已忽略 · 不计入统计";
                    UpdateExcludedLabel(excludedLabel, apps);
                };
                Grid.SetColumn(monitor, 3);
                grid.Children.Add(monitor);

                row.Child = grid;
                StackPanel container = new StackPanel();
                container.Children.Add(row);
                if (i < apps.Count - 1)
                {
                    Border separator = Ui.Separator();
                    separator.Margin = new Thickness(42, 0, 8, 0);
                    container.Children.Add(separator);
                }
                rows.Children.Add(container);

                AppFilterEntry entry = new AppFilterEntry();
                entry.Container = container;
                entry.Search = (DisplayName(app) + " " + SafeName(app) + " " + (app.Path == null ? string.Empty : app.Path) + " " + app.Key).ToLowerInvariant();
                entries.Add(entry);
            }

            TextBlock emptyHint = Ui.TextBlock(apps.Count == 0 ? "还没有记录到应用，使用后会出现在这里" : "没有匹配的应用", 10.5, Ui.Dim);
            emptyHint.HorizontalAlignment = HorizontalAlignment.Center;
            emptyHint.Margin = new Thickness(0, 22, 0, 16);
            emptyHint.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            rows.Children.Add(emptyHint);

            ScrollViewer listScroll = new ScrollViewer();
            listScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            listScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            listScroll.CanContentScroll = false;
            listScroll.MaxHeight = 360;
            listScroll.Margin = new Thickness(0, 8, 0, 0);
            listScroll.Content = rows;
            cardContent.Children.Add(listScroll);

            searchBox.TextChanged += delegate
            {
                string query = searchBox.Text.Trim().ToLowerInvariant();
                bool any = false;
                foreach (AppFilterEntry entry in entries)
                {
                    bool visible = query.Length == 0 || entry.Search.IndexOf(query, StringComparison.Ordinal) >= 0;
                    entry.Container.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    if (visible) any = true;
                }
                emptyHint.Text = query.Length == 0 ? "还没有记录到应用" : "没有匹配的应用";
                emptyHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            };

            Border card = Ui.CardSurface(cardContent, 16);
            card.CornerRadius = new CornerRadius(16);
            return card;
        }

        private static void usingTextTrimming(TextBlock block)
        {
            block.TextTrimming = TextTrimming.CharacterEllipsis;
            block.MaxWidth = 430;
        }

        private List<AppMeta> GatherApps(out Dictionary<string, long> totals)
        {
            Dictionary<string, AppMeta> map = new Dictionary<string, AppMeta>(StringComparer.OrdinalIgnoreCase);
            foreach (AppMeta meta in _store.GetKnownApps())
            {
                if (meta == null || string.IsNullOrEmpty(meta.Key)) continue;
                map[meta.Key] = meta;
            }
            totals = _store.TotalsIncludingExcluded(DateTime.Today.AddDays(-29), DateTime.Today);
            foreach (string key in totals.Keys)
            {
                if (map.ContainsKey(key)) continue;
                AppMeta meta = _store.GetAppMeta(key);
                if (meta == null) meta = new AppMeta();
                meta.Key = key;
                map[key] = meta;
            }

            Dictionary<string, long> totalsMap = totals;
            List<AppMeta> apps = new List<AppMeta>(map.Values);
            apps.Sort(delegate(AppMeta a, AppMeta b)
            {
                long totalA;
                long totalB;
                totalsMap.TryGetValue(a.Key, out totalA);
                totalsMap.TryGetValue(b.Key, out totalB);
                int compare = totalB.CompareTo(totalA);
                if (compare != 0) return compare;
                return string.Compare(SafeName(a), SafeName(b), StringComparison.CurrentCulture);
            });
            return apps;
        }

        private string DisplayName(AppMeta meta)
        {
            if (meta == null) return "未知应用";
            return _store.GetDisplayName(meta.Key, SafeName(meta));
        }

        private void RenameApp(AppMeta app)
        {
            if (app == null || string.IsNullOrEmpty(app.Key)) return;
            string fallback = SafeName(app);
            string current = _store.GetDisplayName(app.Key, fallback);
            RenameDialog dialog = new RenameDialog(current, fallback);
            dialog.Owner = this;
            if (dialog.ShowDialog() != true) return;

            string name = dialog.NewName;
            if (name.Length == 0 || string.Equals(name, fallback, StringComparison.Ordinal))
                _store.SetCustomName(app.Key, null);
            else
                _store.SetCustomName(app.Key, name);

            if (_view == "detail" && string.Equals(_detailKey, app.Key, StringComparison.OrdinalIgnoreCase))
                _detailName = _store.GetDisplayName(app.Key, fallback);
            Rebuild(true);
        }

        private static string SafeName(AppMeta meta)
        {
            if (meta == null) return "未知应用";
            if (!string.IsNullOrEmpty(meta.Name)) return meta.Name;
            if (!string.IsNullOrEmpty(meta.Path))
            {
                string name = Path.GetFileNameWithoutExtension(meta.Path);
                if (!string.IsNullOrEmpty(name)) return name;
            }
            if (!string.IsNullOrEmpty(meta.Key))
            {
                int index = meta.Key.IndexOf(':');
                if (index >= 0 && index + 1 < meta.Key.Length) return meta.Key.Substring(index + 1);
                return meta.Key;
            }
            return "未知应用";
        }

        private static string UsageSummary(long total)
        {
            return total > 0 ? Ui.Duration(total) + " · 最近 30 天" : "最近 30 天未使用";
        }

        private void UpdateExcludedLabel(TextBlock label, List<AppMeta> apps)
        {
            int count = 0;
            foreach (AppMeta app in apps)
            {
                if (_store.IsExcluded(app.Key)) count++;
            }
            label.Text = "已忽略 " + count + " 个应用";
        }

        private UIElement BuildDataCard()
        {
            StackPanel rows = new StackPanel();

            Border openButton = PillButton("打开文件夹", "\uE8B7", Ui.TextSoft, false, delegate
            {
                DataStore.OpenDataFolder();
            });
            AddSettingRow(rows, SettingRow("数据存储位置", _store.DataDirectory, openButton), true);

            Border clearButton = PillButton("清空数据", "\uE74D", Ui.Red, true, delegate
            {
                ConfirmDialog dialog = new ConfirmDialog("清空使用记录",
                    "这会删除本机保存的全部使用记录，且无法恢复。不监控列表和偏好设置会保留。", "清空", true);
                dialog.Owner = this;
                if (dialog.ShowDialog() == true)
                {
                    _store.ClearAllData();
                    _view = "dashboard";
                    _rangeMode = 0;
                    _anchor = DateTime.Today;
                    _showAllApps = false;
                    Rebuild(false);
                }
            });
            AddSettingRow(rows, SettingRow("清空使用记录", "删除所有统计历史，不影响应用过滤设置", clearButton), true);

            StackPanel about = new StackPanel();
            about.Margin = new Thickness(18, 15, 18, 18);
            TextBlock aboutTitle = Ui.TextBlock("时间统计 1.2.1", 12, Ui.TextSoft, FontWeights.Medium);
            about.Children.Add(aboutTitle);
            TextBlock aboutText = Ui.TextBlock("只在本地记录，不联网、不上传任何数据。", 10.5, Ui.Dim);
            aboutText.Margin = new Thickness(0, 4, 0, 0);
            about.Children.Add(aboutText);
            rows.Children.Add(about);

            Border card = Ui.CardSurface(rows, 0);
            card.CornerRadius = new CornerRadius(16);
            return card;
        }

        private void AddSettingRow(StackPanel stack, UIElement row, bool divider)
        {
            stack.Children.Add(row);
            if (divider)
            {
                Border line = Ui.Separator();
                line.Margin = new Thickness(18, 0, 18, 0);
                stack.Children.Add(line);
            }
        }

        private static UIElement SettingRow(string title, string description, FrameworkElement control)
        {
            Border row = new Border();
            row.Padding = new Thickness(18, 13, 18, 13);
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions[1].Width = GridLength.Auto;

            StackPanel text = new StackPanel();
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Children.Add(Ui.TextBlock(title, 12.5, Ui.Text, FontWeights.Medium));
            TextBlock desc = Ui.TextBlock(description, 10.5, Ui.Dim);
            desc.Margin = new Thickness(0, 4, 0, 0);
            desc.TextWrapping = TextWrapping.Wrap;
            desc.MaxWidth = 520;
            desc.ToolTip = description;
            text.Children.Add(desc);
            Grid.SetColumn(text, 0);
            grid.Children.Add(text);

            control.VerticalAlignment = VerticalAlignment.Center;
            control.Margin = new Thickness(20, 0, 0, 0);
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);

            row.Child = grid;
            return row;
        }

        private static StackPanel SectionHeader(string text)
        {
            StackPanel header = new StackPanel();
            header.Orientation = Orientation.Horizontal;
            header.Margin = new Thickness(2, 22, 0, 9);
            Border bar = new Border();
            bar.Width = 3;
            bar.Height = 12;
            bar.CornerRadius = new CornerRadius(2);
            bar.Background = Ui.Accent;
            bar.VerticalAlignment = VerticalAlignment.Center;
            bar.Margin = new Thickness(0, 0, 8, 0);
            header.Children.Add(bar);
            TextBlock label = Ui.TextBlock(text, 11.5, Ui.TextSoft, FontWeights.SemiBold);
            label.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(label);
            return header;
        }

        private UIElement BackLink(string text, Action action)
        {
            Border link = new Border();
            link.Padding = new Thickness(8, 5, 9, 5);
            link.CornerRadius = new CornerRadius(8);
            link.Background = Brushes.Transparent;
            link.Cursor = Cursors.Hand;
            link.HorizontalAlignment = HorizontalAlignment.Left;
            link.Margin = new Thickness(-8, 0, 0, 12);
            StackPanel content = new StackPanel();
            content.Orientation = Orientation.Horizontal;
            TextBlock arrow = Ui.Glyph("\uE72B", 11, Ui.Muted);
            arrow.Margin = new Thickness(0, 0, 5, 0);
            content.Children.Add(arrow);
            content.Children.Add(Ui.TextBlock(text, 11.5, Ui.Muted));
            link.Child = content;
            link.MouseEnter += delegate
            {
                link.Background = Ui.Card2;
                arrow.Foreground = Ui.Text;
            };
            link.MouseLeave += delegate
            {
                link.Background = Brushes.Transparent;
                arrow.Foreground = Ui.Muted;
            };
            Ui.AttachClick(link, action);
            return link;
        }

        private Border SmallGlyphButton(string glyph, string tooltip, Action action)
        {
            Border button = new Border();
            button.Width = 30;
            button.Height = 30;
            button.CornerRadius = new CornerRadius(8);
            button.Background = Brushes.Transparent;
            button.Cursor = Cursors.Hand;
            button.Focusable = false;
            TextBlock icon = Ui.Glyph(glyph, 11, Ui.Muted);
            button.Child = icon;
            button.ToolTip = tooltip;
            button.MouseEnter += delegate
            {
                button.Background = Ui.Card2;
                icon.Foreground = Ui.Text;
            };
            button.MouseLeave += delegate
            {
                button.Background = Brushes.Transparent;
                icon.Foreground = Ui.Muted;
            };
            button.MouseLeftButtonUp += delegate
            {
                if (action != null) action();
            };
            return button;
        }

        private Border PillButton(string text, string glyph, Brush foreground, bool danger, Action action)
        {
            Brush normal = danger ? Ui.RedSoft : Ui.Card2;
            Brush hover = danger ? Ui.BrushFrom("#3A1E24") : Ui.BrushFrom("#252A34");
            Border button = new Border();
            button.Background = normal;
            button.BorderBrush = danger ? Ui.BrushFrom("#4A252B") : Ui.Stroke;
            button.BorderThickness = new Thickness(1);
            button.CornerRadius = new CornerRadius(9);
            button.Padding = new Thickness(12, 7, 12, 7);
            button.Cursor = Cursors.Hand;
            button.VerticalAlignment = VerticalAlignment.Center;
            StackPanel content = new StackPanel();
            content.Orientation = Orientation.Horizontal;
            if (!string.IsNullOrEmpty(glyph))
            {
                TextBlock icon = Ui.Glyph(glyph, 12, foreground);
                icon.Margin = new Thickness(0, 0, 6, 0);
                content.Children.Add(icon);
            }
            content.Children.Add(Ui.TextBlock(text, 12, foreground, FontWeights.Medium));
            button.Child = content;
            button.MouseEnter += delegate { button.Background = hover; };
            button.MouseLeave += delegate { button.Background = normal; };
            Ui.AttachClick(button, action);
            return button;
        }

        private Grid CreateSearchBox(string placeholder, out TextBox box)
        {
            Grid host = new Grid();
            host.Width = 280;
            host.Height = 34;

            Border frame = new Border();
            frame.Background = Ui.Field;
            frame.BorderBrush = Ui.Stroke;
            frame.BorderThickness = new Thickness(1);
            frame.CornerRadius = new CornerRadius(8);
            frame.Padding = new Thickness(10, 0, 10, 0);
            host.Children.Add(frame);

            Grid inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition());
            inner.ColumnDefinitions[0].Width = GridLength.Auto;
            inner.ColumnDefinitions.Add(new ColumnDefinition());
            TextBlock icon = Ui.Glyph("\uE721", 11.5, Ui.Dim);
            icon.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(icon, 0);
            inner.Children.Add(icon);

            box = new TextBox();
            box.Background = Brushes.Transparent;
            box.BorderThickness = new Thickness(0);
            box.Foreground = Ui.Text;
            box.CaretBrush = Ui.Accent;
            box.SelectionBrush = Ui.AccentSoft;
            box.FontFamily = Ui.Font;
            box.FontSize = 12;
            box.VerticalContentAlignment = VerticalAlignment.Center;
            box.Padding = new Thickness(0);
            SpellCheck.SetIsEnabled(box, false);
            Grid.SetColumn(box, 1);
            inner.Children.Add(box);

            TextBlock hint = Ui.TextBlock(placeholder, 11.5, Ui.Dim);
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.Margin = new Thickness(1, 0, 0, 0);
            hint.IsHitTestVisible = false;
            Grid.SetColumn(hint, 1);
            inner.Children.Add(hint);

            TextBox textBox = box;
            box.TextChanged += delegate
            {
                hint.Visibility = string.IsNullOrEmpty(textBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            };
            box.GotFocus += delegate
            {
                frame.BorderBrush = Ui.Accent;
                hint.Visibility = Visibility.Collapsed;
            };
            box.LostFocus += delegate
            {
                frame.BorderBrush = Ui.Stroke;
                hint.Visibility = string.IsNullOrEmpty(textBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            };

            frame.Child = inner;
            return host;
        }

        private void ApplyLightweightMode()
        {
            bool lightweight = _store != null && _store.Lightweight;
            if (_tickTimer != null)
                _tickTimer.Interval = TimeSpan.FromSeconds(lightweight ? 5 : 1);
            if (_refreshTimer != null)
                _refreshTimer.Interval = TimeSpan.FromSeconds(lightweight ? 30 : 15);
            PerformanceMode.ApplyPriority(lightweight);
            _trimTicks = 0;
            if (lightweight && !IsVisible && _startHidden) PerformanceMode.TrimWorkingSet();
        }

        private void UpdateStatus()
        {
            if (_disposed || _statusLabel == null) return;
            bool paused = _store != null && _store.IsPaused;
            if (!_statusPaused.HasValue || _statusPaused.Value != paused)
            {
                _statusPaused = paused;
                _statusLabel.Text = paused ? "已暂停" : "正在记录";
                _statusDot.Background = paused ? Ui.Amber : Ui.Green;
                _statusPill.Background = paused ? Ui.AmberSoft : Ui.GreenSoft;
            }
            if (_tray != null)
            {
                string text = paused ? "时间统计 · 已暂停" : "时间统计 · 正在记录";
                if (!string.Equals(_tray.Text, text, StringComparison.Ordinal)) _tray.Text = text;
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_disposed) return;
            DateTime today = DateTime.Today;
            if (today != _lastToday)
            {
                DateTime previous = _lastToday;
                _lastToday = today;
                if (_anchor.Date == previous) _anchor = today;
                if (_view == "dashboard") Rebuild(false);
            }
            if (_store.Lightweight && !IsVisible)
            {
                _trimTicks++;
                if (_trimTicks >= 60)
                {
                    _trimTicks = 0;
                    PerformanceMode.TrimWorkingSet();
                }
            }
            else
            {
                _trimTicks = 0;
            }
            UpdateStatus();
        }

        private void OnRefreshTimer(object sender, EventArgs e)
        {
            if (_disposed || _store == null) return;
            if (!IsVisible || WindowState == WindowState.Minimized || _view != "dashboard") return;
            if (_store.Version == _lastVersion) return;
            Rebuild(true);
        }

        public void ShowFromTray()
        {
            if (_disposed) return;
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            UpdateContentWidth();
            _trimTicks = 0;
            UpdateStatus();
            if (_view == "dashboard") Rebuild(false);
        }

        private void SetupTray()
        {
            try
            {
                _trayIcon = TrayIconFactory.Load();
                _trayMenu = new WinForms.ContextMenuStrip();
                _trayMenu.Font = new Drawing.Font("Microsoft YaHei UI", 9f);
                _trayMenu.BackColor = Drawing.Color.FromArgb(23, 25, 31);
                _trayMenu.ForeColor = Drawing.Color.FromArgb(224, 228, 234);
                _trayMenu.ShowImageMargin = false;
                _trayMenu.ShowCheckMargin = true;
                _trayMenu.Renderer = new DarkMenuRenderer();
                _trayMenu.Padding = new WinForms.Padding(4, 4, 4, 4);

                WinForms.ToolStripMenuItem openItem = new WinForms.ToolStripMenuItem("打开时间统计");
                openItem.Font = new Drawing.Font("Microsoft YaHei UI", 9f, Drawing.FontStyle.Bold);
                openItem.Padding = new WinForms.Padding(4, 3, 4, 3);
                openItem.Click += delegate { ShowFromTray(); };
                _trayMenu.Items.Add(openItem);
                _trayMenu.Items.Add(new WinForms.ToolStripSeparator());

                _pauseItem = new WinForms.ToolStripMenuItem("暂停统计");
                _pauseItem.Padding = new WinForms.Padding(4, 3, 4, 3);
                _pauseItem.Click += delegate
                {
                    _store.SetPaused(!_store.IsPaused);
                    UpdateStatus();
                    UpdateTrayMenu();
                    if (IsVisible) Rebuild(true);
                };
                _trayMenu.Items.Add(_pauseItem);

                _autoStartItem = new WinForms.ToolStripMenuItem("开机自动启动");
                _autoStartItem.Padding = new WinForms.Padding(4, 3, 4, 3);
                _autoStartItem.Click += delegate
                {
                    if (!_store.SetAutoStart(!_store.AutoStart))
                    {
                        ShowTrayBalloon("无法修改开机启动", "请检查当前账户的系统权限。");
                    }
                    UpdateTrayMenu();
                    if (IsVisible && _view == "settings") Rebuild(true);
                };
                _trayMenu.Items.Add(_autoStartItem);
                _trayMenu.Items.Add(new WinForms.ToolStripSeparator());

                WinForms.ToolStripMenuItem exitItem = new WinForms.ToolStripMenuItem("退出");
                exitItem.Padding = new WinForms.Padding(4, 3, 4, 3);
                exitItem.Click += delegate
                {
                    _exiting = true;
                    Close();
                };
                _trayMenu.Items.Add(exitItem);
                _trayMenu.Opening += delegate { UpdateTrayMenu(); };

                _tray = new WinForms.NotifyIcon();
                _tray.Icon = _trayIcon;
                _tray.Text = "时间统计 · 正在记录";
                _tray.Visible = true;
                _tray.ContextMenuStrip = _trayMenu;
                _tray.DoubleClick += delegate { ShowFromTray(); };
                UpdateTrayMenu();
            }
            catch (Exception ex)
            {
                Logger.Error("SetupTray", ex);
                _tray = null;
            }
        }

        private void UpdateTrayMenu()
        {
            if (_tray == null || _store == null) return;
            _pauseItem.Text = _store.IsPaused ? "继续统计" : "暂停统计";
            _autoStartItem.Checked = _store.AutoStart;
            string text = _store.IsPaused ? "时间统计 · 已暂停" : "时间统计 · 正在记录";
            if (!string.Equals(_tray.Text, text, StringComparison.Ordinal)) _tray.Text = text;
        }

        private void ShowTrayBalloon(string title, string message)
        {
            if (_tray == null) return;
            try
            {
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = message;
                _tray.BalloonTipIcon = WinForms.ToolTipIcon.Info;
                _tray.ShowBalloonTip(3000);
            }
            catch
            {
            }
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_exiting) return;
            if (_tray == null)
            {
                _exiting = true;
                return;
            }
            e.Cancel = true;
            Hide();
            _trimTicks = 0;
            if (_store != null && _store.Lightweight)
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(PerformanceMode.TrimWorkingSet));
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                ShowTrayBalloon("时间统计仍在后台运行", "窗口已隐藏，统计会继续。右击托盘图标可以暂停或退出。");
            }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            DisposeApp();
            if (Application.Current != null) Application.Current.Shutdown();
        }

        public void DisposeApp()
        {
            if (_disposed) return;
            _disposed = true;
            if (_tickTimer != null)
            {
                _tickTimer.Stop();
                _tickTimer = null;
            }
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer = null;
            }
            if (_tray != null)
            {
                try { _tray.Visible = false; } catch { }
                _tray.Dispose();
                _tray = null;
            }
            if (_trayMenu != null)
            {
                _trayMenu.Dispose();
                _trayMenu = null;
            }
            if (_trayIcon != null)
            {
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            if (_tracker != null)
            {
                _tracker.Dispose();
                _tracker = null;
            }
            if (_store != null)
            {
                _store.Dispose();
                _store = null;
            }
        }
    }
}