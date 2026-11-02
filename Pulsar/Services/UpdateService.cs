using System.IO.Compression;
using System.Text.Json;

namespace Pulsar.Services;

/// <summary>Installs and updates yt-dlp and ffmpeg in ToolPaths.Bin.</summary>
public static class UpdateService
{
    private const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string FfmpegZipUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

    public static async Task<string?> GetYtDlpVersionAsync()
    {
        if (!ToolPaths.HasYtDlp) return null;
        string? version = null;
        await ProcessRunner.RunAsync(ToolPaths.YtDlp, ["--version"],
            l => { if (!string.IsNullOrWhiteSpace(l)) version ??= l.Trim(); }, null);
        return version;
    }

    public static async Task<string?> GetFfmpegVersionAsync()
    {
        if (!ToolPaths.HasFfmpeg) return null;
        string? first = null;
        await ProcessRunner.RunAsync(ToolPaths.Ffmpeg, ["-version"],
            l => { if (!string.IsNullOrWhiteSpace(l)) first ??= l.Trim(); }, null);
        // "ffmpeg version N-117xxx-gabc123-20261030 Copyright ..."
        var parts = first?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts is { Length: > 2 } ? parts[2] : first;
    }

    /// <summary>Latest release tag for the channel ("stable" or "nightly") from the GitHub API.</summary>
    public static async Task<string?> GetLatestYtDlpVersionAsync(string channel)
    {
        string repo = channel == "nightly" ? "yt-dlp/yt-dlp-nightly-builds" : "yt-dlp/yt-dlp";
        using var doc = JsonDocument.Parse(await Http.Client.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest"));
        return doc.RootElement.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
    }

    /// <summary>yt-dlp's built-in self-updater (works on the standalone .exe).</summary>
    public static Task<int> SelfUpdateAsync(string channel, Action<string> log, CancellationToken ct = default) =>
        ProcessRunner.RunAsync(ToolPaths.YtDlp, ["--update-to", channel], log, log, ct);

    public static Task InstallYtDlpAsync(IProgress<double> progress, CancellationToken ct = default) =>
        Http.DownloadFileAsync(YtDlpUrl, ToolPaths.YtDlp, progress, ct);

    public static async Task InstallFfmpegAsync(IProgress<double> progress, CancellationToken ct = default)
    {
        string zip = Path.Combine(Path.GetTempPath(), $"pulsar-ffmpeg-{Guid.NewGuid():N}.zip");
        try
        {
            await Http.DownloadFileAsync(FfmpegZipUrl, zip, progress, ct);
            using var archive = ZipFile.OpenRead(zip);
            foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe" })
            {
                var entry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("/bin/" + name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"{name} wasn't found in the ffmpeg archive.");
                entry.ExtractToFile(Path.Combine(ToolPaths.Bin, name), overwrite: true);
            }
        }
        finally
        {
            try { File.Delete(zip); } catch { /* temp file */ }
        }
    }
}
