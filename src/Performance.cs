using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TimeTrack
{
    internal static class PerformanceMode
    {
        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr process);

        public static void ApplyPriority(bool lightweight)
        {
            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    process.PriorityClass = lightweight
                        ? ProcessPriorityClass.BelowNormal
                        : ProcessPriorityClass.Normal;
                }
            }
            catch
            {
            }
        }

        public static void TrimWorkingSet()
        {
            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    EmptyWorkingSet(process.Handle);
                }
            }
            catch
            {
            }
        }
    }
}