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

        public DayData()
        {
            Apps = new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase);
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
        public Dictionary<string, string> CustomNames { get; set; }
        public int IdleSeconds { get; set; }

        public Settings()
        {
            ExcludedKeys = new List<string>();
            KnownApps = new List<AppMeta>();
            CustomNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IdleSeconds = 120;
            AutoStart = false;
            Paused = false;
            TrackBackground = true;
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