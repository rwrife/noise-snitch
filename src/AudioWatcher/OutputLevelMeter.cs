using System;

namespace NoiseSnitch.AudioWatcher;

/// <summary>Instant attack and linear release of half the full scale per second.</summary>
internal static class OutputLevelMeter
{
    public static float Update(float previous, float? peak, TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);

        // A missing/invalid reading clears even a held peak after device loss.
        if (peak is not float sample || !float.IsFinite(sample))
        {
            return 0;
        }

        float current = Math.Clamp(sample, 0, 1);
        float held = float.IsFinite(previous) ? Math.Clamp(previous, 0, 1) : 0;
        return Math.Max(current, (float)Math.Max(0, held - elapsed.TotalSeconds * 0.5));
    }
}
