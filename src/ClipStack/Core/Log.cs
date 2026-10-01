using System.IO;

namespace ClipStack.Core;

/// <summary>
/// Minimal file logger. Never logs clipboard contents — only events and errors.
/// Errors are always written; info lines only when logging is enabled in settings.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    public static bool Verbose { get; set; }

    public static void Info(string message)
    {
        if (Verbose) Write("INFO", message);
    }

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var file = AppPaths.LogFile;
                if (File.Exists(file) && new FileInfo(file).Length > 1_000_000)
                    File.Move(file, file + ".old", overwrite: true);
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
