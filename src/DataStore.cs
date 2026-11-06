using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
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
        private readonly string _backupsDir;
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
            _backupsDir = Path.Combine(root, "backups");
            Directory.CreateDirectory(_daysDir);
            Logger.FilePath = Path.Combine(root, "log.txt");
            _json.MaxJsonLength = int.MaxValue;
            _json.RecursionLimit = 100;

            LoadSettings();
            LoadKnownApps();
            SyncAutoStartPath();
            Logger.Write("TimeTrack started. Data dir: " + root);
            try
            {
                MaybeAutoBackup();
                RunAutoCleanup(false);
            }
            catch (Exception ex)
            {
                Logger.Error("Startup maintenance", ex);
            }
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

        public bool Lightweight
        {
            get { lock (_gate) return _settings.Lightweight; }
        }

        public bool DarkMode
        {
            get { lock (_gate) return _settings.DarkMode; }
        }

        public bool AutoBackup
        {
            get { lock (_gate) return _settings.AutoBackup; }
        }

        public bool AutoCleanup
        {
            get { lock (_gate) return _settings.AutoCleanup; }
        }

        public int RetentionDays
        {
            get { lock (_gate) return _settings.RetentionDays; }
        }

        public int CleanupIntervalDays
        {
            get { lock (_gate) return _settings.CleanupIntervalDays; }
        }

        public string BackupDirectory
        {
            get { return _backupsDir; }
        }

        public string LogFilePath
        {
            get { return Logger.FilePath; }
        }

        public string GetCategory(string key)
        {
            if (string.IsNullOrEmpty(key)) return CategoryCatalog.Normalize(null);
            lock (_gate)
            {
                string category;
                if (_settings.Categories != null && _settings.Categories.TryGetValue(key, out category))
                    return CategoryCatalog.Normalize(category);
            }
            return CategoryCatalog.Normalize(null);
        }

        public void SetCategory(string key, string category)
        {
            if (string.IsNullOrEmpty(key)) return;
            string value = CategoryCatalog.Normalize(category);
            lock (_gate)
            {
                if (_settings.Categories == null)
                    _settings.Categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                string existing;
                if (_settings.Categories.TryGetValue(key, out existing) &&
                    string.Equals(existing, value, StringComparison.Ordinal)) return;
                _settings.Categories[key] = value;
                SaveSettingsLocked();
                Interlocked.Increment(ref _version);
            }
        }

        public List<string> GetCategories()
        {
            List<string> result = new List<string>(CategoryCatalog.Names);
            lock (_gate)
            {
                if (_settings.CategoryColors != null)
                {
                    foreach (string name in _settings.CategoryColors.Keys)
                    {
                        if (string.IsNullOrEmpty(name) || CategoryCatalog.IsBuiltIn(name)) continue;
                        if (!result.Contains(name)) result.Add(name);
                    }
                }
                if (_settings.Categories != null)
                {
                    foreach (string name in _settings.Categories.Values)
                    {
                        string category = CategoryCatalog.Normalize(name);
                        if (string.IsNullOrEmpty(category) || CategoryCatalog.IsBuiltIn(category)) continue;
                        if (!result.Contains(category)) result.Add(category);
                    }
                }
            }
            return result;
        }

        public string GetCategoryColor(string category)
        {
            string name = CategoryCatalog.Normalize(category);
            lock (_gate)
            {
                string color;
                if (_settings.CategoryColors != null && _settings.CategoryColors.TryGetValue(name, out color))
                {
                    string normalized = CategoryCatalog.NormalizeColor(color);
                    if (normalized != null) return normalized;
                }
            }
            return CategoryCatalog.DefaultColor(name);
        }

        public List<Dictionary<string, long>> BuildCategoryBuckets(DateTime start, DateTime end, bool hourly)
        {
            List<Dictionary<string, long>> buckets = new List<Dictionary<string, long>>();
            if (end.Date < start.Date) return buckets;
            if (hourly)
            {
                for (int i = 0; i < 24; i++) buckets.Add(new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase));
            }
            else
            {
                int days = (end.Date - start.Date).Days + 1;
                for (int i = 0; i < days; i++) buckets.Add(new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase));
            }

            lock (_gate)
            {
                for (DateTime date = start.Date; date <= end.Date; date = date.AddDays(1))
                {
                    DayData day = GetDayLocked(date);
                    int dayIndex = (date - start.Date).Days;
                    foreach (AppUsage usage in day.Apps.Values)
                    {
                        if (usage == null || string.IsNullOrEmpty(usage.Key)) continue;
                        if (_excluded.Contains(usage.Key)) continue;
                        string category = ResolveCategoryLocked(usage.Key);
                        long[] hours = NormalizeHours(usage.Hours);
                        for (int h = 0; h < 24; h++)
                        {
                            long seconds = hours[h];
                            if (seconds <= 0) continue;
                            int index = hourly ? h : dayIndex;
                            if (index < 0 || index >= buckets.Count) continue;
                            Dictionary<string, long> bucket = buckets[index];
                            long existing;
                            bucket.TryGetValue(category, out existing);
                            bucket[category] = existing + seconds;
                        }
                    }
                }
            }
            return buckets;
        }

        private string ResolveCategoryLocked(string key)
        {
            string category;
            if (_settings.Categories != null && _settings.Categories.TryGetValue(key, out category))
                return CategoryCatalog.Normalize(category);
            return CategoryCatalog.Normalize(null);
        }

        public bool AddCategory(string name, string color)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            name = name.Trim();
            if (name.Length > 12) return false;
            color = CategoryCatalog.NormalizeColor(color);
            if (color == null) color = "#64748B";
            lock (_gate)
            {
                if (_settings.CategoryColors == null)
                    _settings.CategoryColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _settings.CategoryColors[name] = color;
                SaveSettingsLocked();
                Interlocked.Increment(ref _version);
            }
            return true;
        }

        public void SetAutoBackup(bool enabled)
        {
            lock (_gate)
            {
                if (_settings.AutoBackup == enabled) return;
                _settings.AutoBackup = enabled;
                SaveSettingsLocked();
                Interlocked.Increment(ref _version);
            }
        }

        public void SetAutoCleanup(bool enabled)
        {
            lock (_gate)
            {
                if (_settings.AutoCleanup == enabled) return;
                _settings.AutoCleanup = enabled;
                SaveSettingsLocked();
                Interlocked.Increment(ref _version);
            }
        }

        public void SetRetentionDays(int days)
        {
            if (days < 7) days = 7;
            if (days > 3650) days = 3650;
            lock (_gate)
            {
                if (_settings.RetentionDays == days) return;
                _settings.RetentionDays = days;
                SaveSettingsLocked();
                Interlocked.Increment(ref _version);
            }
        }

        public void SetCleanupIntervalDays(int days)
        {
            if (days < 1) days = 1;
            if (days > 365) days = 365;
            lock (_gate)
            {
                if (_settings.CleanupIntervalDays == days) return;
                _settings.CleanupIntervalDays = days;
                SaveSettingsLocked();
                Interlocked.Increment(ref _version);
            }
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

            Dictionary<string, string> categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_settings.Categories != null)
            {
                foreach (KeyValuePair<string, string> pair in _settings.Categories)
                {
                    if (string.IsNullOrEmpty(pair.Key)) continue;
                    categories[pair.Key] = CategoryCatalog.Normalize(pair.Value);
                }
            }
            _settings.Categories = categories;

            Dictionary<string, string> categoryColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_settings.CategoryColors != null)
            {
                foreach (KeyValuePair<string, string> pair in _settings.CategoryColors)
                {
                    string name = CategoryCatalog.Normalize(pair.Key);
                    string color = CategoryCatalog.NormalizeColor(pair.Value);
                    if (string.IsNullOrEmpty(name) || color == null) continue;
                    categoryColors[name] = color;
                }
            }
            _settings.CategoryColors = categoryColors;

            if (_settings.RetentionDays < 7) _settings.RetentionDays = 90;
            if (_settings.LastCleanupUnixSeconds < 0) _settings.LastCleanupUnixSeconds = 0;
            if (_settings.CleanupIntervalDays < 1) _settings.CleanupIntervalDays = 30;

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

        /// <summary>
        /// Counts one wall-clock second of computer activity. Call this once per
        /// sampler tick, before recording individual apps, so concurrent apps do
        /// not inflate the headline "computer usage" total.
        /// </summary>
        public void AddActiveSecond(DateTime now)
        {
            lock (_gate)
            {
                if (_disposed || _settings.Paused) return;

                DayData day = GetDayLocked(now.Date);
                day.ActiveHours = NormalizeHours(day.ActiveHours);
                if (day.ActiveSeconds < 0) day.ActiveSeconds = 0;

                int hour = now.Hour;
                if (hour < 0) hour = 0;
                if (hour > 23) hour = 23;
                day.ActiveSeconds = day.ActiveSeconds + 1;
                day.ActiveHours[hour] = day.ActiveHours[hour] + 1;

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
            EnsureActiveSeconds(day);

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

        private static void EnsureActiveSeconds(DayData day)
        {
            day.ActiveHours = NormalizeHours(day.ActiveHours);

            long hourSum = 0;
            for (int h = 0; h < 24; h++) hourSum += day.ActiveHours[h];

            if (day.ActiveSeconds <= 0)
            {
                if (hourSum > 0)
                {
                    day.ActiveSeconds = hourSum;
                    return;
                }

                // Days recorded before ActiveSeconds existed only have per-app
                // totals. Cap each hour at 3600s so simultaneous apps cannot
                // produce a day longer than the wall clock.
                if (day.Apps != null && day.Apps.Count > 0)
                {
                    long estimate = 0;
                    for (int h = 0; h < 24; h++)
                    {
                        long sum = 0;
                        foreach (AppUsage usage in day.Apps.Values)
                        {
                            if (usage != null && usage.Hours != null && h < usage.Hours.Length)
                                sum += usage.Hours[h];
                        }
                        if (sum > 3600) sum = 3600;
                        if (sum < 0) sum = 0;
                        day.ActiveHours[h] = sum;
                        estimate += sum;
                    }
                    day.ActiveSeconds = estimate;
                }
            }
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

                    foreach (AppUsage usage in day.Apps.Values)
                    {
                        if (usage == null || string.IsNullOrEmpty(usage.Key)) continue;
                        bool excluded = _excluded.Contains(usage.Key);
                        if (excluded && !includeAll &&
                            !string.Equals(usage.Key, includeKey, StringComparison.OrdinalIgnoreCase)) continue;

                        long appTotal = usage.Total;
                        if (appTotal <= 0) continue;

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
                    }

                    long active = day.ActiveSeconds;
                    if (active < 0) active = 0;
                    for (int h = 0; h < 24; h++)
                        result.HourBins[h] += day.ActiveHours[h];

                    DayBar bar = new DayBar();
                    bar.Date = date;
                    bar.Seconds = active;
                    result.Days.Add(bar);
                    result.Total += active;
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

        public void SetLightweight(bool enabled)
        {
            lock (_gate)
            {
                if (_settings.Lightweight == enabled) return;
                _settings.Lightweight = enabled;
                SaveSettingsLocked();
                Interlocked.Increment(ref _version);
            }
        }

        public void SetDarkMode(bool enabled)
        {
            lock (_gate)
            {
                if (_settings.DarkMode == enabled) return;
                _settings.DarkMode = enabled;
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

        public int RunAutoCleanup(bool force)
        {
            lock (_gate)
            {
                if (_disposed || !_settings.AutoCleanup) return 0;
                DateTime now = DateTime.UtcNow;
                if (!force && _settings.LastCleanupUnixSeconds > 0 &&
                    (now - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(_settings.LastCleanupUnixSeconds)).TotalDays < _settings.CleanupIntervalDays)
                    return 0;

                int removed = 0;
                DateTime cutoff = DateTime.Today.AddDays(-_settings.RetentionDays);
                try
                {
                    if (Directory.Exists(_daysDir))
                    {
                        foreach (string file in Directory.GetFiles(_daysDir, "*.json"))
                        {
                            DateTime date;
                            if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd",
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) continue;
                            if (date >= cutoff) continue;
                            try
                            {
                                File.Delete(file);
                                _cache.Remove(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                                removed++;
                            }
                            catch (Exception ex)
                            {
                                Logger.Error("Cleanup " + file, ex);
                            }
                        }
                    }
                    _settings.LastCleanupUnixSeconds = (long)(now - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                    SaveSettingsLocked();
                    Interlocked.Increment(ref _version);
                    Logger.Write("Auto cleanup removed " + removed + " day file(s), retention " + _settings.RetentionDays + " days.");
                }
                catch (Exception ex)
                {
                    Logger.Error("Auto cleanup", ex);
                }
                return removed;
            }
        }

        public int CleanupNow()
        {
            return RunAutoCleanup(true);
        }

        public string MaybeAutoBackup()
        {
            bool enabled;
            lock (_gate)
            {
                if (_disposed) return null;
                enabled = _settings.AutoBackup;
            }
            if (!enabled) return null;

            try
            {
                string latest = LatestBackup();
                if (latest != null && (DateTime.Now - File.GetLastWriteTime(latest)).TotalHours < 23) return null;
                return CreateBackup();
            }
            catch (Exception ex)
            {
                Logger.Error("Auto backup", ex);
                return null;
            }
        }

        public string CreateBackup()
        {
            Flush();
            lock (_gate)
            {
                Directory.CreateDirectory(_backupsDir);
                string name = "TimeTrack-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".zip";
                string path = Path.Combine(_backupsDir, name);
                using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    AddBackupEntry(archive, "settings.json", _settingsPath);
                    if (Directory.Exists(_daysDir))
                    {
                        foreach (string file in Directory.GetFiles(_daysDir, "*.json"))
                            AddBackupEntry(archive, "days/" + Path.GetFileName(file), file);
                    }
                }
                TrimBackupsLocked();
                Logger.Write("Backup created: " + path);
                return path;
            }
        }

        private static void AddBackupEntry(ZipArchive archive, string entryName, string path)
        {
            if (!File.Exists(path)) return;
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using (Stream output = entry.Open())
            using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                input.CopyTo(output);
            }
        }

        private string LatestBackup()
        {
            try
            {
                if (!Directory.Exists(_backupsDir)) return null;
                string latest = null;
                DateTime latestTime = DateTime.MinValue;
                foreach (string file in Directory.GetFiles(_backupsDir, "TimeTrack-*.zip"))
                {
                    DateTime time = File.GetLastWriteTime(file);
                    if (time > latestTime)
                    {
                        latestTime = time;
                        latest = file;
                    }
                }
                return latest;
            }
            catch
            {
                return null;
            }
        }

        private void TrimBackupsLocked()
        {
            try
            {
                if (!Directory.Exists(_backupsDir)) return;
                string[] files = Directory.GetFiles(_backupsDir, "TimeTrack-*.zip");
                if (files.Length <= 7) return;
                Array.Sort(files, delegate(string a, string b)
                {
                    return File.GetLastWriteTime(a).CompareTo(File.GetLastWriteTime(b));
                });
                for (int i = 0; i < files.Length - 7; i++)
                {
                    try { File.Delete(files[i]); } catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("TrimBackups", ex);
            }
        }

        public string ExportCsv(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("导出路径不能为空", "path");
            List<string> lines = new List<string>();
            lines.Add("日期,应用,总秒数,总时长,前台秒数,后台秒数,电脑活跃秒数");
            lock (_gate)
            {
                if (Directory.Exists(_daysDir))
                {
                    string[] files = Directory.GetFiles(_daysDir, "*.json");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    foreach (string file in files)
                    {
                        DateTime date;
                        if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) continue;
                        DayData day = GetDayLocked(date);
                        List<AppUsage> apps = new List<AppUsage>(day.Apps.Values);
                        apps.Sort(delegate(AppUsage a, AppUsage b)
                        {
                            return b.Total.CompareTo(a.Total);
                        });
                        foreach (AppUsage app in apps)
                        {
                            if (app == null) continue;
                            lines.Add(CsvEscape(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) + "," +
                                CsvEscape(app.Name) + "," +
                                app.Total.ToString(CultureInfo.InvariantCulture) + "," +
                                CsvEscape(FormatCsvDuration(app.Total)) + "," +
                                app.ForegroundTotal.ToString(CultureInfo.InvariantCulture) + "," +
                                app.BackgroundTotal.ToString(CultureInfo.InvariantCulture) + "," +
                                day.ActiveSeconds.ToString(CultureInfo.InvariantCulture));
                        }
                    }
                }
            }
            File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(true));
            Logger.Write("CSV exported: " + path);
            return path;
        }

        private static string CsvEscape(string value)
        {
            if (value == null) return "\"\"";
            if (value.IndexOfAny(new char[] { ',', '"', '\r', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string FormatCsvDuration(long seconds)
        {
            if (seconds < 0) seconds = 0;
            long hours = seconds / 3600;
            long minutes = (seconds % 3600) / 60;
            long secs = seconds % 60;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", hours, minutes, secs);
        }

        public static void OpenBackupFolder(string path)
        {
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = "explorer.exe";
                info.Arguments = "\"" + path + "\"";
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception ex)
            {
                Logger.Error("OpenBackupFolder", ex);
            }
        }

        public static void OpenLogFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = "notepad.exe";
                info.Arguments = "\"" + path + "\"";
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception ex)
            {
                Logger.Error("OpenLogFile", ex);
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