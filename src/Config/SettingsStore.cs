using System;
using System.IO;
using System.Text.Json;
using NoiseSnitch.Diagnostics;
using NoiseSnitch.Model;

namespace NoiseSnitch.Config;

/// <summary>
/// Loads and saves <see cref="NoiseSnitch.Model.Settings"/> as JSON under
/// <c>%LOCALAPPDATA%\noise-snitch\settings.json</c> (same folder as the debug
/// log). This is the M5 "persist settings locally" slice — deliberately tiny and
/// dependency-free (<c>System.Text.Json</c> ships with the runtime; no new
/// NuGet).
///
/// Robustness rules:
/// <list type="bullet">
/// <item><see cref="Load"/> never throws: a missing, empty, or corrupt file (or
/// an unreadable location) yields clamped <see cref="Model.Settings.Defaults"/>,
/// so the app always boots.</item>
/// <item>Everything returned by <see cref="Load"/> is already
/// <see cref="Model.Settings.Normalized"/>, so callers can trust the ranges.</item>
/// <item><see cref="Save"/> writes atomically (temp file + replace) so a crash
/// mid-write can't leave a truncated file that fails to parse next launch.</item>
/// </list>
/// </summary>
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Be forgiving of hand-edits: tolerate comments and trailing commas.
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string? _path;

    /// <summary>
    /// Creates a store rooted at the given file path, or (default) the standard
    /// <c>%LOCALAPPDATA%\noise-snitch\settings.json</c> location. A <c>null</c>
    /// resolved path (e.g. the folder can't be created) makes this a no-op store
    /// that always returns defaults and silently skips saving.
    /// </summary>
    public SettingsStore(string? path = null)
    {
        _path = path ?? ResolveDefaultPath();
    }

    /// <summary>The resolved settings file path, or <c>null</c> if unavailable.</summary>
    public string? FilePath => _path;

    /// <summary>
    /// Reads and normalizes settings from disk, falling back to clamped defaults
    /// on any problem. Never throws.
    /// </summary>
    public Settings Load()
    {
        if (_path is null || !File.Exists(_path))
        {
            return Settings.Defaults();
        }

        try
        {
            string json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Settings.Defaults();
            }

            var parsed = JsonSerializer.Deserialize<Settings>(json, JsonOptions);
            // A literal "null" document deserializes to null — treat as defaults.
            return (parsed ?? Settings.Defaults()).Normalized();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DebugLog.Write($"[settings] load failed ({ex.GetType().Name}); using defaults: {ex.Message}");
            return Settings.Defaults();
        }
    }

    /// <summary>
    /// Persists settings (normalized first). Returns <c>true</c> on success. A
    /// failure is logged and swallowed — losing a settings write must never take
    /// the app down.
    /// </summary>
    public bool Save(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_path is null)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string json = JsonSerializer.Serialize(settings.Normalized(), JsonOptions);

            // Atomic-ish write: stage to a temp file in the same directory, then
            // move into place so readers never observe a partial file.
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(_path))
            {
                File.Replace(tmp, _path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tmp, _path);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugLog.Write($"[settings] save failed ({ex.GetType().Name}): {ex.Message}");
            return false;
        }
    }

    private static string? ResolveDefaultPath()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "noise-snitch");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
