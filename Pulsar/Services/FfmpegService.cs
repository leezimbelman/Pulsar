using System.Globalization;

namespace Pulsar.Services;

public sealed record MediaProbe(double? Duration, int Width, int Height, bool HasVideo, bool HasAudio);

public static class FfmpegService
{
    public static async Task<double?> GetDurationAsync(string file, CancellationToken ct = default) =>
        (await ProbeAsync(file, ct)).Duration;

    /// <summary>Duration, first video stream size, and which stream types exist.</summary>
    public static async Task<MediaProbe> ProbeAsync(string file, CancellationToken ct = default)
    {
        var lines = new List<string>();
        int code = await ProcessRunner.RunAsync(ToolPaths.Ffprobe,
            ["-v", "error", "-show_entries", "format=duration:stream=codec_type,width,height", "-of", "default=noprint_wrappers=1", file],
            l => { lock (lines) lines.Add(l.Trim()); }, null, ct);
        if (code != 0) return new MediaProbe(null, 0, 0, false, false);

        double? duration = null;
        int width = 0, height = 0;
        bool hasVideo = false, hasAudio = false;
        foreach (var line in lines)
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq], value = line[(eq + 1)..];
            switch (key)
            {
                case "duration" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d):
                    duration = d;
                    break;
                case "codec_type" when value == "video":
                    hasVideo = true;
                    break;
                case "codec_type" when value == "audio":
                    hasAudio = true;
                    break;
                case "width" when width == 0 && int.TryParse(value, out var w):
                    width = w;
                    break;
                case "height" when height == 0 && int.TryParse(value, out var h):
                    height = h;
                    break;
            }
        }
        // Cover art in audio files shows up as a tiny "video" stream; ignore it when there's no real size.
        if (hasVideo && (width == 0 || height == 0)) hasVideo = false;
        return new MediaProbe(duration, width, height, hasVideo, hasAudio);
    }

    /// <summary>
    /// Runs ffmpeg with machine-readable progress on stdout (-progress pipe:1).
    /// Percent is computed from out_time against <paramref name="totalSeconds"/>.
    /// </summary>
    public static Task<int> RunAsync(
        IEnumerable<string> args,
        double? totalSeconds,
        IProgress<double>? progress,
        Action<string>? log,
        CancellationToken ct,
        string? workingDirectory = null)
    {
        var full = new List<string> { "-hide_banner", "-nostdin", "-y", "-nostats", "-loglevel", "error", "-progress", "pipe:1" };
        full.AddRange(args);

        void OnOutput(string line)
        {
            if (line.StartsWith("out_time_us=", StringComparison.Ordinal) || line.StartsWith("out_time_ms=", StringComparison.Ordinal))
            {
                // Both keys are in microseconds (out_time_ms is a historical misnomer).
                if (totalSeconds is > 0 && long.TryParse(line.AsSpan(12), out var us))
                    progress?.Report(Math.Clamp(us / 1_000_000.0 / totalSeconds.Value * 100, 0, 100));
            }
            else if (line == "progress=end")
            {
                progress?.Report(100);
            }
        }

        return ProcessRunner.RunAsync(ToolPaths.Ffmpeg, full, OnOutput, log, ct, workingDirectory);
    }
}
