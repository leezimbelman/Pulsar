namespace Pulsar.Models;

/// <summary>yt-dlp download presets shown on the Download page.</summary>
public sealed record FormatPreset(string Name, string[] Args, bool AudioOnly = false)
{
    public override string ToString() => Name;

    public static IReadOnlyList<FormatPreset> All { get; } =
    [
        new("Best quality (MP4)", ["-f", "bv*[ext=mp4]+ba[ext=m4a]/bv*+ba/b", "--merge-output-format", "mp4"]),
        new("Best quality (original container)", ["-f", "bv*+ba/b"]),
        new("1080p MP4", ["-f", "bv*[height<=1080][ext=mp4]+ba[ext=m4a]/bv*[height<=1080]+ba/b[height<=1080]", "--merge-output-format", "mp4"]),
        new("720p MP4", ["-f", "bv*[height<=720][ext=mp4]+ba[ext=m4a]/bv*[height<=720]+ba/b[height<=720]", "--merge-output-format", "mp4"]),
        new("480p MP4 (small)", ["-f", "bv*[height<=480][ext=mp4]+ba[ext=m4a]/bv*[height<=480]+ba/b[height<=480]", "--merge-output-format", "mp4"]),
        new("Audio — MP3 (best VBR)", ["-f", "ba/b", "-x", "--audio-format", "mp3", "--audio-quality", "0"], true),
        new("Audio — M4A (AAC)", ["-f", "ba[ext=m4a]/ba/b", "-x", "--audio-format", "m4a"], true),
        new("Audio — Opus", ["-f", "ba/b", "-x", "--audio-format", "opus"], true),
        new("Audio — FLAC (lossless container)", ["-f", "ba/b", "-x", "--audio-format", "flac"], true),
        new("Audio — original (no re-encode)", ["-f", "ba/b", "-x"], true),
    ];
}

/// <summary>ffmpeg presets used on the Tools page.</summary>
public sealed record FfmpegPreset(string Name, string Extension, string[] Args)
{
    public override string ToString() => Name;

    public static IReadOnlyList<FfmpegPreset> Convert { get; } =
    [
        new("MP4 — H.264 + AAC (most compatible)", ".mp4",
            ["-map", "0:v:0?", "-map", "0:a?", "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"]),
        new("MP4 — H.265 / HEVC (smaller)", ".mp4",
            ["-map", "0:v:0?", "-map", "0:a?", "-c:v", "libx265", "-preset", "medium", "-crf", "24", "-tag:v", "hvc1", "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart"]),
        new("WebM — VP9 + Opus", ".webm",
            ["-map", "0:v:0?", "-map", "0:a?", "-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-row-mt", "1", "-c:a", "libopus", "-b:a", "128k"]),
        new("Compress for sharing — 720p H.264", ".mp4",
            ["-map", "0:v:0?", "-map", "0:a?", "-vf", "scale=-2:'min(720,ih)'", "-c:v", "libx264", "-preset", "slow", "-crf", "28", "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart"]),
        new("MKV — remux (no re-encode, instant)", ".mkv", ["-map", "0", "-c", "copy"]),
        new("MP4 — remux (no re-encode, instant)", ".mp4", ["-map", "0:v?", "-map", "0:a?", "-c", "copy", "-movflags", "+faststart"]),
        new("GIF — 480px, 12 fps", ".gif",
            ["-vf", "fps=12,scale=480:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse", "-loop", "0"]),
    ];

    public static IReadOnlyList<FfmpegPreset> Audio { get; } =
    [
        new("MP3 — best VBR (V0)", ".mp3", ["-vn", "-c:a", "libmp3lame", "-q:a", "0"]),
        new("M4A — AAC 256k", ".m4a", ["-vn", "-c:a", "aac", "-b:a", "256k"]),
        new("Opus — 160k", ".opus", ["-vn", "-c:a", "libopus", "-b:a", "160k"]),
        new("FLAC — lossless", ".flac", ["-vn", "-c:a", "flac"]),
        new("WAV — 16-bit PCM", ".wav", ["-vn", "-c:a", "pcm_s16le"]),
        new("Original track (no re-encode)", ".mka", ["-vn", "-map", "0:a:0", "-c:a", "copy"]),
    ];
}
