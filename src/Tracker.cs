using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TimeTrack
{
    public class Tracker : IDisposable
    {
        private readonly DataStore _store;
        private readonly Thread _thread;
        private readonly int _selfPid;
        private readonly string _selfPath;
        private volatile bool _running;
        private volatile string _statusText = "等待活动";
        private volatile int _statusKind;
        private readonly Dictionary<int, CacheEntry> _cache = new Dictionary<int, CacheEntry>();
        private DateTime _lastBackgroundScan = DateTime.MinValue;
        private List<AppIdentity> _backgroundApps = new List<AppIdentity>();

        private class CacheEntry
        {
            public string Title;
            public DateTime Stamp;
            public AppIdentity Identity;
        }

        public Tracker(DataStore store)
        {
            _store = store;
            _selfPid = Process.GetCurrentProcess().Id;
            try
            {
                _selfPath = Process.GetCurrentProcess().MainModule.FileName;
            }
            catch
            {
                _selfPath = null;
            }

            _running = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "TimeTrack sampler";
            _thread.Start();
        }

        public string StatusText
        {
            get { return _statusText; }
        }

        public int StatusKind
        {
            get { return _statusKind; }
        }

        private void SetStatus(string text, int kind)
        {
            _statusText = text;
            _statusKind = kind;
        }

        private void Loop()
        {
            while (_running)
            {
                DateTime started = DateTime.UtcNow;
                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    Logger.Error("Tracker tick", ex);
                }

                int spent = (int)(DateTime.UtcNow - started).TotalMilliseconds;
                int wait = 1000 - spent;
                if (wait < 200) wait = 200;
                Thread.Sleep(wait);
            }
        }

        private void Tick()
        {
            if (_store.IsPaused)
            {
                SetStatus("已暂停", 2);
                return;
            }

            uint idle = GetIdleMilliseconds();
            if (idle >= (uint)(_store.IdleSeconds * 1000))
            {
                SetStatus("等待活动", 0);
                return;
            }

            // One tick equals one second of actual computer use, regardless of
            // how many apps are visible at the same time.
            _store.AddActiveSecond(DateTime.Now);

            AppIdentity foreground = null;
            bool selfForeground = false;
            bool excludedForeground = false;
            string excludedName = null;

            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hwnd))
            {
                uint pidValue;
                NativeMethods.GetWindowThreadProcessId(hwnd, out pidValue);
                int pid = unchecked((int)pidValue);
                if (pid != 0)
                {
                    string title = NativeMethods.GetWindowTitle(hwnd);
                    AppIdentity app = ResolveCached(pid, title);
                    if (app != null)
                    {
                        if (pid == _selfPid || (!string.IsNullOrEmpty(_selfPath) && string.Equals(app.Path, _selfPath, StringComparison.OrdinalIgnoreCase)))
                        {
                            selfForeground = true;
                        }
                        else if (_store.IsExcluded(app.Key))
                        {
                            excludedForeground = true;
                            excludedName = app.Name;
                        }
                        else
                        {
                            foreground = app;
                        }
                    }
                }
            }

            if (!_store.TrackBackground)
            {
                if (foreground != null)
                {
                    Record(foreground, true);
                    SetStatus("正在记录 · " + _store.GetDisplayName(foreground.Key, foreground.Name), 1);
                }
                else if (selfForeground)
                {
                    SetStatus("正在查看统计", 4);
                }
                else if (excludedForeground)
                {
                    SetStatus("已忽略 · " + excludedName, 3);
                }
                else
                {
                    SetStatus("等待活动", 0);
                }
                return;
            }

            Dictionary<string, AppIdentity> apps = new Dictionary<string, AppIdentity>(StringComparer.OrdinalIgnoreCase);
            if (foreground != null) apps[foreground.Key] = foreground;

            foreach (AppIdentity background in GetBackgroundApps())
            {
                if (!apps.ContainsKey(background.Key)) apps[background.Key] = background;
            }

            if (apps.Count == 0)
            {
                if (excludedForeground) SetStatus("已忽略 · " + excludedName, 3);
                else if (selfForeground) SetStatus("正在查看统计", 4);
                else SetStatus("等待活动", 0);
                return;
            }

            foreach (AppIdentity app in apps.Values)
            {
                bool isForeground = foreground != null &&
                    string.Equals(app.Key, foreground.Key, StringComparison.OrdinalIgnoreCase);
                Record(app, isForeground);
            }

            if (foreground != null) SetStatus("正在记录 · " + _store.GetDisplayName(foreground.Key, foreground.Name), 1);
            else if (selfForeground) SetStatus("正在查看统计", 4);
            else SetStatus("后台记录 · " + apps.Count + " 个应用", 1);
        }

        private void Record(AppIdentity app, bool foreground)
        {
            _store.RememberApp(app.Key, app.Name, app.Path);
            _store.AddSecond(DateTime.Now, app, foreground);
        }

        private List<AppIdentity> GetBackgroundApps()
        {
            DateTime now = DateTime.UtcNow;
            double interval = _store.Lightweight ? 5.0 : 2.0;
            if (_lastBackgroundScan != DateTime.MinValue &&
                (now - _lastBackgroundScan).TotalSeconds < interval)
            {
                return _backgroundApps;
            }
            _lastBackgroundScan = now;

            List<AppIdentity> result = new List<AppIdentity>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch
            {
                return _backgroundApps;
            }

            foreach (Process process in processes)
            {
                try
                {
                    int pid = process.Id;
                    if (pid == _selfPid) continue;

                    IntPtr hwnd = process.MainWindowHandle;
                    if (hwnd == IntPtr.Zero || !NativeMethods.IsWindowVisible(hwnd)) continue;

                    string title = NativeMethods.GetWindowTitle(hwnd);
                    AppIdentity app = ResolveCached(pid, title);
                    if (app == null) continue;
                    if (!string.IsNullOrEmpty(_selfPath) && string.Equals(app.Path, _selfPath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (_store.IsExcluded(app.Key)) continue;
                    if (seen.Add(app.Key)) result.Add(app);
                }
                catch
                {
                }
                finally
                {
                    try { process.Dispose(); } catch { }
                }
            }

            _backgroundApps = result;
            return result;
        }

        private AppIdentity ResolveCached(int pid, string title)
        {
            CacheEntry entry;
            if (_cache.TryGetValue(pid, out entry))
            {
                if ((DateTime.UtcNow - entry.Stamp).TotalSeconds < 120 && string.Equals(entry.Title, title, StringComparison.Ordinal))
                    return entry.Identity;
            }

            AppIdentity identity = AppResolver.Resolve(pid, title);
            if (identity != null)
            {
                CacheEntry fresh = new CacheEntry();
                fresh.Title = title;
                fresh.Stamp = DateTime.UtcNow;
                fresh.Identity = identity;
                _cache[pid] = fresh;
            }
            else
            {
                _cache.Remove(pid);
            }

            if (_cache.Count > 200) _cache.Clear();
            return identity;
        }

        private static uint GetIdleMilliseconds()
        {
            NativeMethods.LASTINPUTINFO info = new NativeMethods.LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.LASTINPUTINFO));
            if (!NativeMethods.GetLastInputInfo(ref info)) return 0;
            return unchecked((uint)Environment.TickCount - info.dwTime);
        }

        public void Dispose()
        {
            _running = false;
            try
            {
                if (_thread.IsAlive) _thread.Join(1500);
            }
            catch { }
            try { _store.Flush(); }
            catch { }
        }
    }

    public static class AppResolver
    {
        private static readonly Dictionary<string, string> Names = BuildNames();

        private static readonly HashSet<string> Ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "textinputhost", "shellexperiencehost", "startmenuexperiencehost", "searchhost",
            "searchapp", "lockapp", "logonui", "dwm", "csrss", "winlogon", "sihost",
            "taskhostw", "runtimebroker", "backgroundtaskhost", "smartscreen", "ctfmon",
            "securityhealthsystray"
        };

        private static Dictionary<string, string> BuildNames()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            map["wechat"] = "微信";
            map["weixin"] = "微信";
            map["wechatappex"] = "微信";
            map["qq"] = "QQ";
            map["tim"] = "TIM";
            map["msedge"] = "Microsoft Edge";
            map["chrome"] = "Google Chrome";
            map["firefox"] = "Firefox";
            map["brave"] = "Brave";
            map["opera"] = "Opera";
            map["douyin"] = "抖音";
            map["tiktok"] = "抖音";
            map["bilibili"] = "哔哩哔哩";
            map["cloudmusic"] = "网易云音乐";
            map["qqmusic"] = "QQ音乐";
            map["spotify"] = "Spotify";
            map["steam"] = "Steam";
            map["steamwebhelper"] = "Steam";
            map["code"] = "Visual Studio Code";
            map["devenv"] = "Visual Studio";
            map["explorer"] = "文件资源管理器";
            map["windowsterminal"] = "Windows 终端";
            map["powershell"] = "PowerShell";
            map["pwsh"] = "PowerShell";
            map["cmd"] = "命令提示符";
            map["notepad"] = "记事本";
            map["winword"] = "Microsoft Word";
            map["excel"] = "Microsoft Excel";
            map["powerpnt"] = "Microsoft PowerPoint";
            map["wps"] = "WPS Office";
            map["wpp"] = "WPS 演示";
            map["et"] = "WPS 表格";
            map["acrobat"] = "Adobe Acrobat";
            map["photoshop"] = "Adobe Photoshop";
            map["idea64"] = "IntelliJ IDEA";
            map["pycharm64"] = "PyCharm";
            map["clion64"] = "CLion";
            map["webstorm64"] = "WebStorm";
            map["androidstudio64"] = "Android Studio";
            map["java"] = "Java";
            map["javaw"] = "Java";
            map["minecraft"] = "Minecraft";
            map["yuanshen"] = "原神";
            map["starrail"] = "崩坏：星穹铁道";
            map["zenlesszonezero"] = "绝区零";
            map["taskmgr"] = "任务管理器";
            map["systemsettings"] = "设置";
            map["calculatorapp"] = "计算器";
            map["calculator"] = "计算器";
            map["mspaint"] = "画图";
            map["snippingtool"] = "截图工具";
            map["telegram"] = "Telegram";
            map["discord"] = "Discord";
            map["dingtalk"] = "钉钉";
            map["feishu"] = "飞书";
            map["lark"] = "飞书";
            map["wemeetapp"] = "腾讯会议";
            map["baidunetdisk"] = "百度网盘";
            map["aliyundrive"] = "阿里云盘";
            map["thunder"] = "迅雷";
            map["potplayermini64"] = "PotPlayer";
            map["vlc"] = "VLC";
            map["obs64"] = "OBS Studio";
            map["idea"] = "IntelliJ IDEA";
            return map;
        }

        public static AppIdentity Resolve(int pid, string windowTitle)
        {
            try
            {
                Process process = Process.GetProcessById(pid);
                string processName = process.ProcessName;
                if (string.IsNullOrEmpty(processName)) return null;
                if (Ignored.Contains(processName)) return null;

                string path = null;
                string description = null;
                try
                {
                    path = process.MainModule.FileName;
                    description = process.MainModule.FileVersionInfo.FileDescription;
                }
                catch
                {
                }

                string display;
                string key;

                if (string.Equals(processName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(windowTitle)) return null;
                    display = CleanUwpTitle(windowTitle);
                    if (string.IsNullOrEmpty(display)) return null;
                    key = "uwp:" + display.ToLowerInvariant();
                    path = null;
                }
                else
                {
                    if (!Names.TryGetValue(processName, out display))
                    {
                        if (!string.IsNullOrWhiteSpace(description) && description.Length <= 70)
                            display = description.Trim();
                        else
                            display = processName;
                    }

                    if (string.IsNullOrEmpty(path))
                        key = "proc:" + processName.ToLowerInvariant();
                    else
                        key = "path:" + path.ToLowerInvariant();
                }

                AppIdentity identity = new AppIdentity();
                identity.ProcessId = pid;
                identity.Key = key;
                identity.Name = CleanName(display);
                identity.Path = path;
                return identity;
            }
            catch
            {
                return null;
            }
        }

        private static string CleanUwpTitle(string title)
        {
            string value = title.Trim();
            int index = value.IndexOf(" - ", StringComparison.Ordinal);
            if (index > 0) value = value.Substring(0, index).Trim();
            return value;
        }

        private static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "未知应用";
            string value = name.Trim();
            if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(0, value.Length - 4);
            if (value.Length > 48) value = value.Substring(0, 48);
            return value;
        }
    }

    internal static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        public static string GetWindowTitle(IntPtr hwnd)
        {
            try
            {
                int length = GetWindowTextLength(hwnd);
                if (length <= 0) return string.Empty;
                StringBuilder builder = new StringBuilder(length + 1);
                GetWindowText(hwnd, builder, builder.Capacity);
                return builder.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}