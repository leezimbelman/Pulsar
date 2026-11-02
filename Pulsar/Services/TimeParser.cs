using System.Globalization;

namespace Pulsar.Services;

/// <summary>Parses "90", "1:30" or "01:02:03.5" into seconds, and formats times back.</summary>
public static class TimeParser
{
    public static bool TryParse(string? text, out double seconds, bool allowEmpty)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text)) return allowEmpty;

        var parts = text.Trim().Split(':');
        if (parts.Length > 3) return false;

        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0)
                return false;
            seconds = seconds * 60 + v;
        }
        return true;
    }

    public static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    /// <summary>Like Format, but keeps tenths of a second (for trim points picked in the player).</summary>
    public static string FormatPrecise(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        string whole = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
        int tenths = t.Milliseconds / 100;
        return tenths == 0 ? whole : $"{whole}.{tenths}";
    }

    public static string ToFfmpeg(double seconds) => seconds.ToString("0.###", CultureInfo.InvariantCulture);
}
