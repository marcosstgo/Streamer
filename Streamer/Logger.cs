using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Streamer
{
    internal static class Logger
    {
        private static readonly object _lock = new object();
        private static string? _logPath;
        private const long MaxBytes = 5 * 1024 * 1024; // 5 MB

        public static bool EnableInfoLogging { get; set; }

        private static string GetLogPath()
        {
            if (_logPath != null) return _logPath;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "CorilloStreamer");
            try { Directory.CreateDirectory(folder); } catch { }
            _logPath = Path.Combine(folder, "streamer.log");
            return _logPath;
        }

        public static void LogInfo(string message)
        {
            if (!EnableInfoLogging) return;
            TryWrite("INFO", message);
        }

        public static void LogError(string message)
        {
            TryWrite("ERROR", message);
        }

        private static void TryWrite(string level, string message)
        {
            try
            {
                var path = GetLogPath();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                lock (_lock)
                {
                    try
                    {
                        File.AppendAllText(path, line, Encoding.UTF8);
                    }
                    catch
                    {
                        // attempt rotation once
                        try
                        {
                            if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                            {
                                var bak = path + ".1";
                                try { File.Delete(bak); } catch { }
                                File.Move(path, bak);
                                File.AppendAllText(path, line, Encoding.UTF8);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }
    }
}
