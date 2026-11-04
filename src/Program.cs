using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace TimeTrack
{
    internal static class Program
    {
        private const string MutexName = "Local\\TimeTrack.SingleInstance";
        private const string ShowEventName = "Local\\TimeTrack.ShowWindow";

        [STAThread]
        public static void Main(string[] args)
        {
            bool startHidden = false;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--tray", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(args[i], "/tray", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(args[i], "--hidden", StringComparison.OrdinalIgnoreCase))
                    {
                        startHidden = true;
                    }
                }
            }

            bool created = false;
            Mutex mutex = null;
            try
            {
                mutex = new Mutex(true, MutexName, out created);
            }
            catch (AbandonedMutexException)
            {
                created = true;
            }
            catch
            {
                created = true;
            }

            if (!created)
            {
                SignalExistingInstance();
                return;
            }

            EventWaitHandle showEvent = null;
            try
            {
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            }
            catch
            {
            }

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
            {
                Logger.Error("Dispatcher", e.Exception);
                e.Handled = true;
                MessageBox.Show("发生了一个意外错误，详细信息已写入日志。", "时间统计",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                if (ex != null) Logger.Error("Unhandled", ex);
            };

            MainWindow window = null;
            try
            {
                window = new MainWindow(startHidden);
                app.MainWindow = window;
                if (!startHidden) window.Show();
            }
            catch (Exception ex)
            {
                Logger.Error("Startup", ex);
                MessageBox.Show("时间统计启动失败：" + ex.Message, "时间统计",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                if (window != null) window.DisposeApp();
                if (mutex != null)
                {
                    try { mutex.ReleaseMutex(); } catch { }
                    mutex.Dispose();
                }
                return;
            }

            Thread listener = new Thread(delegate()
            {
                while (showEvent != null)
                {
                    try { showEvent.WaitOne(); }
                    catch { return; }

                    if (window == null || window.Dispatcher.HasShutdownStarted) return;
                    try
                    {
                        window.Dispatcher.BeginInvoke(new Action(window.ShowFromTray));
                    }
                    catch
                    {
                        return;
                    }
                }
            });
            listener.IsBackground = true;
            listener.Name = "TimeTrack activation";
            listener.Start();

            try
            {
                app.Run();
            }
            catch (Exception ex)
            {
                Logger.Error("App run", ex);
            }
            finally
            {
                if (window != null) window.DisposeApp();
                if (showEvent != null) showEvent.Dispose();
                if (mutex != null)
                {
                    try { mutex.ReleaseMutex(); } catch { }
                    mutex.Dispose();
                }
            }
        }

        private static void SignalExistingInstance()
        {
            for (int attempt = 0; attempt < 24; attempt++)
            {
                try
                {
                    using (EventWaitHandle handle = EventWaitHandle.OpenExisting(ShowEventName))
                    {
                        handle.Set();
                        return;
                    }
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    Thread.Sleep(120);
                }
                catch
                {
                    Thread.Sleep(120);
                }
            }

            MessageBox.Show("时间统计已经在运行，请在任务栏通知区域找到它的图标。", "时间统计",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}