using System;
using System.Text.Json.Serialization;
using NoiseSnitch.Model;

namespace NoiseSnitch.Persistence;

/// <summary>
/// The on-disk projection of a <see cref="NoiseEvent"/> — one of these is
/// serialized per line in the JSONL noise log (M6). Kept as a separate,
/// public-shaped DTO (rather than serializing <see cref="NoiseEvent"/> directly)
/// so the file format is an explicit, stable contract we can evolve
/// independently of the in-memory struct.
///
/// <para>
/// It is a plain, mutable, parameterless-constructible class so
/// <c>System.Text.Json</c> round-trips it without custom converters and a
/// hand-edited or partially-written line still deserializes (missing keys fall
/// back to defaults, exactly like <see cref="Settings"/>).
/// </para>
///
/// <para>
/// <b>Schema (v1)</b> — the property names below are the JSON keys and are part
/// of the documented format (see README §Data format):
/// <list type="bullet">
/// <item><c>t</c>  — ISO-8601 UTC timestamp of the onset.</item>
/// <item><c>pid</c> — owning process id (<c>0</c> = system-sounds session).</item>
/// <item><c>name</c> — resolved process name (e.g. <c>chrome</c>).</item>
/// <item><c>peak</c> — peak meter value in <c>[0,1]</c> at onset.</item>
/// <item><c>session</c> — Windows session display name, when present.</item>
/// </list>
/// Short keys keep each line small (this file can grow to thousands of rows).
/// </para>
/// </summary>
internal sealed class NoiseLogRecord
{
    /// <summary>ISO-8601 (round-trip) UTC timestamp of the onset.</summary>
    [JsonPropertyName("t")]
    public DateTime T { get; set; }

    /// <summary>Owning process id, or <c>0</c> for the system-sounds session.</summary>
    [JsonPropertyName("pid")]
    public uint Pid { get; set; }

    /// <summary>Resolved process name (e.g. <c>chrome</c>) or best-effort fallback.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Peak meter value (<c>[0,1]</c>) at the moment the onset was detected.</summary>
    [JsonPropertyName("peak")]
    public float Peak { get; set; }

    /// <summary>Session display name as reported by Windows, when present.</summary>
    [JsonPropertyName("session")]
    public string Session { get; set; } = string.Empty;

    /// <summary>Projects an in-memory <see cref="NoiseEvent"/> into its on-disk form.</summary>
    public static NoiseLogRecord From(NoiseEvent e) => new()
    {
        // Normalize to UTC so the file is timezone-agnostic regardless of how the
        // struct's Kind was set upstream.
        T = e.TimestampUtc.Kind == DateTimeKind.Utc
            ? e.TimestampUtc
            : e.TimestampUtc.ToUniversalTime(),
        Pid = e.ProcessId,
        Name = e.ProcessName ?? string.Empty,
        Peak = e.Peak,
        Session = e.SessionName ?? string.Empty,
    };

    /// <summary>Rehydrates the in-memory <see cref="NoiseEvent"/> from this record.</summary>
    public NoiseEvent ToEvent() => new(
        DateTime.SpecifyKind(T, DateTimeKind.Utc),
        Pid,
        Name ?? string.Empty,
        Peak,
        Session ?? string.Empty);
}
