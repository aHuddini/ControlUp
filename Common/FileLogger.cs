using System;
using System.Collections.Generic;
using System.IO;

namespace ControlUp.Common
{
    public class FileLogger
    {
        private readonly string _logFilePath;
        private readonly object _lockObject = new object();
        private bool _initialized = false;

        public FileLogger(string extensionPath)
        {
            var possiblePaths = new List<string>();

            if (!string.IsNullOrEmpty(extensionPath) && Directory.Exists(extensionPath))
            {
                possiblePaths.Add(Path.Combine(extensionPath, Constants.LogFileName));
            }

            var playniteAppData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Constants.PlayniteFolderName,
                Constants.PlayniteExtensionsFolderName);

            if (Directory.Exists(playniteAppData))
            {
                var extensionFolders = Directory.GetDirectories(playniteAppData, Constants.ExtensionFolderName + "*");
                if (extensionFolders.Length > 0)
                {
                    possiblePaths.Add(Path.Combine(extensionFolders[0], Constants.LogFileName));
                }
            }

            possiblePaths.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Constants.PlayniteFolderName,
                Constants.LogFileName));

            _logFilePath = possiblePaths.Count > 0 ? possiblePaths[0] : possiblePaths[possiblePaths.Count - 1];

            try
            {
                var logDir = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                File.AppendAllText(_logFilePath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] FileLogger initialized. Log file: {_logFilePath}\n");
                _initialized = true;
            }
            catch
            {
                var fallbackIndex = possiblePaths.Count - 1;
                _logFilePath = possiblePaths[fallbackIndex];
                try
                {
                    var logDir = Path.GetDirectoryName(_logFilePath);
                    if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                    {
                        Directory.CreateDirectory(logDir);
                    }
                    File.AppendAllText(_logFilePath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] FileLogger initialized (fallback). Log file: {_logFilePath}\n");
                    _initialized = true;
                }
                catch
                {
                    _initialized = false;
                }
            }
        }

        public void Log(string level, string message, Exception exception = null)
        {
            if (!_initialized) return;

            try
            {
                lock (_lockObject)
                {
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    var logEntry = $"[{timestamp}] [{level}] {message}";

                    if (exception != null)
                    {
                        logEntry += $"\nException: {exception.GetType().Name}: {exception.Message}";
                        logEntry += $"\nStack Trace: {exception.StackTrace}";
                    }

                    logEntry += Environment.NewLine;
                    File.AppendAllText(_logFilePath, logEntry);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FileLogger failed to write: {ex.Message}");
            }
        }

        public void Info(string message) => Log("INFO", message);
        public void Debug(string message) => Log("DEBUG", message);
        public void Warn(string message) => Log("WARN", message);
        public void Error(string message, Exception exception = null) => Log("ERROR", message, exception);
    }
}
