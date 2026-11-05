using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace TimeTrack
{
    public static class Ui
    {
        public static bool IsDark = true;
        public static event EventHandler ThemeChanged;

        public static readonly Brush Bg = BrushFrom("#0B0C0F");
        public static readonly Brush BgSoft = BrushFrom("#101216");
        public static readonly Brush Card = BrushFrom("#17191F");
        public static readonly Brush Card2 = BrushFrom("#1D2027");
        public static readonly Brush Field = BrushFrom("#111318");
        public static readonly Brush Stroke = BrushFrom("#2A2E37");
        public static readonly Brush StrokeSoft = BrushFrom("#22252C");
        public static readonly Brush Text = BrushFrom("#F3F5F8");
        public static readonly Brush TextSoft = BrushFrom("#C9CDD4");
        public static readonly Brush Muted = BrushFrom("#969CA6");
        public static readonly Brush Dim = BrushFrom("#6C727D");
        public static readonly Brush Accent = BrushFrom("#3B82F6");
        public static readonly Brush AccentLight = BrushFrom("#6EA8FF");
        public static readonly Brush AccentSoft = BrushFrom("#1A2A44");
        public static readonly Brush Green = BrushFrom("#35D399");
        public static readonly Brush GreenSoft = BrushFrom("#12261D");
        public static readonly Brush Amber = BrushFrom("#F5B34A");
        public static readonly Brush AmberSoft = BrushFrom("#2A2114");
        public static readonly Brush Red = BrushFrom("#F26D78");
        public static readonly Brush RedSoft = BrushFrom("#2B171B");
        public static readonly Brush Track = BrushFrom("#2A2E37");
        public static readonly Brush TitleBar = BrushFrom("#0D0F13");
        public static readonly Brush TitleBarBorder = BrushFrom("#1C1F26");
        public static readonly Brush CardHover = BrushFrom("#252A34");
        public static readonly Brush DangerHover = BrushFrom("#3A1E24");
        public static readonly Brush DangerBorder = BrushFrom("#4A252B");
        public static readonly Brush AccentHover = BrushFrom("#2F6FE0");
        public static readonly Brush TrackHover = BrushFrom("#3B404A");
        public static readonly Brush HoverOverlay = BrushFrom("#14FFFFFF");
        public static readonly Brush TipBg = BrushFrom("#FA181B22");
        public static readonly Brush ChromeGlyphHover = BrushFrom("#FFFFFF");
        public static readonly Brush CloseHover = BrushFrom("#C42B1C");
        public static readonly Brush DangerText = BrushFrom("#FF9AA4");
        public static readonly LinearGradientBrush AccentGradient = BuildAccentGradient();
        public static readonly LinearGradientBrush LogoGradient = BuildLogoGradient();

        public static readonly FontFamily Font = new FontFamily("Microsoft YaHei UI");
        public static readonly FontFamily GlyphFont = new FontFamily("Segoe MDL2 Assets");

        private static readonly string[] Palette = new string[]
        {
            "#3B82F6", "#8B5CF6", "#EC4899", "#F59E0B", "#10B981", "#06B6D4", "#EF4444", "#6366F1"
        };

        public static Brush BrushFrom(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        private static bool _animateColors;

        public static void ApplyTheme(bool dark)
        {
            ApplyTheme(dark, false);
        }

        public static void ApplyTheme(bool dark, bool animate)
        {
            IsDark = dark;
            _animateColors = animate;
            if (dark)
            {
                SetColor(Bg, "#0B0C0F");
                SetColor(BgSoft, "#101216");
                SetColor(Card, "#17191F");
                SetColor(Card2, "#1D2027");
                SetColor(Field, "#111318");
                SetColor(Stroke, "#2A2E37");
                SetColor(StrokeSoft, "#22252C");
                SetColor(Text, "#F3F5F8");
                SetColor(TextSoft, "#C9CDD4");
                SetColor(Muted, "#969CA6");
                SetColor(Dim, "#6C727D");
                SetColor(Accent, "#3B82F6");
                SetColor(AccentLight, "#6EA8FF");
                SetColor(AccentSoft, "#1A2A44");
                SetColor(Green, "#35D399");
                SetColor(GreenSoft, "#12261D");
                SetColor(Amber, "#F5B34A");
                SetColor(AmberSoft, "#2A2114");
                SetColor(Red, "#F26D78");
                SetColor(RedSoft, "#2B171B");
                SetColor(Track, "#2A2E37");
                SetColor(TitleBar, "#0D0F13");
                SetColor(TitleBarBorder, "#1C1F26");
                SetColor(CardHover, "#252A34");
                SetColor(DangerHover, "#3A1E24");
                SetColor(DangerBorder, "#4A252B");
                SetColor(AccentHover, "#2F6FE0");
                SetColor(TrackHover, "#3B404A");
                SetColor(HoverOverlay, "#14FFFFFF");
                SetColor(TipBg, "#FA181B22");
                SetColor(ChromeGlyphHover, "#FFFFFF");
                SetColor(CloseHover, "#C42B1C");
                SetColor(DangerText, "#FF9AA4");
            }
            else
            {
                SetColor(Bg, "#F3F5F9");
                SetColor(BgSoft, "#EAEFF7");
                SetColor(Card, "#FFFFFF");
                SetColor(Card2, "#F2F6FC");
                SetColor(Field, "#F7F9FD");
                SetColor(Stroke, "#D9E0EA");
                SetColor(StrokeSoft, "#E8EDF4");
                SetColor(Text, "#151A21");
                SetColor(TextSoft, "#49515E");
                SetColor(Muted, "#6B7280");
                SetColor(Dim, "#98A0AC");
                SetColor(Accent, "#2563EB");
                SetColor(AccentLight, "#1D4ED8");
                SetColor(AccentSoft, "#E4EDFF");
                SetColor(Green, "#0F9F6E");
                SetColor(GreenSoft, "#E3F7EF");
                SetColor(Amber, "#D97706");
                SetColor(AmberSoft, "#FEF3C7");
                SetColor(Red, "#DC2626");
                SetColor(RedSoft, "#FEE2E2");
                SetColor(Track, "#E4EAF2");
                SetColor(TitleBar, "#FFFFFF");
                SetColor(TitleBarBorder, "#E3E8F0");
                SetColor(CardHover, "#EEF3FA");
                SetColor(DangerHover, "#FDE8E8");
                SetColor(DangerBorder, "#F5C2C2");
                SetColor(AccentHover, "#1D4ED8");
                SetColor(TrackHover, "#D5DEEA");
                SetColor(HoverOverlay, "#12000000");
                SetColor(TipBg, "#FFFFFFFF");
                SetColor(ChromeGlyphHover, "#1F2937");
                SetColor(CloseHover, "#C42B1C");
                SetColor(DangerText, "#B91C1C");
            }
            _animateColors = false;
            EventHandler handler = ThemeChanged;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        private static void SetColor(Brush brush, string hex)
        {
            SolidColorBrush solid = brush as SolidColorBrush;
            if (solid == null || solid.IsFrozen) return;

            Color target = (Color)ColorConverter.ConvertFromString(hex);
            Color from = solid.Color;
            solid.BeginAnimation(SolidColorBrush.ColorProperty, null);
            solid.Color = target;

            if (_animateColors && from != target)
            {
                ColorAnimation animation = new ColorAnimation(from, target, TimeSpan.FromMilliseconds(220));
                animation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                solid.BeginAnimation(SolidColorBrush.ColorProperty, animation);
            }
        }

        private static LinearGradientBrush BuildAccentGradient()
        {
            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new System.Windows.Point(0, 0);
            brush.EndPoint = new System.Windows.Point(1, 1);
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#3B82F6"), 0));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#245BC8"), 1));
            brush.Freeze();
            return brush;
        }

        private static LinearGradientBrush BuildLogoGradient()
        {
            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new System.Windows.Point(0, 0);
            brush.EndPoint = new System.Windows.Point(1, 1);
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#2563EB"), 0));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#4F46E5"), 1));
            brush.Freeze();
            return brush;
        }

        public static TextBlock TextBlock(string text, double size, Brush brush)
        {
            return TextBlock(text, size, brush, FontWeights.Normal);
        }

        public static TextBlock TextBlock(string text, double size, Brush brush, FontWeight weight)
        {
            TextBlock block = new TextBlock();
            block.Text = text;
            block.FontFamily = Font;
            block.FontSize = size;
            block.Foreground = brush;
            block.FontWeight = weight;
            TextOptions.SetTextFormattingMode(block, TextFormattingMode.Display);
            TextOptions.SetTextHintingMode(block, TextHintingMode.Fixed);
            return block;
        }

        public static TextBlock Glyph(string glyph, double size, Brush brush)
        {
            TextBlock block = new TextBlock();
            block.Text = glyph;
            block.FontFamily = GlyphFont;
            block.FontSize = size;
            block.Foreground = brush;
            block.VerticalAlignment = VerticalAlignment.Center;
            block.HorizontalAlignment = HorizontalAlignment.Center;
            return block;
        }

        public static Border CardSurface(UIElement child, double padding)
        {
            Border border = new Border();
            border.Background = Card;
            border.BorderBrush = Stroke;
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(18);
            border.Padding = new Thickness(padding);
            border.Child = child;
            return border;
        }

        public static Border BuildLogo(double size)
        {
            Border outer = new Border();
            outer.Width = size;
            outer.Height = size;
            outer.CornerRadius = new CornerRadius(size * 0.28);
            outer.Background = LogoGradient;
            outer.BorderBrush = BrushFrom("#93C5FD");
            outer.BorderThickness = new Thickness(Math.Max(0.6, size * 0.018));
            outer.VerticalAlignment = VerticalAlignment.Center;

            Canvas canvas = new Canvas();
            canvas.Width = size;
            canvas.Height = size;
            canvas.ClipToBounds = true;

            double cx = size / 2.0;
            double cy = size / 2.0;
            double radius = size * 0.29;
            double stroke = Math.Max(1.5, size * 0.07);
            double startAngle = 40.0;
            double endAngle = 340.0;
            Point p1 = PointOnCircle(cx, cy, radius, startAngle);
            Point p2 = PointOnCircle(cx, cy, radius, endAngle);

            PathFigure figure = new PathFigure();
            figure.StartPoint = p1;
            figure.IsClosed = false;
            figure.Segments.Add(new ArcSegment(p2, new Size(radius, radius), 0, true,
                SweepDirection.Clockwise, true));
            PathGeometry geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            System.Windows.Shapes.Path ring = new System.Windows.Shapes.Path();
            ring.Data = geometry;
            ring.Stroke = Brushes.White;
            ring.StrokeThickness = stroke;
            ring.StrokeStartLineCap = PenLineCap.Round;
            ring.StrokeEndLineCap = PenLineCap.Round;
            canvas.Children.Add(ring);

            Ellipse tip = new Ellipse();
            tip.Width = size * 0.115;
            tip.Height = size * 0.115;
            tip.Fill = Amber;
            Canvas.SetLeft(tip, p2.X - tip.Width / 2.0);
            Canvas.SetTop(tip, p2.Y - tip.Height / 2.0);
            canvas.Children.Add(tip);

            AddHand(canvas, cx, cy, size * 0.145, -122.0, Math.Max(1.5, size * 0.052));
            AddHand(canvas, cx, cy, size * 0.205, -58.0, Math.Max(1.3, size * 0.043));

            Ellipse center = new Ellipse();
            center.Width = size * 0.13;
            center.Height = size * 0.13;
            center.Fill = Brushes.White;
            Canvas.SetLeft(center, cx - center.Width / 2.0);
            Canvas.SetTop(center, cy - center.Height / 2.0);
            canvas.Children.Add(center);

            Ellipse centerDot = new Ellipse();
            centerDot.Width = size * 0.055;
            centerDot.Height = size * 0.055;
            centerDot.Fill = Amber;
            Canvas.SetLeft(centerDot, cx - centerDot.Width / 2.0);
            Canvas.SetTop(centerDot, cy - centerDot.Height / 2.0);
            canvas.Children.Add(centerDot);

            outer.Child = canvas;
            return outer;
        }

        private static Point PointOnCircle(double cx, double cy, double radius, double degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            return new Point(cx + radius * Math.Cos(radians), cy + radius * Math.Sin(radians));
        }

        private static void AddHand(Canvas canvas, double cx, double cy, double length, double degrees, double thickness)
        {
            Point end = PointOnCircle(cx, cy, length, degrees);
            Line line = new Line();
            line.X1 = cx;
            line.Y1 = cy;
            line.X2 = end.X;
            line.Y2 = end.Y;
            line.Stroke = Brushes.White;
            line.StrokeThickness = thickness;
            line.StrokeStartLineCap = PenLineCap.Round;
            line.StrokeEndLineCap = PenLineCap.Round;
            canvas.Children.Add(line);
        }

        public static Border AppIcon(string key, string path, double size)
        {
            return AppIcon(key, null, path, size);
        }

        public static Border AppIcon(string key, string name, string path, double size)
        {
            Border border = new Border();
            border.Width = size;
            border.Height = size;
            border.CornerRadius = new CornerRadius(size * 0.26);
            border.Background = Card2;
            border.ClipToBounds = true;

            ImageSource source = IconCache.Get(key, path);
            if (source != null)
            {
                Image image = new Image();
                image.Source = source;
                image.Stretch = Stretch.Uniform;
                image.Margin = new Thickness(size * 0.08);
                border.Child = image;
            }
            else
            {
                string label = DisplayLabel(name, key, path);
                string letter = string.IsNullOrEmpty(label) ? "?" : label.Substring(0, 1);
                Grid background = new Grid();
                background.Background = LetterBrush(key);
                TextBlock text = TextBlock(letter, size * 0.38, Brushes.White, FontWeights.SemiBold);
                text.HorizontalAlignment = HorizontalAlignment.Center;
                text.VerticalAlignment = VerticalAlignment.Center;
                background.Children.Add(text);
                border.Child = background;
            }
            return border;
        }

        private static string DisplayLabel(string name, string key, string path)
        {
            if (!string.IsNullOrEmpty(name)) return name;
            if (!string.IsNullOrEmpty(path))
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(fileName)) return fileName;
            }
            if (!string.IsNullOrEmpty(key))
            {
                int index = key.IndexOf(':');
                if (index >= 0 && index + 1 < key.Length) return key.Substring(index + 1);
                return key;
            }
            return null;
        }

        private static Brush LetterBrush(string key)
        {
            int hash = 0;
            if (!string.IsNullOrEmpty(key))
            {
                for (int i = 0; i < key.Length; i++) hash = unchecked(hash * 31 + key[i]);
            }
            int index = Math.Abs(hash) % Palette.Length;
            return BrushFrom(Palette[index]);
        }

        public static string Duration(long seconds)
        {
            if (seconds <= 0) return "0 分钟";
            long minutes = seconds / 60;
            if (minutes < 1) return "<1 分钟";
            if (minutes < 60) return minutes + " 分钟";
            long hours = minutes / 60;
            long rest = minutes % 60;
            if (rest == 0) return hours + " 小时";
            return hours + " 小时 " + rest + " 分钟";
        }

        public static string DurationAxis(long seconds)
        {
            if (seconds <= 0) return "0";
            long minutes = seconds / 60;
            if (minutes < 60) return minutes + " 分钟";
            long hours = minutes / 60;
            return hours + " 小时";
        }

        public static FrameworkElement BigDuration(long seconds, double numberSize)
        {
            long minutes = Math.Max(0, seconds) / 60;
            long hours = minutes / 60;
            long rest = minutes % 60;

            StackPanel panel = new StackPanel();
            panel.Orientation = Orientation.Horizontal;
            panel.VerticalAlignment = VerticalAlignment.Bottom;

            if (hours > 0)
            {
                panel.Children.Add(TextBlock(hours.ToString(CultureInfo.InvariantCulture), numberSize, Text, FontWeights.Bold));
                panel.Children.Add(TextBlock(" 小时", numberSize * 0.42, TextSoft, FontWeights.Normal));
                if (rest > 0 || minutes < 60)
                {
                    TextBlock gap = TextBlock(" ", numberSize * 0.3, Text);
                    panel.Children.Add(gap);
                    panel.Children.Add(TextBlock(rest.ToString(CultureInfo.InvariantCulture), numberSize, Text, FontWeights.Bold));
                    panel.Children.Add(TextBlock(" 分钟", numberSize * 0.42, TextSoft, FontWeights.Normal));
                }
            }
            else
            {
                panel.Children.Add(TextBlock(minutes.ToString(CultureInfo.InvariantCulture), numberSize, Text, FontWeights.Bold));
                panel.Children.Add(TextBlock(" 分钟", numberSize * 0.42, TextSoft, FontWeights.Normal));
            }

            foreach (UIElement child in panel.Children)
            {
                TextBlock block = child as TextBlock;
                if (block != null)
                {
                    block.VerticalAlignment = VerticalAlignment.Bottom;
                    block.Margin = new Thickness(0, 0, 0, numberSize * 0.07);
                }
            }
            return panel;
        }

        public static string Weekday(DateTime date)
        {
            string[] names = new string[] { "日", "一", "二", "三", "四", "五", "六" };
            return names[(int)date.DayOfWeek];
        }

        public static string WeekdayLong(DateTime date)
        {
            return "周" + Weekday(date);
        }

        public static string RangeLabel(DateTime start, DateTime end, bool isDay)
        {
            DateTime today = DateTime.Today;
            if (isDay)
            {
                string label = start.ToString("M月d日", CultureInfo.InvariantCulture);
                if (start.Date == today) return label + "（今天）";
                return label + " " + WeekdayLong(start);
            }
            if (start.Year != end.Year)
                return start.ToString("yyyy年M月d日", CultureInfo.InvariantCulture) + " - " + end.ToString("yyyy年M月d日", CultureInfo.InvariantCulture);
            if (start.Month != end.Month)
                return start.ToString("M月d日", CultureInfo.InvariantCulture) + " - " + end.ToString("M月d日", CultureInfo.InvariantCulture);
            return start.ToString("M月d日", CultureInfo.InvariantCulture) + " - " + end.ToString("M月d日", CultureInfo.InvariantCulture);
        }

        public static string TimeOfDay()
        {
            return DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        public static string ShortPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "系统应用 · 未获取到路径";
            return path;
        }

        public static void AttachClick(UIElement element, Action action)
        {
            element.MouseLeftButtonUp += delegate { if (action != null) action(); };
        }

        public static Border Separator()
        {
            Border line = new Border();
            line.Height = 1;
            line.Background = StrokeSoft;
            line.Margin = new Thickness(0, 0, 0, 0);
            return line;
        }

        public static Ellipse Dot(Brush brush, double size)
        {
            Ellipse dot = new Ellipse();
            dot.Width = size;
            dot.Height = size;
            dot.Fill = brush;
            dot.VerticalAlignment = VerticalAlignment.Center;
            return dot;
        }
    }

    public class HoverBorder : Border
    {
        public Brush NormalBackground;
        public Brush HoverBackground;

        public HoverBorder()
        {
            Background = Brushes.Transparent;
            NormalBackground = Brushes.Transparent;
            HoverBackground = Ui.Card2;
            MouseEnter += delegate { Background = HoverBackground; };
            MouseLeave += delegate { Background = NormalBackground; };
        }
    }

    public class GlyphButton : Border
    {
        public event EventHandler Click;
        private readonly TextBlock _glyph;
        private Brush _normal;
        private Brush _hover;

        public GlyphButton(string glyph, string tooltip, double size)
        {
            Width = size;
            Height = size;
            CornerRadius = new CornerRadius(9);
            _normal = Brushes.Transparent;
            _hover = Ui.Card2;
            Background = _normal;
            Cursor = Cursors.Hand;
            _glyph = Ui.Glyph(glyph, 15, Ui.Muted);
            Child = _glyph;
            if (!string.IsNullOrEmpty(tooltip)) ToolTip = tooltip;
            MouseEnter += delegate
            {
                Background = _hover;
                _glyph.Foreground = Ui.Text;
            };
            MouseLeave += delegate
            {
                Background = _normal;
                _glyph.Foreground = Ui.Muted;
            };
            MouseLeftButtonUp += delegate
            {
                if (Click != null) Click(this, EventArgs.Empty);
            };
        }

        public void SetHover(Brush hover, Brush glyphHover)
        {
            _normal = Brushes.Transparent;
            Background = _normal;
            _hover = hover;
            _glyph.Foreground = Ui.Muted;
            MouseEnter += delegate { _glyph.Foreground = glyphHover; };
        }
    }

    public class TextButton : Border
    {
        public event EventHandler Click;
        private readonly TextBlock _label;
        private readonly Brush _normalText;

        public TextButton(string text, Brush textBrush)
        {
            Padding = new Thickness(10, 5, 10, 5);
            CornerRadius = new CornerRadius(8);
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            _normalText = textBrush;
            _label = Ui.TextBlock(text, 12.5, textBrush, FontWeights.Medium);
            Child = _label;
            MouseEnter += delegate { Background = Ui.Card2; _label.Foreground = Ui.Text; };
            MouseLeave += delegate { Background = Brushes.Transparent; _label.Foreground = _normalText; };
            MouseLeftButtonUp += delegate
            {
                if (Click != null) Click(this, EventArgs.Empty);
            };
        }
    }

    public static class IconCache
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public static ImageSource Get(string key, string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            string cacheKey = key + "|" + path;
            lock (Gate)
            {
                ImageSource existing;
                if (Cache.TryGetValue(cacheKey, out existing)) return existing;
            }

            try
            {
                using (System.Drawing.Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    if (icon == null) return null;
                    BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromWidthAndHeight(64, 64));
                    source.Freeze();
                    lock (Gate)
                    {
                        Cache[cacheKey] = source;
                    }
                    return source;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}