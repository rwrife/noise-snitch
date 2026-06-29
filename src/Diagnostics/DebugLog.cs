using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace NoiseSnitch.Diagnostics;

/// <summary>
/// Tiny append-only debug logger. Lines go to <see cref="Trace"/> (visible in a
/// debugger / DebugView) and to a plain-text file under
/// <c>%LOCALAPPDATA%\noise-snitch\noise-snitch.log</c> so the M2 audio dump can
/// be inspected without attaching a debugger.
///
/// This is intentionally minimal — M6 introduces real persistence/export. Until
/// then this is just a breadcrumb trail for development.
/// </summary>
internal static class DebugLog
{
    private static readonly object Gate = new();
    private static readonly Lazy<string?> LogPath = new(ResolveLogPath);

    /// <summary>Writes one timestamped line to the trace listeners and the log file.</summary>
    public static void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff}  {message}";
        Trace.WriteLine(line);

        var path = LogPath.Value;
        if (path is null)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (IOException)
        {
            // Logging must never take the app down; a locked/unavailable file is
            // non-fatal. The trace line above still went out.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Returns the resolved log file path, or <c>null</c> if it can't be created.</summary>
    public static string? FilePath => LogPath.Value;

    private static string? ResolveLogPath()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "noise-snitch");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "noise-snitch.log");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
