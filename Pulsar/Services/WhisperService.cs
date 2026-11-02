using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pulsar.Services;

public enum TranscriptFormat { Srt, Vtt, Txt, All }

public sealed record TranscribeOptions(
    string InputFile,
    string OutputFolder,
    TranscriptFormat Format,
    string Language,
    bool TranslateToEnglish);

/// <summary>
/// Speech-to-text with Whisper, either locally via whisper.cpp (downloaded on demand) or through an
/// OpenAI-compatible /v1/audio/transcriptions server (OpenAI, faster-whisper-server, LocalAI, etc.).
/// </summary>
public static partial class WhisperService
{
    public static readonly (string Id, string Label)[] Models =
    [
        ("tiny", "Tiny (75 MB, fastest)"),
        ("base", "Base (142 MB, good balance)"),
        ("small", "Small (466 MB, more accurate)"),
        ("medium", "Medium (1.5 GB, slow on CPU)"),
        ("large-v3-turbo", "Large v3 Turbo (1.6 GB, best quality)"),
    ];

    public static readonly (string Code, string Name)[] Languages =
    [
        ("auto", "Detect automatically"), ("en", "English"), ("es", "Spanish"), ("fr", "French"), ("de", "German"),
        ("it", "Italian"), ("pt", "Portuguese"), ("nl", "Dutch"), ("pl", "Polish"), ("ru", "Russian"), ("uk", "Ukrainian"),
        ("tr", "Turkish"), ("ar", "Arabic"), ("hi", "Hindi"), ("ja", "Japanese"), ("ko", "Korean"), ("zh", "Chinese"),
        ("sv", "Swedish"),
    ];

    private const string FallbackEngineUrl = "https://github.com/ggml-org/whisper.cpp/releases/download/v1.7.4/whisper-bin-x64.zip";

    [GeneratedRegex(@"progress\s*=\s*(\d+)\s*%")]
    private static partial Regex ProgressRx();

    public static string ModelPath(string id) => Path.Combine(ToolPaths.Models, $"ggml-{id}.bin");
    public static bool HasModel(string id) => File.Exists(ModelPath(id));

    /// <summary>The whisper.cpp command-line tool: a custom path from Settings, or the copy Pulsar downloaded.</summary>
    public static string? FindCli()
    {
        string custom = App.Settings.Current.WhisperCustomExe;
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom)) return custom;
        if (!Directory.Exists(ToolPaths.Whisper)) return null;
        return Directory.EnumerateFiles(ToolPaths.Whisper, "whisper-cli.exe", SearchOption.AllDirectories).FirstOrDefault()
            ?? Directory.EnumerateFiles(ToolPaths.Whisper, "main.exe", SearchOption.AllDirectories).FirstOrDefault();
    }

    public static bool HasEngine => FindCli() is not null;

    /// <summary>Downloads the latest Windows x64 build of whisper.cpp into bin\whisper.</summary>
    public static async Task InstallEngineAsync(IProgress<double> progress, CancellationToken ct = default)
    {
        string url = await FindEngineUrlAsync(ct) ?? FallbackEngineUrl;
        string zip = Path.Combine(Path.GetTempPath(), $"pulsar-whisper-{Guid.NewGuid():N}.zip");
        try
        {
            await Http.DownloadFileAsync(url, zip, progress, ct);
            if (Directory.Exists(ToolPaths.Whisper)) Directory.Delete(ToolPaths.Whisper, recursive: true);
            ZipFile.ExtractToDirectory(zip, ToolPaths.Whisper, overwriteFiles: true);
            if (FindCli() is null)
                throw new InvalidOperationException("The whisper.cpp download didn't contain whisper-cli.exe.");
        }
        finally
        {
            try { File.Delete(zip); } catch { /* temp file */ }
        }
    }

    private static async Task<string?> FindEngineUrlAsync(CancellationToken ct)
    {
        try
        {
            string json = await Http.Client.GetStringAsync("https://api.github.com/repos/ggml-org/whisper.cpp/releases?per_page=15", ct);
            using var doc = JsonDocument.Parse(json);
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (!release.TryGetProperty("assets", out var assets)) continue;
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var name) && name.GetString() == "whisper-bin-x64.zip" &&
                        asset.TryGetProperty("browser_download_url", out var link))
                        return link.GetString();
                }
            }
        }
        catch
        {
            // Fall back to a known release.
        }
        return null;
    }

    public static Task DownloadModelAsync(string id, IProgress<double> progress, CancellationToken ct = default) =>
        Http.DownloadFileAsync($"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{id}.bin", ModelPath(id), progress, ct);

    /// <summary>Transcribes one file and returns the paths of the transcript files written.</summary>
    public static async Task<IReadOnlyList<string>> TranscribeAsync(
        TranscribeOptions o, IProgress<double> progress, Action<string> status, Action<string> log, CancellationToken ct)
    {
        var settings = App.Settings.Current;
        string stem = UniqueStem(o.OutputFolder, Path.GetFileNameWithoutExtension(o.InputFile), o.Format);
        string temp = Path.Combine(Path.GetTempPath(), $"pulsar-asr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            double? duration = await FfmpegService.GetDurationAsync(o.InputFile, ct);
            return settings.WhisperEngine == 1
                ? await TranscribeWithServerAsync(o, stem, temp, duration, progress, status, log, ct)
                : await TranscribeLocallyAsync(o, stem, temp, duration, progress, status, log, ct);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { /* temp files */ }
        }
    }

    private static async Task<IReadOnlyList<string>> TranscribeLocallyAsync(
        TranscribeOptions o, string stem, string temp, double? duration,
        IProgress<double> progress, Action<string> status, Action<string> log, CancellationToken ct)
    {
        string model = App.Settings.Current.WhisperModel;

        if (FindCli() is null)
        {
            status("Downloading whisper.cpp…");
            await InstallEngineAsync(progress, ct);
        }
        if (!HasModel(model))
        {
            status($"Downloading the {model} speech model (one time)…");
            await DownloadModelAsync(model, progress, ct);
        }

        status("Preparing audio…");
        string wav = Path.Combine(temp, "audio.wav");
        var scaled = new Progress<double>(p => progress.Report(p * 0.1));
        int code = await FfmpegService.RunAsync(["-i", o.InputFile, "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", wav],
            duration, scaled, log, ct);
        if (code != 0) throw new InvalidOperationException("ffmpeg couldn't read the audio from this file.");

        status("Transcribing…");
        var args = new List<string>
        {
            "-m", ModelPath(model), "-f", wav, "-l", o.Language, "-pp",
            "-t", Math.Clamp(Environment.ProcessorCount - 1, 1, 16).ToString(),
            "-of", Path.Combine(o.OutputFolder, stem),
        };
        if (o.TranslateToEnglish) args.Add("-tr");
        args.AddRange(o.Format switch
        {
            TranscriptFormat.Vtt => ["-ovtt"],
            TranscriptFormat.Txt => ["-otxt"],
            TranscriptFormat.All => ["-osrt", "-ovtt", "-otxt"],
            _ => ["-osrt"],
        });

        void OnLine(string line)
        {
            var m = ProgressRx().Match(line);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var pct))
                progress.Report(10 + pct * 0.9);
            else if (line.Contains("error", StringComparison.OrdinalIgnoreCase))
                log(line);
        }

        string cli = FindCli()!;
        code = await ProcessRunner.RunAsync(cli, args, OnLine, OnLine, ct, Path.GetDirectoryName(cli));
        if (code != 0) throw new InvalidOperationException($"whisper.cpp exited with code {code}.");

        progress.Report(100);
        return ExistingOutputs(o.OutputFolder, stem, o.Format);
    }

    private static async Task<IReadOnlyList<string>> TranscribeWithServerAsync(
        TranscribeOptions o, string stem, string temp, double? duration,
        IProgress<double> progress, Action<string> status, Action<string> log, CancellationToken ct)
    {
        var settings = App.Settings.Current;
        if (string.IsNullOrWhiteSpace(settings.WhisperServerUrl))
            throw new InvalidOperationException("Set the transcription server address in Settings first.");

        status("Preparing audio…");
        string mp3 = Path.Combine(temp, "audio.mp3");
        var scaled = new Progress<double>(p => progress.Report(p * 0.3));
        int code = await FfmpegService.RunAsync(["-i", o.InputFile, "-vn", "-ac", "1", "-ar", "16000", "-c:a", "libmp3lame", "-b:a", "48k", mp3],
            duration, scaled, log, ct);
        if (code != 0) throw new InvalidOperationException("ffmpeg couldn't read the audio from this file.");

        status("Uploading to the transcription server…");
        string srt = await PostAsync(settings, mp3, o.Language, o.TranslateToEnglish, ct);
        progress.Report(95);

        var written = new List<string>();
        void Write(string ext, string content)
        {
            string path = Path.Combine(o.OutputFolder, stem + ext);
            File.WriteAllText(path, content);
            written.Add(path);
        }

        if (o.Format is TranscriptFormat.Srt or TranscriptFormat.All) Write(".srt", srt);
        if (o.Format is TranscriptFormat.Vtt or TranscriptFormat.All) Write(".vtt", SubtitleText.SrtToVtt(srt));
        if (o.Format is TranscriptFormat.Txt or TranscriptFormat.All) Write(".txt", SubtitleText.SrtToText(srt));
        progress.Report(100);
        return written;
    }

    private static async Task<string> PostAsync(AppSettings settings, string audioFile, string language, bool translate, CancellationToken ct)
    {
        string baseUrl = settings.WhisperServerUrl.Trim().TrimEnd('/');
        if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) baseUrl += "/v1";
        string endpoint = baseUrl + (translate ? "/audio/translations" : "/audio/transcriptions");

        using var form = new MultipartFormDataContent();
        var file = new StreamContent(File.OpenRead(audioFile));
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        form.Add(file, "file", "audio.mp3");
        form.Add(new StringContent(string.IsNullOrWhiteSpace(settings.WhisperServerModel) ? "whisper-1" : settings.WhisperServerModel), "model");
        form.Add(new StringContent("srt"), "response_format");
        if (!translate && language != "auto") form.Add(new StringContent(language), "language");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = form };
        string key = Secret.Unprotect(settings.WhisperApiKeyProtected);
        if (!string.IsNullOrEmpty(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var response = await Http.Client.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"The server returned {(int)response.StatusCode}: {Truncate(body, 300)}");
        return body;
    }

    private static string UniqueStem(string folder, string stem, TranscriptFormat format)
    {
        string[] exts = format switch
        {
            TranscriptFormat.Vtt => [".vtt"],
            TranscriptFormat.Txt => [".txt"],
            TranscriptFormat.All => [".srt", ".vtt", ".txt"],
            _ => [".srt"],
        };
        string candidate = stem;
        for (int i = 2; exts.Any(e => File.Exists(Path.Combine(folder, candidate + e))); i++)
            candidate = $"{stem} ({i})";
        return candidate;
    }

    private static List<string> ExistingOutputs(string folder, string stem, TranscriptFormat format) =>
        new[] { ".srt", ".vtt", ".txt" }
            .Select(e => Path.Combine(folder, stem + e))
            .Where(File.Exists)
            .ToList();

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
