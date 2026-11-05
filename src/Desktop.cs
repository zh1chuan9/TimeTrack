using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace TimeTrack
{
    public static class WindowFx
    {
        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmWindowCornerPreference = 33;
        private const int DwmBorderColor = 34;
        private const int WmGetMinMaxInfo = 0x0024;
        private const uint MonitorDefaultToNearest = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public Point Reserved;
            public Point MaxSize;
            public Point MaxPosition;
            public Point MinTrackSize;
            public Point MaxTrackSize;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        public static void Apply(Window window)
        {
            window.SourceInitialized += delegate
            {
                try
                {
                    IntPtr handle = new WindowInteropHelper(window).Handle;
                    int round = 2;
                    DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref round, sizeof(int));
                    HwndSource source = HwndSource.FromHwnd(handle);
                    if (source != null) source.AddHook(WindowProc);
                }
                catch
                {
                }
                SetDark(window, Ui.IsDark);
            };
        }

        private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmGetMinMaxInfo)
            {
                try
                {
                    MinMaxInfo info = (MinMaxInfo)Marshal.PtrToStructure(lParam, typeof(MinMaxInfo));
                    IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
                    if (monitor != IntPtr.Zero)
                    {
                        MonitorInfo monitorInfo = new MonitorInfo();
                        monitorInfo.Size = Marshal.SizeOf(typeof(MonitorInfo));
                        if (GetMonitorInfo(monitor, ref monitorInfo))
                        {
                            info.MaxPosition.X = monitorInfo.Work.Left - monitorInfo.Monitor.Left;
                            info.MaxPosition.Y = monitorInfo.Work.Top - monitorInfo.Monitor.Top;
                            info.MaxSize.X = monitorInfo.Work.Right - monitorInfo.Work.Left;
                            info.MaxSize.Y = monitorInfo.Work.Bottom - monitorInfo.Work.Top;
                            Marshal.StructureToPtr(info, lParam, true);
                        }
                    }
                }
                catch
                {
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        public static void SetDark(Window window, bool dark)
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero) return;
                int immersive = dark ? 1 : 0;
                DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref immersive, sizeof(int));
                int border = dark ? 0x002B231F : 0x00ECE3DD;
                DwmSetWindowAttribute(handle, DwmBorderColor, ref border, sizeof(int));
            }
            catch
            {
            }
        }
    }

    public class DarkMenuColorTable : WinForms.ProfessionalColorTable
    {
        private static Drawing.Color Surface
        {
            get { return Ui.IsDark ? Drawing.Color.FromArgb(23, 25, 31) : Drawing.Color.White; }
        }

        private static Drawing.Color Hover
        {
            get { return Ui.IsDark ? Drawing.Color.FromArgb(43, 48, 59) : Drawing.Color.FromArgb(238, 243, 250); }
        }

        private static Drawing.Color Line
        {
            get { return Ui.IsDark ? Drawing.Color.FromArgb(45, 49, 58) : Drawing.Color.FromArgb(226, 232, 240); }
        }

        public override Drawing.Color ToolStripDropDownBackground { get { return Surface; } }
        public override Drawing.Color ImageMarginGradientBegin { get { return Surface; } }
        public override Drawing.Color ImageMarginGradientMiddle { get { return Surface; } }
        public override Drawing.Color ImageMarginGradientEnd { get { return Surface; } }
        public override Drawing.Color MenuBorder { get { return Line; } }
        public override Drawing.Color MenuItemBorder { get { return Hover; } }
        public override Drawing.Color MenuItemSelected { get { return Hover; } }
        public override Drawing.Color MenuItemSelectedGradientBegin { get { return Hover; } }
        public override Drawing.Color MenuItemSelectedGradientEnd { get { return Hover; } }
        public override Drawing.Color SeparatorDark { get { return Line; } }
        public override Drawing.Color SeparatorLight { get { return Surface; } }
        public override Drawing.Color CheckBackground { get { return Surface; } }
        public override Drawing.Color CheckSelectedBackground { get { return Hover; } }
    }

    public class DarkMenuRenderer : WinForms.ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer()
            : base(new DarkMenuColorTable())
        {
        }

        protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected
                ? (Ui.IsDark ? Drawing.Color.White : Drawing.Color.FromArgb(15, 23, 42))
                : (Ui.IsDark ? Drawing.Color.FromArgb(224, 228, 234) : Drawing.Color.FromArgb(31, 41, 55));
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(WinForms.ToolStripItemImageRenderEventArgs e)
        {
            using (Drawing.Font font = new Drawing.Font("Segoe UI", 9f, Drawing.FontStyle.Bold))
            using (Drawing.Brush brush = new Drawing.SolidBrush(Drawing.Color.FromArgb(110, 168, 255)))
            {
                e.Graphics.DrawString("\u2713", font, brush, e.ImageRectangle.X + 2, e.ImageRectangle.Y + 1);
            }
        }
    }

    public static class TrayIconFactory
    {
        public static Drawing.Icon Load()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                Drawing.Icon icon = Drawing.Icon.ExtractAssociatedIcon(exe);
                if (icon != null) return icon;
            }
            catch
            {
            }
            return new Drawing.Icon(Drawing.SystemIcons.Application, 32, 32);
        }
    }
}