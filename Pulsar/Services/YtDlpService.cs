using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pulsar.Models;

namespace Pulsar.Services;

public static partial class YtDlpService
{
    // Tags we ask yt-dlp to prefix onto its output so we can parse it reliably.
    private const string TagProgress = "[pulsar]";
    private const string TagPostProcess = "[pulsarpp]";
    private const string TagTitle = "[pulsartitle]";
    private const string TagFile = "[pulsarfile]";

    public static readonly (string Id, string Label)[] SponsorBlockCategories =
    [
        ("sponsor", "Sponsors"),
        ("selfpromo", "Self-promotion"),
        ("interaction", "Like/subscribe reminders"),
        ("intro", "Intros"),
        ("outro", "Outros / end cards"),
        ("preview", "Previews and recaps"),
        ("filler", "Filler and tangents"),
        ("music_offtopic", "Non-music parts of music videos"),
    ];

    public static readonly (string Id, string Label)[] Browsers =
    [
        ("", "Don't use cookies"),
        ("edge", "Microsoft Edge"),
        ("chrome", "Google Chrome"),
        ("firefox", "Firefox"),
        ("brave", "Brave"),
        ("opera", "Opera"),
        ("vivaldi", "Vivaldi"),
    ];

    [GeneratedRegex(@"([\d.]+)\s*%")]
    private static partial Regex PercentRx();

    [GeneratedRegex(@"^\d+(\.\d+)?[KkMmGg]?$")]
    private static partial Regex RateRx();

    public static bool IsValidRate(string? rate) => string.IsNullOrWhiteSpace(rate) || RateRx().IsMatch(rate.Trim());

    public static bool LooksLikeUrl(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        text.Length < 2048 &&
        !text.Trim().Contains(' ') &&
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        uri.Host.Contains('.');

    /// <summary>Title, thumbnail and available formats, via --dump-single-json.</summary>
    public static async Task<VideoInfo> GetInfoAsync(string url, bool playlist, string? cookiesBrowser, CancellationToken ct)
    {
        var json = new StringBuilder();
        var err = new StringBuilder();
        var args = new List<string> { "--dump-single-json", "--flat-playlist", "--no-warnings", "--encoding", "utf-8" };
        if (!playlist) args.Add("--no-playlist");
        if (!string.IsNullOrWhiteSpace(cookiesBrowser)) args.AddRange(["--cookies-from-browser", cookiesBrowser]);
        args.Add("--");
        args.Add(url);

        int code = await ProcessRunner.RunAsync(ToolPaths.YtDlp, args,
            l => { lock (json) json.Append(l); },
            l => { lock (err) err.AppendLine(l); }, ct);

        if (code != 0)
            throw new InvalidOperationException(LastError(err.ToString()) ?? $"yt-dlp exited with code {code}.");

        using var doc = JsonDocument.Parse(json.ToString());
        var root = doc.RootElement;

        bool isPlaylist = Str(root, "_type") == "playlist";
        int entries = isPlaylist && root.TryGetProperty("entries", out var e) && e.ValueKind == JsonValueKind.Array
            ? e.GetArrayLength() : 0;

        var formats = new List<FormatOption> { new("Use preset", null, false) };
        if (root.TryGetProperty("formats", out var fmts) && fmts.ValueKind == JsonValueKind.Array)
        {
            // yt-dlp lists worst → best; show best first.
            foreach (var f in fmts.EnumerateArray().Reverse())
            {
                string? id = Str(f, "format_id");
                if (id is null) continue;
                string vcodec = Str(f, "vcodec") ?? "none";
                string acodec = Str(f, "acodec") ?? "none";
                if (vcodec == "none" && acodec == "none") continue; // storyboards/images

                bool audioOnly = vcodec == "none";
                bool videoOnly = acodec == "none";

                var parts = new List<string> { id, Str(f, "ext") ?? "?" };
                parts.Add(audioOnly ? "audio only" : Str(f, "resolution") ?? "video");
                if (!audioOnly && Num(f, "fps") is double fps) parts.Add($"{fps:0}fps");
                parts.Add(ShortCodec(audioOnly ? acodec : vcodec));
                if (Num(f, "tbr") is double tbr) parts.Add($"{tbr:0}k");
                if ((Num(f, "filesize") ?? Num(f, "filesize_approx")) is double size) parts.Add(FormatSize(size));
                if (videoOnly) parts.Add("+ best audio");

                // Video-only formats get the best audio merged in automatically.
                string spec = videoOnly ? $"{id}+ba/{id}" : id;
                formats.Add(new FormatOption(string.Join("  •  ", parts), spec, audioOnly));
            }
        }

        return new VideoInfo(
            Str(root, "title") ?? url,
            Str(root, "uploader") ?? Str(root, "channel"),
            Num(root, "duration") is double d ? TimeParser.Format(d) : null,
            Str(root, "thumbnail") ?? LastThumbnail(root),
            isPlaylist,
            entries,
            formats);
    }

    /// <summary>Runs one download, reporting progress. Returns yt-dlp's exit code.</summary>
    public static Task<int> DownloadAsync(DownloadRequest r, IProgress<DownloadUpdate> progress, CancellationToken ct)
    {
        var args = new List<string>
        {
            "--newline", "--no-colors", "--encoding", "utf-8", "--no-warnings",
            "--quiet", "--progress", "--no-simulate",
            "--progress-template", $"download:{TagProgress}%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s",
            "--progress-template", $"postprocess:{TagPostProcess}%(progress.postprocessor)s",
            "--print", $"before_dl:{TagTitle}%(title)s",
            "--print", $"after_move:{TagFile}%(webpage_url)s\t%(filepath)s",
            "--ffmpeg-location", ToolPaths.Bin,
            "-P", r.OutputFolder,
            "-o", r.FileTemplate,
        };
        args.AddRange(r.FormatArgs);

        if (r.EmbedThumbnail) args.Add("--embed-thumbnail");
        if (r.EmbedMetadata) args.Add("--embed-metadata");
        if (r.EmbedChapters || r.SponsorBlockMode == 1) args.Add("--embed-chapters");
        if (r.Subtitles && !r.AudioOnly) args.AddRange(["--write-subs", "--sub-langs", "en.*", "--embed-subs"]);
        args.Add(r.Playlist ? "--yes-playlist" : "--no-playlist");

        if (r.SplitChapters)
            args.AddRange(["--split-chapters", "-o", "chapter:%(title)s - chapters/%(section_number)02d - %(section_title)s.%(ext)s"]);

        if (r.SponsorBlockMode > 0 && !string.IsNullOrWhiteSpace(r.SponsorBlockCategories))
            args.AddRange([r.SponsorBlockMode == 2 ? "--sponsorblock-remove" : "--sponsorblock-mark", r.SponsorBlockCategories]);

        if (!string.IsNullOrWhiteSpace(r.CookiesBrowser)) args.AddRange(["--cookies-from-browser", r.CookiesBrowser]);
        if (!string.IsNullOrWhiteSpace(r.RateLimit)) args.AddRange(["--limit-rate", r.RateLimit.Trim()]);
        if (!string.IsNullOrWhiteSpace(r.ArchivePath)) args.AddRange(["--download-archive", r.ArchivePath]);
        if (!string.IsNullOrWhiteSpace(r.PlaylistItems)) args.AddRange(["--playlist-items", r.PlaylistItems]);
        if (r.IgnoreErrors) args.Add("--ignore-errors");

        if (!string.IsNullOrWhiteSpace(r.SectionStart) || !string.IsNullOrWhiteSpace(r.SectionEnd))
        {
            string start = string.IsNullOrWhiteSpace(r.SectionStart) ? "0" : r.SectionStart.Trim();
            string end = string.IsNullOrWhiteSpace(r.SectionEnd) ? "inf" : r.SectionEnd.Trim();
            args.AddRange(["--download-sections", $"*{start}-{end}", "--force-keyframes-at-cuts"]);
        }

        args.Add("--");
        args.Add(r.Url);

        void OnLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            if (line.StartsWith(TagProgress, StringComparison.Ordinal))
            {
                var p = line[TagProgress.Length..].Split('|');
                var m = PercentRx().Match(p[0]);
                double? pct = m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                    ? v : null;
                progress.Report(new DownloadUpdate(
                    Percent: pct,
                    Speed: p.Length > 1 ? p[1].Trim() : "",
                    Eta: p.Length > 2 ? p[2].Trim() : ""));
            }
            else if (line.StartsWith(TagPostProcess, StringComparison.Ordinal))
            {
                progress.Report(new DownloadUpdate(Message: $"Post-processing ({line[TagPostProcess.Length..].Trim()})…", Processing: true));
            }
            else if (line.StartsWith(TagTitle, StringComparison.Ordinal))
            {
                progress.Report(new DownloadUpdate(Title: line[TagTitle.Length..].Trim()));
            }
            else if (line.StartsWith(TagFile, StringComparison.Ordinal))
            {
                var rest = line[TagFile.Length..];
                int tab = rest.IndexOf('\t');
                string url = tab >= 0 ? rest[..tab].Trim() : "";
                string path = tab >= 0 ? rest[(tab + 1)..].Trim() : rest.Trim();
                progress.Report(new DownloadUpdate(FilePath: path, FileUrl: url == "NA" ? null : url));
            }
            else if (line.StartsWith("ERROR:", StringComparison.Ordinal))
            {
                progress.Report(new DownloadUpdate(Message: line[6..].Trim(), IsError: true));
            }
            else
            {
                progress.Report(new DownloadUpdate(Message: line.Trim()));
            }
        }

        return ProcessRunner.RunAsync(ToolPaths.YtDlp, args, OnLine, OnLine, ct);
    }

    /// <summary>
    /// Records the newest <paramref name="count"/> items of a channel/playlist in the archive without downloading,
    /// so a new subscription only picks up uploads published from now on.
    /// </summary>
    public static async Task PrimeArchiveAsync(string url, string archivePath, int count, string? cookiesBrowser, CancellationToken ct)
    {
        var args = new List<string>
        {
            "--flat-playlist", "--simulate", "--quiet", "--no-warnings", "--ignore-errors",
            "--force-write-archive", "--download-archive", archivePath,
            "--playlist-items", $"1:{count}",
        };
        if (!string.IsNullOrWhiteSpace(cookiesBrowser)) args.AddRange(["--cookies-from-browser", cookiesBrowser]);
        args.Add("--");
        args.Add(url);

        var err = new StringBuilder();
        int code = await ProcessRunner.RunAsync(ToolPaths.YtDlp, args, null, l => { lock (err) err.AppendLine(l); }, ct);
        if (code != 0 && !File.Exists(archivePath))
            throw new InvalidOperationException(LastError(err.ToString()) ?? $"yt-dlp exited with code {code}.");
    }

    private static string? LastError(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var error = lines.LastOrDefault(l => l.StartsWith("ERROR:", StringComparison.Ordinal));
        return error is not null ? error[6..].Trim() : lines.LastOrDefault();
    }

    private static string? LastThumbnail(JsonElement root) =>
        root.TryGetProperty("thumbnails", out var t) && t.ValueKind == JsonValueKind.Array && t.GetArrayLength() > 0
            ? Str(t[t.GetArrayLength() - 1], "url")
            : null;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static string ShortCodec(string codec) => codec.Split('.')[0];

    private static string FormatSize(double bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (1L << 30):0.00} GiB",
        >= 1L << 20 => $"{bytes / (1L << 20):0.0} MiB",
        _ => $"{bytes / 1024:0} KiB",
    };
}
