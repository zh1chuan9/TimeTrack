using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace TimeTrack
{
    public class DataStore : IDisposable
    {
        public static string RootDir;

        private readonly object _gate = new object();
        private readonly Dictionary<string, DayData> _cache = new Dictionary<string, DayData>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AppMeta> _known = new Dictionary<string, AppMeta>(StringComparer.OrdinalIgnoreCase);
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly string _daysDir;
        private readonly string _settingsPath;
        private Settings _settings;
        private DayData _today;
        private DateTime _lastSave = DateTime.MinValue;
        private bool _dirty;
        private long _version;
        private bool _disposed;

        public DataStore()
        {
            string root = Environment.GetEnvironmentVariable("TIMETRACK_DATA");
            if (string.IsNullOrEmpty(root))
            {
                root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(root)) root = AppDomain.CurrentDomain.BaseDirectory;
                root = Path.Combine(root, "TimeTrack");
            }

            RootDir = root;
            _daysDir = Path.Combine(root, "days");
            _settingsPath = Path.Combine(root, "settings.json");
            Directory.CreateDirectory(_daysDir);
            Logger.FilePath = Path.Combine(root, "log.txt");
            _json.MaxJsonLength = int.MaxValue;
            _json.RecursionLimit = 100;

            LoadSettings();
            LoadKnownApps();
            SyncAutoStartPath();
            Logger.Write("TimeTrack started. Data dir: " + root);
        }

        public long Version
        {
            get { return Interlocked.Read(ref _version); }
        }

        public string DataDirectory
        {
            get { return RootDir; }
        }

        public bool IsPaused
        {
            get { lock (_gate) return _settings.Paused; }
        }

        public int IdleSeconds
        {
            get { lock (_gate) return _settings.IdleSeconds; }
        }

        public bool AutoStart
        {
            get { lock (_gate) return _settings.AutoStart; }
        }

        public bool TrackBackground
        {
            get { lock (_gate) return _settings.TrackBackground; }
        }

        public string GetDisplayName(string key, string fallback)
        {
            if (string.IsNullOrEmpty(key)) return fallback;
            lock (_gate)
            {
                string name;
                if (_settings.CustomNames != null && _settings.CustomNames.TryGetValue(key, out name) &&
                    !string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
            return fallback;
        }

        public void SetCustomName(string key, string name)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_gate)
            {
                if (_settings.CustomNames == null)
                    _settings.CustomNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                string value = name == null ? string.Empty : name.Trim();
                if (value.Length > 40) value = value.Substring(0, 40);

                bool changed = false;
                if (value.Length == 0)
                {
                    changed = _settings.CustomNames.Remove(key);
                }
                else
                {
                    string existing;
                    if (!_settings.CustomNames.TryGetValue(key, out existing) ||
                        !string.Equals(existing, value, StringComparison.Ordinal))
                    {
                        _settings.CustomNames[key] = value;
                        changed = true;
                    }
                }

                if (changed)
                {
                    SaveSettingsLocked();
                    Interlocked.Increment(ref _version);
                }
            }
        }

        private void LoadSettings()
        {
            _settings = null;
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(json))
                        _settings = _json.Deserialize<Settings>(json);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("LoadSettings", ex);
            }

            if (_settings == null) _settings = new Settings();
            if (_settings.ExcludedKeys == null) _settings.ExcludedKeys = new List<string>();
            if (_settings.KnownApps == null) _settings.KnownApps = new List<AppMeta>();
            if (_settings.IdleSeconds < 30) _settings.IdleSeconds = 120;

            Dictionary<string, string> displayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_settings.CustomNames != null)
            {
                foreach (KeyValuePair<string, string> pair in _settings.CustomNames)
                {
                    if (string.IsNullOrEmpty(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)) continue;
                    string value = pair.Value.Trim();
                    if (value.Length > 40) value = value.Substring(0, 40);
                    displayNames[pair.Key] = value;
                }
            }
            _settings.CustomNames = displayNames;

            _excluded.Clear();
            foreach (string key in _settings.ExcludedKeys)
            {
                if (!string.IsNullOrEmpty(key)) _excluded.Add(key);
            }
        }

        private void LoadKnownApps()
        {
            lock (_gate)
            {
                _known.Clear();
                foreach (AppMeta meta in _settings.KnownApps)
                {
                    if (meta == null || string.IsNullOrEmpty(meta.Key)) continue;
                    _known[meta.Key] = meta;
                }

                try
                {
                    bool wanted = _settings.AutoStart;
                    bool enabled = AutoStartManager.IsEnabled();
                    if (wanted && !enabled)
                    {
                        // The Run value can disappear after a Windows cleanup,
                        // antivirus action, or an application move. Restore it
                        // instead of silently switching the user's setting off.
                        AutoStartManager.Enable();
                        enabled = AutoStartManager.IsEnabled();
                        Logger.Write(enabled ? "AutoStart entry restored." : "AutoStart entry could not be restored.");
                    }
                    _settings.AutoStart = enabled;
                }
                catch (Exception ex)
                {
                    Logger.Error("AutoStart check", ex);
                }
            }
        }

        private void SyncAutoStartPath()
        {
            try
            {
                if (_settings.AutoStart) AutoStartManager.Enable();
            }
            catch (Exception ex)
            {
                Logger.Error("AutoStart sync", ex);
            }
        }

        public void AddSecond(DateTime now, AppIdentity app)
        {
            AddSecond(now, app, true);
        }

        public void AddSecond(DateTime now, AppIdentity app, bool foreground)
        {
            if (app == null || string.IsNullOrEmpty(app.Key)) return;

            lock (_gate)
            {
                if (_disposed || _settings.Paused) return;
                if (_excluded.Contains(app.Key)) return;

                DayData day = GetDayLocked(now.Date);
                AppUsage usage;
                if (!day.Apps.TryGetValue(app.Key, out usage))
                {
                    usage = new AppUsage();
                    usage.Key = app.Key;
                    usage.Name = app.Name;
                    usage.Path = app.Path;
                    day.Apps[app.Key] = usage;
                }
                if (!string.IsNullOrEmpty(app.Name)) usage.Name = app.Name;
                if (!string.IsNullOrEmpty(app.Path)) usage.Path = app.Path;

                usage.Hours = NormalizeHours(usage.Hours);
                usage.ForegroundHours = NormalizeHours(usage.ForegroundHours);
                usage.BackgroundHours = NormalizeHours(usage.BackgroundHours);

                int hour = now.Hour;
                if (hour < 0) hour = 0;
                if (hour > 23) hour = 23;
                usage.Hours[hour] = usage.Hours[hour] + 1;
                if (foreground)
                    usage.ForegroundHours[hour] = usage.ForegroundHours[hour] + 1;
                else
                    usage.BackgroundHours[hour] = usage.BackgroundHours[hour] + 1;

                _today = day;
                _dirty = true;
                if (_lastSave == DateTime.MinValue || (now - _lastSave).TotalSeconds >= 10)
                {
                    SaveTodayLocked();
                }
            }
        }

        public void Flush()
        {
            lock (_gate)
            {
                if (_dirty) SaveTodayLocked();
            }
        }

        private DayData GetDayLocked(DateTime date)
        {
            string key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            DayData day;
            if (_cache.TryGetValue(key, out day)) return day;

            string path = Path.Combine(_daysDir, key + ".json");
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        day = _json.Deserialize<DayData>(json);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("LoadDay " + key, ex);
                }
            }

            if (day == null)
            {
                day = new DayData();
                day.Date = key;
            }
            if (day.Apps == null)
                day.Apps = new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase);
            foreach (AppUsage usage in day.Apps.Values)
            {
                usage.Hours = NormalizeHours(usage.Hours);
                usage.ForegroundHours = NormalizeHours(usage.ForegroundHours);
                usage.BackgroundHours = NormalizeHours(usage.BackgroundHours);
            }

            _cache[key] = day;
            return day;
        }

        private static long[] NormalizeHours(long[] values)
        {
            if (values == null) return new long[24];
            if (values.Length == 24) return values;
            long[] fixedHours = new long[24];
            Array.Copy(values, fixedHours, Math.Min(24, values.Length));
            return fixedHours;
        }

        private void SaveTodayLocked()
        {
            if (_today == null) return;
            try
            {
                string json = _json.Serialize(_today);
                string path = Path.Combine(_daysDir, _today.Date + ".json");
                AtomicWrite(path, json);
                _dirty = false;
                _lastSave = DateTime.Now;
                Interlocked.Increment(ref _version);
            }
            catch (Exception ex)
            {
                Logger.Error("SaveToday", ex);
            }
        }

        private static void AtomicWrite(string path, string content)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temp, path, null);
                }
                catch
                {
                    File.Copy(temp, path, true);
                    try { File.Delete(temp); } catch { }
                }
            }
            else
            {
                File.Move(temp, path);
            }
        }

        public RangeData BuildRange(DateTime start, DateTime end, string includeKey)
        {
            return BuildRange(start, end, includeKey, false);
        }

        private RangeData BuildRange(DateTime start, DateTime end, string includeKey, bool includeAll)
        {
            DateTime today = DateTime.Today;
            if (end > today) end = today;
            if (start > end)
            {
                RangeData empty = new RangeData();
                empty.Start = start;
                empty.End = end;
                empty.DayCount = 1;
                return empty;
            }

            RangeData result = new RangeData();
            result.Start = start.Date;
            result.End = end.Date;
            result.DayCount = (end.Date - start.Date).Days + 1;

            Dictionary<string, AppAgg> aggregate = new Dictionary<string, AppAgg>(StringComparer.OrdinalIgnoreCase);

            lock (_gate)
            {
                for (DateTime date = start.Date; date <= end.Date; date = date.AddDays(1))
                {
                    DayData day = GetDayLocked(date);
                    long dayTotal = 0;

                    foreach (AppUsage usage in day.Apps.Values)
                    {
                        if (usage == null || string.IsNullOrEmpty(usage.Key)) continue;
                        bool excluded = _excluded.Contains(usage.Key);
                        if (excluded && !includeAll &&
                            !string.Equals(usage.Key, includeKey, StringComparison.OrdinalIgnoreCase)) continue;

                        long appTotal = usage.Total;
                        if (appTotal <= 0) continue;

                        dayTotal += appTotal;

                        AppAgg agg;
                        if (!aggregate.TryGetValue(usage.Key, out agg))
                        {
                            agg = new AppAgg();
                            agg.Key = usage.Key;
                            agg.Name = usage.Name;
                            agg.Path = usage.Path;
                            aggregate[usage.Key] = agg;
                        }
                        agg.Seconds += appTotal;
                        long foreground = usage.ForegroundTotal;
                        long background = usage.BackgroundTotal;
                        long unclassified = appTotal - foreground - background;
                        if (unclassified < 0) unclassified = 0;
                        agg.ForegroundSeconds += foreground;
                        agg.BackgroundSeconds += background;
                        agg.UnclassifiedSeconds += unclassified;
                        result.ForegroundTotal += foreground;
                        result.BackgroundTotal += background;
                        result.UnclassifiedTotal += unclassified;
                        if (!string.IsNullOrEmpty(usage.Name)) agg.Name = usage.Name;
                        if (!string.IsNullOrEmpty(usage.Path)) agg.Path = usage.Path;

                        for (int h = 0; h < 24; h++)
                            result.HourBins[h] += usage.Hours[h];
                    }

                    if (Equals(date, today) && dayTotal == 0 && !includeAll)
                    {
                        // still keep the empty day for chart alignment
                    }

                    DayBar bar = new DayBar();
                    bar.Date = date;
                    bar.Seconds = dayTotal;
                    result.Days.Add(bar);
                    result.Total += dayTotal;
                }

                if (_settings.CustomNames != null)
                {
                    foreach (AppAgg agg in aggregate.Values)
                    {
                        string custom;
                        if (_settings.CustomNames.TryGetValue(agg.Key, out custom) && !string.IsNullOrWhiteSpace(custom))
                            agg.Name = custom;
                    }
                }
            }

            result.Apps.AddRange(aggregate.Values);
            result.Apps.Sort(delegate(AppAgg a, AppAgg b) { return b.Seconds.CompareTo(a.Seconds); });
            result.Average = result.Total / Math.Max(1, result.DayCount);
            return result;
        }

        public long TotalForKey(string key, DateTime start, DateTime end)
        {
            if (string.IsNullOrEmpty(key)) return 0;
            DateTime today = DateTime.Today;
            if (end > today) end = today;
            if (start > end) return 0;

            long total = 0;
            lock (_gate)
            {
                for (DateTime date = start.Date; date <= end.Date; date = date.AddDays(1))
                {
                    DayData day = GetDayLocked(date);
                    AppUsage usage;
                    if (day.Apps.TryGetValue(key, out usage) && usage != null) total += usage.Total;
                }
            }
            return total;
        }

        public UsageBreakdown GetBreakdown(string key, DateTime start, DateTime end)
        {
            UsageBreakdown result = new UsageBreakdown();
            if (string.IsNullOrEmpty(key)) return result;
            DateTime today = DateTime.Today;
            if (end > today) end = today;
            if (start > end) return result;

            lock (_gate)
            {
                for (DateTime date = start.Date; date <= end.Date; date = date.AddDays(1))
                {
                    DayData day = GetDayLocked(date);
                    AppUsage usage;
                    if (!day.Apps.TryGetValue(key, out usage) || usage == null) continue;
                    result.Total += usage.Total;
                    result.Foreground += usage.ForegroundTotal;
                    result.Background += usage.BackgroundTotal;
                }
            }
            result.Unclassified = result.Total - result.Foreground - result.Background;
            if (result.Unclassified < 0) result.Unclassified = 0;
            return result;
        }

        public Dictionary<string, long> TotalsIncludingExcluded(DateTime start, DateTime end)
        {
            Dictionary<string, long> totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            DateTime today = DateTime.Today;
            if (end > today) end = today;
            if (start > end) return totals;

            lock (_gate)
            {
                for (DateTime date = start.Date; date <= end.Date; date = date.AddDays(1))
                {
                    DayData day = GetDayLocked(date);
                    foreach (AppUsage usage in day.Apps.Values)
                    {
                        if (usage == null || string.IsNullOrEmpty(usage.Key)) continue;
                        long current;
                        totals.TryGetValue(usage.Key, out current);
                        totals[usage.Key] = current + usage.Total;
                    }
                }
            }
            return totals;
        }

        public void RememberApp(string key, string name, string path)
        {
            if (string.IsNullOrEmpty(key)) return;
            bool changed = false;
            lock (_gate)
            {
                AppMeta meta;
                if (!_known.TryGetValue(key, out meta))
                {
                    meta = new AppMeta();
                    meta.Key = key;
                    meta.Name = name;
                    meta.Path = path;
                    _known[key] = meta;
                    _settings.KnownApps.Add(meta);
                    changed = true;
                }
                else
                {
                    if (!string.IsNullOrEmpty(name) && !string.Equals(meta.Name, name, StringComparison.Ordinal))
                    {
                        meta.Name = name;
                        changed = true;
                    }
                    if (!string.IsNullOrEmpty(path) && !string.Equals(meta.Path, path, StringComparison.OrdinalIgnoreCase))
                    {
                        meta.Path = path;
                        changed = true;
                    }
                }
                if (changed) SaveSettingsLocked();
            }
        }

        public List<AppMeta> GetKnownApps()
        {
            List<AppMeta> list = new List<AppMeta>();
            lock (_gate)
            {
                foreach (AppMeta meta in _known.Values) list.Add(meta);
            }
            return list;
        }

        public AppMeta GetAppMeta(string key)
        {
            lock (_gate)
            {
                AppMeta meta;
                if (_known.TryGetValue(key, out meta)) return meta;
                foreach (DayData day in _cache.Values)
                {
                    AppUsage usage;
                    if (day.Apps.TryGetValue(key, out usage))
                    {
                        AppMeta result = new AppMeta();
                        result.Key = key;
                        result.Name = usage.Name;
                        result.Path = usage.Path;
                        return result;
                    }
                }
            }
            return null;
        }

        public bool IsExcluded(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            lock (_gate) return _excluded.Contains(key);
        }

        public void SetExcluded(string key, bool excluded)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_gate)
            {
                bool changed;
                if (excluded)
                {
                    changed = _excluded.Add(key);
                    if (changed && !_settings.ExcludedKeys.Contains(key)) _settings.ExcludedKeys.Add(key);
                }
                else
                {
                    changed = _excluded.Remove(key);
                    if (changed) _settings.ExcludedKeys.RemoveAll(delegate(string k) { return string.Equals(k, key, StringComparison.OrdinalIgnoreCase); });
                }
                if (changed)
                {
                    SaveSettingsLocked();
                    Interlocked.Increment(ref _version);
                }
            }
        }

        public bool SetPaused(bool paused)
        {
            lock (_gate)
            {
                if (_settings.Paused == paused) return true;
                _settings.Paused = paused;
                SaveSettingsLocked();
                Interlocked.Increment(ref _version);
                return true;
            }
        }

        public bool SetAutoStart(bool enabled)
        {
            try
            {
                if (enabled) AutoStartManager.Enable();
                else AutoStartManager.Disable();
            }
            catch (Exception ex)
            {
                Logger.Error("SetAutoStart", ex);
                return false;
            }
            lock (_gate)
            {
                _settings.AutoStart = enabled;
                SaveSettingsLocked();
            }
            return true;
        }

        public void SetIdleSeconds(int seconds)
        {
            if (seconds < 30) seconds = 30;
            lock (_gate)
            {
                if (_settings.IdleSeconds == seconds) return;
                _settings.IdleSeconds = seconds;
                SaveSettingsLocked();
            }
        }

        public void SetTrackBackground(bool enabled)
        {
            lock (_gate)
            {
                if (_settings.TrackBackground == enabled) return;
                _settings.TrackBackground = enabled;
                SaveSettingsLocked();
                Interlocked.Increment(ref _version);
            }
        }

        private void SaveSettingsLocked()
        {
            try
            {
                string json = _json.Serialize(_settings);
                AtomicWrite(_settingsPath, json);
            }
            catch (Exception ex)
            {
                Logger.Error("SaveSettings", ex);
            }
        }

        public void ClearAllData()
        {
            lock (_gate)
            {
                _today = null;
                _dirty = false;
                _cache.Clear();
                try
                {
                    if (Directory.Exists(_daysDir))
                    {
                        foreach (string file in Directory.GetFiles(_daysDir, "*.json"))
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("ClearAllData", ex);
                }
                Interlocked.Increment(ref _version);
            }
        }

        public static void OpenDataFolder()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = "explorer.exe";
                info.Arguments = "\"" + RootDir + "\"";
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception ex)
            {
                Logger.Error("OpenDataFolder", ex);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                try
                {
                    if (_dirty) SaveTodayLocked();
                }
                catch { }
                _disposed = true;
            }
        }
    }

    public static class AutoStartManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "TimeTrack";

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                if (key == null) return false;
                object value = key.GetValue(ValueName);
                return value != null && !string.IsNullOrEmpty(value.ToString());
            }
        }

        public static void Enable()
        {
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key == null) throw new InvalidOperationException("无法打开开机启动注册表项");
                key.SetValue(ValueName, "\"" + exe + "\" --tray");
            }
        }

        public static void Disable()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key != null) key.DeleteValue(ValueName, false);
            }
        }
    }
}