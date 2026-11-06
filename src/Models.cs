using System;
using System.Collections.Generic;

namespace TimeTrack
{
    public class AppUsage
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public long[] Hours { get; set; }
        public long[] ForegroundHours { get; set; }
        public long[] BackgroundHours { get; set; }

        public AppUsage()
        {
            Hours = new long[24];
            ForegroundHours = new long[24];
            BackgroundHours = new long[24];
        }

        public long Total
        {
            get { return Sum(Hours); }
        }

        public long ForegroundTotal
        {
            get { return Sum(ForegroundHours); }
        }

        public long BackgroundTotal
        {
            get { return Sum(BackgroundHours); }
        }

        /// <summary>Seconds recorded before foreground/background was split.</summary>
        public long UnclassifiedTotal
        {
            get
            {
                long known = ForegroundTotal + BackgroundTotal;
                return Total > known ? Total - known : 0;
            }
        }

        private static long Sum(long[] values)
        {
            long total = 0;
            if (values == null) return 0;
            for (int i = 0; i < values.Length; i++) total += values[i];
            return total;
        }
    }

    public class DayData
    {
        public string Date { get; set; }
        public Dictionary<string, AppUsage> Apps { get; set; }

        /// <summary>Wall-clock seconds on which the computer was actually in use.</summary>
        public long ActiveSeconds { get; set; }

        /// <summary>Active seconds split by hour; unlike app sums, overlapping apps count once.</summary>
        public long[] ActiveHours { get; set; }

        public DayData()
        {
            Apps = new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase);
            ActiveHours = new long[24];
        }

        public long Total
        {
            get
            {
                long total = 0;
                foreach (AppUsage app in Apps.Values) total += app.Total;
                return total;
            }
        }
    }

    public class AppMeta
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
    }

    public class Settings
    {
        public List<string> ExcludedKeys { get; set; }
        public List<AppMeta> KnownApps { get; set; }
        public bool AutoStart { get; set; }
        public bool Paused { get; set; }
        public bool TrackBackground { get; set; }
        public bool Lightweight { get; set; }
        public bool DarkMode { get; set; }
        public Dictionary<string, string> CustomNames { get; set; }
        public Dictionary<string, string> Categories { get; set; }
        public Dictionary<string, string> CategoryColors { get; set; }
        public int IdleSeconds { get; set; }
        public bool AutoBackup { get; set; }
        public bool AutoCleanup { get; set; }
        public int RetentionDays { get; set; }
        public int CleanupIntervalDays { get; set; }
        public long LastCleanupUnixSeconds { get; set; }

        public Settings()
        {
            ExcludedKeys = new List<string>();
            KnownApps = new List<AppMeta>();
            CustomNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CategoryColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IdleSeconds = 120;
            AutoStart = false;
            Paused = false;
            TrackBackground = true;
            Lightweight = false;
            DarkMode = true;
            AutoBackup = true;
            AutoCleanup = false;
            RetentionDays = 90;
            CleanupIntervalDays = 30;
            LastCleanupUnixSeconds = 0;
        }
    }

    public static class CategoryCatalog
    {
        public static readonly string[] Names = new string[]
        {
            "工作", "学习", "娱乐", "社交", "工具", "其他"
        };

        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "其他";
            return value.Trim();
        }

        public static bool IsBuiltIn(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < Names.Length; i++)
            {
                if (string.Equals(Names[i], value, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static string DefaultColor(string category)
        {
            if (string.Equals(category, "工作", StringComparison.Ordinal)) return "#3B82F6";
            if (string.Equals(category, "学习", StringComparison.Ordinal)) return "#22C55E";
            if (string.Equals(category, "娱乐", StringComparison.Ordinal)) return "#F59E0B";
            if (string.Equals(category, "社交", StringComparison.Ordinal)) return "#EC4899";
            if (string.Equals(category, "工具", StringComparison.Ordinal)) return "#06B6D4";
            if (string.Equals(category, "其他", StringComparison.Ordinal)) return "#8B5CF6";
            return "#8B5CF6";
        }

        public static string NormalizeColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string text = value.Trim();
            if (text.Length != 7 || text[0] != '#') return null;
            for (int i = 1; i < text.Length; i++)
            {
                char c = text[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return null;
            }
            return "#" + text.Substring(1).ToUpperInvariant();
        }

        public static string Next(string value)
        {
            string current = Normalize(value);
            for (int i = 0; i < Names.Length; i++)
            {
                if (string.Equals(Names[i], current, StringComparison.Ordinal))
                    return Names[(i + 1) % Names.Length];
            }
            return Names[0];
        }
    }

    public class AppIdentity
    {
        public int ProcessId;
        public string Key;
        public string Name;
        public string Path;
    }

    public class DayBar
    {
        public DateTime Date;
        public long Seconds;
    }

    public class AppAgg
    {
        public string Key;
        public string Name;
        public string Path;
        public long Seconds;
        public long ForegroundSeconds;
        public long BackgroundSeconds;
        public long UnclassifiedSeconds;
    }

    public class RangeData
    {
        public DateTime Start;
        public DateTime End;
        public long Total;
        public long Average;
        public long ForegroundTotal;
        public long BackgroundTotal;
        public long UnclassifiedTotal;
        public int DayCount;
        public List<DayBar> Days;
        public List<AppAgg> Apps;
        public long[] HourBins;

        public RangeData()
        {
            Days = new List<DayBar>();
            Apps = new List<AppAgg>();
            HourBins = new long[24];
        }
    }

    public class UsageBreakdown
    {
        public long Total;
        public long Foreground;
        public long Background;
        public long Unclassified;

        public long Known
        {
            get { return Foreground + Background; }
        }
    }

    public class ChartItem
    {
        public string Label;
        public long Seconds;
        public string Tip;
        public bool Highlight;
    }
}