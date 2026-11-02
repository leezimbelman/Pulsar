namespace Pulsar.Models;

/// <summary>Everything needed to run one yt-dlp download.</summary>
public sealed class DownloadRequest
{
    public required string Url { get; init; }
    public required string[] FormatArgs { get; init; }
    public required string OutputFolder { get; init; }
    public required string FileTemplate { get; init; }
    public bool AudioOnly { get; init; }
    public string PresetName { get; init; } = "";

    public bool EmbedThumbnail { get; init; }
    public bool EmbedMetadata { get; init; }
    public bool EmbedChapters { get; init; }
    public bool SplitChapters { get; init; }
    public bool Subtitles { get; init; }
    public bool Playlist { get; init; }
    public string? SectionStart { get; init; }
    public string? SectionEnd { get; init; }

    /// <summary>0 = off, 1 = mark as chapters, 2 = remove.</summary>
    public int SponsorBlockMode { get; init; }
    public string SponsorBlockCategories { get; init; } = "";

    public string? CookiesBrowser { get; init; }
    public string? RateLimit { get; init; }

    // Used by subscriptions
    public string? ArchivePath { get; init; }
    public string? PlaylistItems { get; init; }
    public bool IgnoreErrors { get; init; }
    public string? SubscriptionName { get; init; }
}

/// <summary>A specific format reported by yt-dlp for one video. FormatSpec null = "use the preset".</summary>
public sealed record FormatOption(string Label, string? FormatSpec, bool AudioOnly)
{
    public override string ToString() => Label;
}

public sealed record VideoInfo(
    string Title,
    string? Uploader,
    string? Duration,
    string? Thumbnail,
    bool IsPlaylist,
    int EntryCount,
    IReadOnlyList<FormatOption> Formats);

/// <summary>Live progress parsed from yt-dlp output.</summary>
public sealed record DownloadUpdate(
    double? Percent = null,
    string? Speed = null,
    string? Eta = null,
    string? Message = null,
    string? FilePath = null,
    string? FileUrl = null,
    string? Title = null,
    bool Processing = false,
    bool IsError = false);
