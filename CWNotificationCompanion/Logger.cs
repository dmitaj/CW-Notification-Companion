using System;
using System.IO;

namespace CWNotificationCompanion;

/// <summary>
/// Minimal append-only diagnostic log. Writes to
/// %APPDATA%\CWNotificationCompanion\log.txt so notification/startup failures
/// (which are otherwise invisible in a tray-only app) can be inspected.
/// </summary>
internal static class Logger
{
    private static readonly object Gate = new();
    private static readonly string LogPath = BuildPath();

    private static string BuildPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "CWNotificationCompanion");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "log.txt");
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
        }
        catch { /* logging must never throw */ }
    }
}
