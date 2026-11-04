using System;
using System.IO;
using System.Text;

namespace TimeTrack
{
    public static class Logger
    {
        private static readonly object Gate = new object();

        public static string FilePath;

        public static void Write(string message)
        {
            try
            {
                if (string.IsNullOrEmpty(FilePath)) return;
                lock (Gate)
                {
                    FileInfo info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > 1024 * 1024)
                    {
                        try { File.Delete(FilePath); }
                        catch { }
                    }
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine;
                    File.AppendAllText(FilePath, line, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        public static void Error(string area, Exception ex)
        {
            if (ex == null) return;
            Write(area + ": " + ex.GetType().Name + ": " + ex.Message);
        }
    }
}