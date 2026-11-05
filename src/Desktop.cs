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

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void Apply(Window window)
        {
            window.SourceInitialized += delegate
            {
                try
                {
                    IntPtr handle = new WindowInteropHelper(window).Handle;
                    int round = 2;
                    DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref round, sizeof(int));
                }
                catch
                {
                }
                SetDark(window, Ui.IsDark);
            };
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