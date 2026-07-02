using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using NoiseSnitch.Diagnostics;
using NoiseSnitch.Model;

namespace NoiseSnitch.Persistence;

/// <summary>
/// The M6 "forensics that survive restarts" store: an append-only, size-capped
/// <b>JSONL</b> log of <see cref="NoiseEvent"/>s under
/// <c>%LOCALAPPDATA%\noise-snitch\noise-log.jsonl</c> (one
/// <see cref="NoiseLogRecord"/> per line), plus the read side that powers
/// "copy/export the last hour."
///
/// <para>Design mirrors the rest of the app's persistence (<c>SettingsStore</c>,
/// <c>DebugLog</c>):</para>
/// <list type="bullet">
/// <item><b>Opt-in.</b> The tray only calls <see cref="Append"/> when the user
/// enabled <see cref="Settings.PersistLog"/>; the class itself is happy to run
/// regardless.</item>
/// <item><b>Never throws.</b> Every public method swallows IO/JSON faults and
/// logs a breadcrumb \u2014 losing (or failing to read) history must never take the
/// app down or drop a live event.</item>
/// <item><b>Bounded.</b> When the file would exceed
/// <see cref="Settings.MaxLogBytes"/>, the oldest lines are dropped and the newer
/// tail is kept (rotation-in-place), so the log self-limits without external
/// cron.</item>
/// <item><b>Line-oriented &amp; append-friendly.</b> JSONL means a crash mid-write
/// costs at most the last partial line; readers simply skip any line that won't
/// parse.</item>
/// </list>
/// </summary>
internal sealed class NoiseLog
{
    /// <summary>Default log file name under the app's LocalAppData folder.</summary>
    public const string DefaultFileName = "noise-log.jsonl";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // One record per physical line: never indent, and be forgiving on read.
        WriteIndented = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Tolerate hand-edits that use different key casing than we emit.
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    private readonly string? _path;
    private readonly long _maxBytes;

    /// <summary>
    /// Creates a log at the given path (or the default LocalAppData location) with
    /// the given size cap. A <c>null</c> resolved path (folder can't be created)
    /// makes this a no-op store: <see cref="Append"/> silently does nothing and
    /// reads return empty.
    /// </summary>
    public NoiseLog(string? path = null, long maxBytes = Settings.DefaultMaxLogBytes)
    {
        _path = path ?? ResolveDefaultPath();
        _maxBytes = maxBytes <= 0 ? Settings.DefaultMaxLogBytes : maxBytes;
    }

    /// <summary>The resolved log file path, or <c>null</c> if unavailable.</summary>
    public string? FilePath => _path;

    /// <summary>The effective size cap in bytes before rotation kicks in.</summary>
    public long MaxBytes => _maxBytes;

    /// <summary>
    /// Appends one event as a JSONL line, rotating first if the file has grown
    /// past the cap. Returns <c>true</c> if the line was written. Never throws.
    /// </summary>
    public bool Append(NoiseEvent e)
    {
        if (_path is null)
        {
            return false;
        }

        try
        {
            string line = JsonSerializer.Serialize(NoiseLogRecord.From(e), JsonOptions);

            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                RotateIfNeeded_NoLock(extraBytes: Encoding.UTF8.GetByteCount(line) + 1);
                File.AppendAllText(_path, line + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            DebugLog.Write($"[log] append failed ({ex.GetType().Name}): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reads every parseable event currently on disk, <b>oldest first</b> (file
    /// order). Lines that don't parse are skipped, not fatal. Never throws.
    /// </summary>
    public IReadOnlyList<NoiseEvent> ReadAll()
    {
        if (_path is null || !File.Exists(_path))
        {
            return Array.Empty<NoiseEvent>();
        }

        var result = new List<NoiseEvent>();
        try
        {
            lock (_gate)
            {
                foreach (string raw in File.ReadLines(_path))
                {
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        continue;
                    }

                    NoiseLogRecord? rec;
                    try
                    {
                        rec = JsonSerializer.Deserialize<NoiseLogRecord>(raw, JsonOptions);
                    }
                    catch (JsonException)
                    {
                        // A torn last line or a hand-edit typo: skip just this row.
                        continue;
                    }

                    if (rec is not null)
                    {
                        result.Add(rec.ToEvent());
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugLog.Write($"[log] read failed ({ex.GetType().Name}): {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Returns events whose timestamp falls within the last <paramref name="window"/>
    /// relative to <paramref name="nowUtc"/>, <b>newest first</b> (the order an
    /// export/clipboard block reads best). Never throws.
    /// </summary>
    public IReadOnlyList<NoiseEvent> ReadSince(TimeSpan window, DateTime nowUtc)
    {
        DateTime cutoff = nowUtc - window;
        var all = ReadAll();
        var recent = new List<NoiseEvent>();
        for (int i = all.Count - 1; i >= 0; i--) // newest first
        {
            if (all[i].TimestampUtc >= cutoff)
            {
                recent.Add(all[i]);
            }
        }

        return recent;
    }

    /// <summary>Deletes the log file entirely. Never throws.</summary>
    public void Clear()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugLog.Write($"[log] clear failed ({ex.GetType().Name}): {ex.Message}");
        }
    }

    /// <summary>
    /// If the existing file plus the incoming line would exceed the cap, drops the
    /// oldest whole lines and rewrites the newer tail so the result comfortably
    /// fits (targets ~50% of the cap to avoid rotating on every subsequent write).
    /// Caller must hold <see cref="_gate"/>.
    /// </summary>
    private void RotateIfNeeded_NoLock(int extraBytes)
    {
        if (_path is null || !File.Exists(_path))
        {
            return;
        }

        long current = new FileInfo(_path).Length;
        if (current + extraBytes <= _maxBytes)
        {
            return;
        }

        try
        {
            // Keep the newest lines that fit in ~half the cap, so we don't rotate
            // again on the very next append.
            long keepBudget = Math.Max(_maxBytes / 2, extraBytes);
            string[] lines = File.ReadAllLines(_path);

            var kept = new List<string>();
            long running = 0;
            for (int i = lines.Length - 1; i >= 0; i--) // newest -> oldest
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                long size = Encoding.UTF8.GetByteCount(lines[i]) + 1;
                if (running + size > keepBudget && kept.Count > 0)
                {
                    break;
                }

                kept.Add(lines[i]);
                running += size;
            }

            kept.Reverse(); // back to oldest -> newest

            // Atomic-ish replace: stage to a temp file, then move into place so a
            // reader never sees a half-rotated file.
            string tmp = _path + ".tmp";
            File.WriteAllText(
                tmp,
                kept.Count == 0 ? string.Empty : string.Join("\n", kept) + "\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            File.Replace(tmp, _path, destinationBackupFileName: null);
            DebugLog.Write($"[log] rotated: kept {kept.Count} newest lines (~{running} bytes)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // If rotation fails we'd rather keep appending (and risk a slightly
            // oversized file) than lose the event; log and move on.
            DebugLog.Write($"[log] rotation failed ({ex.GetType().Name}): {ex.Message}");
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
            return Path.Combine(dir, DefaultFileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
