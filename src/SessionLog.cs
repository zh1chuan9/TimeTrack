using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace TimeTrack
{
    /// <summary>
    /// Keeps a tiny on-disk state file plus a heartbeat. If the previous state
    /// was still "running" at startup, the last process did not shut down
    /// cleanly; that fact is written to the normal log for later diagnosis.
    /// </summary>
    public sealed class SessionLog : IDisposable
    {
        private readonly object _gate = new object();
        private readonly string _statePath;
        private readonly string _heartbeatPath;
        private Timer _timer;
        private bool _closed;

        public SessionLog(string root)
        {
            Directory.CreateDirectory(root);
            _statePath = Path.Combine(root, "session-state.txt");
            _heartbeatPath = Path.Combine(root, "heartbeat.txt");
        }

        public void Start()
        {
            try
            {
                if (File.Exists(_statePath))
                {
                    string previous = File.ReadAllText(_statePath, Encoding.UTF8).Trim();
                    if (previous.StartsWith("running", StringComparison.OrdinalIgnoreCase))
                    {
                        string beat = File.Exists(_heartbeatPath)
                            ? File.ReadAllText(_heartbeatPath, Encoding.UTF8).Trim()
                            : "unknown";
                        Logger.Write("Previous session did not exit cleanly. Last heartbeat: " + beat);
                    }
                }

                WriteState("running|" + Now() + "|pid=" + Process.GetCurrentProcess().Id);
                WriteHeartbeat();
                Logger.Write("Session started. pid=" + Process.GetCurrentProcess().Id);
                _timer = new Timer(delegate { WriteHeartbeat(); }, null, 60000, 60000);
            }
            catch (Exception ex)
            {
                Logger.Error("SessionLog.Start", ex);
            }
        }

        public void MarkCleanExit(string reason)
        {
            lock (_gate)
            {
                if (_closed) return;
                _closed = true;
                try
                {
                    if (_timer != null) _timer.Dispose();
                    WriteState("clean|" + Now() + "|" + (reason ?? "exit"));
                    File.WriteAllText(_heartbeatPath, "stopped|" + Now(), new UTF8Encoding(false));
                    Logger.Write("Clean exit: " + (reason ?? "exit"));
                }
                catch (Exception ex)
                {
                    Logger.Error("SessionLog.MarkCleanExit", ex);
                }
            }
        }

        private void WriteHeartbeat()
        {
            try
            {
                if (_closed) return;
                File.WriteAllText(_heartbeatPath, Now() + "|pid=" + Process.GetCurrentProcess().Id, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Logger.Error("SessionLog heartbeat", ex);
            }
        }

        private void WriteState(string value)
        {
            File.WriteAllText(_statePath, value, new UTF8Encoding(false));
        }

        private static string Now()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public void Dispose()
        {
            MarkCleanExit("dispose");
        }
    }
}
