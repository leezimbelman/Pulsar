using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulsar.Models;
using Pulsar.Services;

namespace Pulsar.ViewModels;

public enum ToolOp { Trim, Convert, ExtractAudio, Merge, Normalize, BurnSubtitles, Transform, Transcribe }

public sealed record ToolInfo(ToolOp Op, string Name, string Glyph, string Description);

public partial class ToolsViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;

    public ToolsViewModel()
    {
        Inputs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasInputs));
            OnPropertyChanged(nameof(HasNoInputs));
            OnPropertyChanged(nameof(InputSummary));
            OnPropertyChanged(nameof(ShowBatchHint));
            RunCommand.NotifyCanExecuteChanged();
        };
    }

    public IReadOnlyList<ToolInfo> Operations { get; } =
    [
        new(ToolOp.Trim, "Trim", "\uE8C6", "Cut out a section. Fast mode copies streams; frame-accurate mode re-encodes."),
        new(ToolOp.Convert, "Convert", "\uE8AB", "Change format or codec, shrink files for sharing, or make a GIF."),
        new(ToolOp.ExtractAudio, "Extract audio", "\uE8D6", "Pull the audio track out as MP3, M4A, Opus, FLAC or WAV."),
        new(ToolOp.Merge, "Merge", "\uE71D", "Join the files in the list, in order, into one file."),
        new(ToolOp.Normalize, "Normalize audio", "\uE995", "Even out loudness so every file plays at the same volume."),
        new(ToolOp.BurnSubtitles, "Burn subtitles", "\uE7F0", "Permanently draw an .srt, .ass or .vtt file onto the video."),
        new(ToolOp.Transform, "Rotate & crop", "\uE7AD", "Rotate, flip, crop to an aspect ratio, or resize. Fixes sideways phone videos."),
        new(ToolOp.Transcribe, "Transcribe", "\uE720", "Create subtitles or a transcript from speech with Whisper."),
    ];

    public IReadOnlyList<FfmpegPreset> ConvertPresets => FfmpegPreset.Convert;
    public IReadOnlyList<FfmpegPreset> AudioPresets => FfmpegPreset.Audio;
    public IReadOnlyList<string> NormalizeTargets { get; } =
        ["Streaming (−14 LUFS, Spotify / YouTube)", "Podcast / voice (−16 LUFS)", "Broadcast (−23 LUFS, EBU R128)"];
    public IReadOnlyList<string> SubtitleSizes { get; } = ["Default size", "Small", "Medium", "Large"];
    public IReadOnlyList<string> Rotations { get; } = ["Don't rotate", "90° clockwise", "90° counter-clockwise", "180°"];
    public IReadOnlyList<string> Crops { get; } = ["Don't crop", "16:9 (widescreen)", "9:16 (vertical / Shorts)", "1:1 (square)", "4:3", "4:5 (portrait post)"];
    public IReadOnlyList<string> Sizes { get; } = ["Keep size", "2160p (4K)", "1440p", "1080p", "720p", "480p"];
    public IReadOnlyList<string> TranscriptFormats { get; } = ["Subtitles (.srt)", "Web subtitles (.vtt)", "Plain text (.txt)", "All three"];
    public IReadOnlyList<string> LanguageNames { get; } = WhisperService.Languages.Select(l => l.Name).ToList();

    // ---- Operation ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Op), nameof(Current), nameof(IsTrim), nameof(IsConvert), nameof(IsExtract), nameof(IsMerge),
        nameof(IsNormalize), nameof(IsBurn), nameof(IsTransform), nameof(IsTranscribe), nameof(RunLabel), nameof(ShowBatchHint),
        nameof(ShowReorder), nameof(EngineSummary))]
    private int _operationIndex;

    public ToolInfo Current => Operations[Math.Clamp(OperationIndex, 0, Operations.Count - 1)];
    public ToolOp Op => Current.Op;
    public bool IsTrim => Op == ToolOp.Trim;
    public bool IsConvert => Op == ToolOp.Convert;
    public bool IsExtract => Op == ToolOp.ExtractAudio;
    public bool IsMerge => Op == ToolOp.Merge;
    public bool IsNormalize => Op == ToolOp.Normalize;
    public bool IsBurn => Op == ToolOp.BurnSubtitles;
    public bool IsTransform => Op == ToolOp.Transform;
    public bool IsTranscribe => Op == ToolOp.Transcribe;
    public bool ShowReorder => IsMerge;
    public string RunLabel => Op switch
    {
        ToolOp.Merge => "Merge",
        ToolOp.Transcribe => "Transcribe",
        _ => Inputs.Count > 1 ? $"{Current.Name} {Inputs.Count} files" : Current.Name,
    };

    // ---- Inputs ----
    public ObservableCollection<MediaFile> Inputs { get; } = [];
    public bool HasInputs => Inputs.Count > 0;
    public bool HasNoInputs => Inputs.Count == 0;
    public string InputSummary => Inputs.Count switch
    {
        0 => "Add files, or drag them onto this page.",
        1 => "1 file",
        var n => $"{n} files",
    };
    public bool ShowBatchHint => Inputs.Count > 1 && Op is not ToolOp.Merge;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand), nameof(MoveUpCommand), nameof(MoveDownCommand), nameof(PreviewCommand))]
    private int _selectedInputIndex = -1;

    // ---- Options ----
    [ObservableProperty] private string _trimStart = "";
    [ObservableProperty] private string _trimEnd = "";
    [ObservableProperty] private bool _accurateTrim;
    [ObservableProperty] private int _convertPresetIndex;
    [ObservableProperty] private int _audioPresetIndex;
    [ObservableProperty] private bool _mergeReencode;
    [ObservableProperty] private string _mergeName = "merged";
    [ObservableProperty] private int _normalizeIndex;
    [ObservableProperty] private string _subtitleFile = "";
    [ObservableProperty] private int _subtitleSizeIndex;
    [ObservableProperty] private int _rotationIndex;
    [ObservableProperty] private bool _flipHorizontal;
    [ObservableProperty] private bool _flipVertical;
    [ObservableProperty] private int _cropIndex;
    [ObservableProperty] private int _sizeIndex;
    [ObservableProperty] private int _transcriptFormatIndex;
    [ObservableProperty] private int _languageIndex;
    [ObservableProperty] private bool _translateToEnglish;
    [ObservableProperty] private string _outputFolder = "";

    public string EngineSummary
    {
        get
        {
            var s = App.Settings.Current;
            if (s.WhisperEngine == 1)
                return string.IsNullOrWhiteSpace(s.WhisperServerUrl)
                    ? "Engine: server (not set up yet, see Settings → Transcription)"
                    : $"Engine: server at {s.WhisperServerUrl}";
            string model = WhisperService.Models.FirstOrDefault(m => m.Id == s.WhisperModel).Label ?? s.WhisperModel;
            string note = WhisperService.HasEngine && WhisperService.HasModel(s.WhisperModel) ? "" : " (downloads on first use)";
            return $"Engine: whisper.cpp on this PC, {model}{note}";
        }
    }

    public void RefreshEngineSummary() => OnPropertyChanged(nameof(EngineSummary));

    // ---- Progress ----
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(ShowIndeterminate), nameof(HasOutput), nameof(IsIdle))]
    private bool _isRunning;

    [ObservableProperty] private double _percent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIndeterminate))]
    private bool _isIndeterminate;

    [ObservableProperty] private string _status = "Pick a tool, add files, and press the button.";
    [ObservableProperty] private string _log = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutput))]
    private string? _lastOutput;

    public bool IsIdle => !IsRunning;
    public bool ShowIndeterminate => IsIndeterminate && IsRunning;
    public bool HasOutput => LastOutput is not null && !IsRunning;

    // ---- Input management ----
    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path) || Inputs.Any(i => string.Equals(i.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            var file = new MediaFile(path);
            Inputs.Add(file);
            _ = file.LoadAsync();
        }
        OnPropertyChanged(nameof(RunLabel));
    }

    /// <summary>Sets up a trim from in/out points chosen in the player.</summary>
    public void SetupTrim(string path, double startSeconds, double? endSeconds)
    {
        Inputs.Clear();
        AddFiles([path]);
        OperationIndex = 0;
        TrimStart = TimeParser.FormatPrecise(startSeconds);
        TrimEnd = endSeconds is double e ? TimeParser.FormatPrecise(e) : "";
        Status = "Trim points copied from the player. Press Trim when ready.";
    }

    [RelayCommand]
    private async Task AddFilesAsync() => AddFiles(await Pickers.PickFilesAsync());

    private bool HasSelection() => SelectedInputIndex >= 0 && SelectedInputIndex < Inputs.Count;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RemoveSelected()
    {
        int index = SelectedInputIndex;
        Inputs.RemoveAt(index);
        SelectedInputIndex = Math.Min(index, Inputs.Count - 1);
        OnPropertyChanged(nameof(RunLabel));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveUp()
    {
        int i = SelectedInputIndex;
        if (i <= 0) return;
        Inputs.Move(i, i - 1);
        SelectedInputIndex = i - 1;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveDown()
    {
        int i = SelectedInputIndex;
        if (i < 0 || i >= Inputs.Count - 1) return;
        Inputs.Move(i, i + 1);
        SelectedInputIndex = i + 1;
    }

    [RelayCommand]
    private void ClearInputs()
    {
        Inputs.Clear();
        OnPropertyChanged(nameof(RunLabel));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Preview() => App.OpenPlayer(Inputs[SelectedInputIndex].FilePath);

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        if (await Pickers.PickFolderAsync() is { } folder) OutputFolder = folder;
    }

    [RelayCommand]
    private async Task BrowseSubtitleAsync()
    {
        if (await Pickers.PickFileAsync(0, ".srt", ".ass", ".ssa", ".vtt") is { } file) SubtitleFile = file;
    }

    [RelayCommand]
    private void PlayOutput() => App.OpenPlayer(LastOutput);

    [RelayCommand]
    private void OpenOutput() => Pickers.ShowInExplorer(LastOutput, OutputFolder);

    // ---- Running ----
    private bool CanRun() => !IsRunning && Inputs.Count > 0 && (Op != ToolOp.Merge || Inputs.Count >= 2);

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (!ToolPaths.HasFfmpeg)
        {
            Status = "ffmpeg isn't installed yet. Install it from Settings.";
            return;
        }
        if (Op == ToolOp.Merge && Inputs.Count < 2)
        {
            Status = "Add at least two files to merge.";
            return;
        }
        if (Validate() is { } problem)
        {
            Status = problem;
            return;
        }

        var files = Inputs.ToList();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsRunning = true;
        Percent = 0;
        LastOutput = null;
        Log = "";
        IProgress<string> log = new Progress<string>(AppendLog);
        var outputs = new List<string>();
        int failures = 0;

        try
        {
            if (Op == ToolOp.Merge)
            {
                Status = $"Merging {files.Count} files…";
                outputs.Add(await MergeAsync(files, p => Percent = p, log.Report, ct));
            }
            else
            {
                for (int i = 0; i < files.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var file = files[i];
                    string prefix = files.Count > 1 ? $"File {i + 1} of {files.Count}: " : "";
                    Status = $"{prefix}{Current.Name}   →   {file.Name}";
                    int index = i;
                    Action<double> report = p => Percent = (index + p / 100) / files.Count * 100;
                    Action<string> status = s => Status = $"{prefix}{s}";
                    try
                    {
                        outputs.AddRange(await RunOneAsync(file, report, status, log.Report, ct));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        AppendLog($"{file.Name}: {ex.Message}");
                        if (files.Count == 1) throw;
                    }
                }
            }

            Percent = 100;
            LastOutput = outputs.LastOrDefault();
            Status = (outputs.Count, failures) switch
            {
                (0, _) => "Nothing was created. See the log below.",
                (1, 0) => $"Done   •   {Path.GetFileName(outputs[0])}",
                (var n, 0) => $"Done   •   {n} files created",
                (var n, var f) => $"Finished with problems   •   {n} created, {f} failed (see log)",
            };
            if (outputs.Count > 0 && !App.IsMainWindowActive && App.Settings.Current.NotifyOnComplete)
                NotificationService.Show($"{Current.Name} finished", Status, LastOutput);
        }
        catch (OperationCanceledException)
        {
            Status = "Canceled.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsRunning = false;
            IsIndeterminate = false;
            _cts.Dispose();
            _cts = null;
            RefreshEngineSummary();
        }
    }

    private string? Validate()
    {
        switch (Op)
        {
            case ToolOp.Trim:
                if (!TimeParser.TryParse(TrimStart, out double start, allowEmpty: true))
                    return "Start time isn't valid. Use seconds, m:ss or h:mm:ss.";
                if (!string.IsNullOrWhiteSpace(TrimEnd) &&
                    (!TimeParser.TryParse(TrimEnd, out double end, allowEmpty: false) || end <= start))
                    return "End time must be a valid time after the start.";
                if (string.IsNullOrWhiteSpace(TrimStart) && string.IsNullOrWhiteSpace(TrimEnd))
                    return "Enter a start time, an end time, or both.";
                break;
            case ToolOp.Transform:
                if (RotationIndex == 0 && !FlipHorizontal && !FlipVertical && CropIndex == 0 && SizeIndex == 0)
                    return "Choose at least one change: rotate, flip, crop or resize.";
                break;
            case ToolOp.BurnSubtitles:
                if (!string.IsNullOrWhiteSpace(SubtitleFile) && !File.Exists(SubtitleFile))
                    return "The subtitle file doesn't exist.";
                break;
            case ToolOp.Transcribe:
                if (App.Settings.Current.WhisperEngine == 1 && string.IsNullOrWhiteSpace(App.Settings.Current.WhisperServerUrl))
                    return "Set the transcription server address in Settings → Transcription, or switch to the local engine.";
                break;
        }
        return null;
    }

    private string FolderFor(MediaFile file) =>
        string.IsNullOrWhiteSpace(OutputFolder) ? Path.GetDirectoryName(file.FilePath)! : OutputFolder.Trim();

    private async Task<IReadOnlyList<string>> RunOneAsync(
        MediaFile file, Action<double> report, Action<string> status, Action<string> log, CancellationToken ct)
    {
        string input = file.FilePath;
        string folder = FolderFor(file);
        Directory.CreateDirectory(folder);
        string stem = Path.GetFileNameWithoutExtension(input);
        var probe = file.Probe ?? await FfmpegService.ProbeAsync(input, ct);
        double? total = probe.Duration;
        IsIndeterminate = total is not > 0;
        var progress = new Progress<double>(report);

        if (Op == ToolOp.Transcribe)
        {
            IsIndeterminate = false;
            var lang = WhisperService.Languages[Math.Clamp(LanguageIndex, 0, WhisperService.Languages.Length - 1)].Code;
            var options = new TranscribeOptions(input, folder, (TranscriptFormat)TranscriptFormatIndex, lang, TranslateToEnglish);
            return await WhisperService.TranscribeAsync(options, progress, status, log, ct);
        }

        List<string> args;
        string output;
        string? workingDir = null;
        string? tempDir = null;

        switch (Op)
        {
            case ToolOp.Trim:
            {
                TimeParser.TryParse(TrimStart, out double start, allowEmpty: true);
                double? end = TimeParser.TryParse(TrimEnd, out double e, allowEmpty: false) ? e : null;
                output = UniquePath(folder, $"{stem}_trim", AccurateTrim ? ".mp4" : Path.GetExtension(input), input);
                args = ["-ss", TimeParser.ToFfmpeg(start), "-i", input];
                if (end.HasValue) args.AddRange(["-t", TimeParser.ToFfmpeg(end.Value - start)]);
                if (AccurateTrim)
                    args.AddRange(["-map", "0:v:0?", "-map", "0:a?", "-c:v", "libx264", "-preset", "medium", "-crf", "18",
                                   "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"]);
                else
                    args.AddRange(["-map", "0:v?", "-map", "0:a?", "-c", "copy", "-avoid_negative_ts", "make_zero"]);
                args.Add(output);
                total = end.HasValue ? end.Value - start : total - start;
                break;
            }

            case ToolOp.Convert:
            {
                var preset = ConvertPresets[Math.Clamp(ConvertPresetIndex, 0, ConvertPresets.Count - 1)];
                output = UniquePath(folder, stem, preset.Extension, input);
                args = ["-i", input, .. preset.Args, output];
                break;
            }

            case ToolOp.ExtractAudio:
            {
                var preset = AudioPresets[Math.Clamp(AudioPresetIndex, 0, AudioPresets.Count - 1)];
                output = UniquePath(folder, stem, preset.Extension, input);
                args = ["-i", input, .. preset.Args, output];
                break;
            }

            case ToolOp.Normalize:
            {
                double lufs = NormalizeIndex switch { 1 => -16, 2 => -23, _ => -14 };
                string ext = Path.GetExtension(input).ToLowerInvariant();
                var (audioArgs, outExt) = AudioCodecFor(ext);
                output = UniquePath(folder, $"{stem}_norm", outExt, input);
                args = ["-i", input, "-map", "0:v?", "-map", "0:a?", "-c:v", "copy",
                        "-af", $"loudnorm=I={lufs}:TP=-1.5:LRA=11", "-ar", "48000", .. audioArgs, output];
                if (!probe.HasAudio) throw new InvalidOperationException("This file has no audio to normalize.");
                break;
            }

            case ToolOp.BurnSubtitles:
            {
                string? subs = string.IsNullOrWhiteSpace(SubtitleFile) ? FindSidecarSubtitle(input) : SubtitleFile;
                if (subs is null)
                    throw new InvalidOperationException("No subtitle file chosen, and none with the same name was found next to the video.");
                if (!probe.HasVideo) throw new InvalidOperationException("This file has no video to draw subtitles on.");

                // The subtitles filter is picky about Windows paths, so run ffmpeg next to a temp copy.
                tempDir = Path.Combine(Path.GetTempPath(), $"pulsar-subs-{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);
                string subName = "subs" + Path.GetExtension(subs).ToLowerInvariant();
                File.Copy(subs, Path.Combine(tempDir, subName));
                workingDir = tempDir;

                string filter = $"subtitles={subName}";
                int fontSize = SubtitleSizeIndex switch { 1 => 16, 2 => 22, 3 => 30, _ => 0 };
                if (fontSize > 0) filter += $":force_style='FontSize={fontSize}'";

                output = UniquePath(folder, $"{stem}_subs", ".mp4", input);
                args = ["-i", input, "-map", "0:v:0", "-map", "0:a?", "-vf", filter,
                        "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", output];
                break;
            }

            case ToolOp.Transform:
            {
                if (!probe.HasVideo) throw new InvalidOperationException("This file has no video to transform.");
                var filters = new List<string>();
                switch (RotationIndex)
                {
                    case 1: filters.Add("transpose=1"); break;
                    case 2: filters.Add("transpose=2"); break;
                    case 3: filters.Add("transpose=1,transpose=1"); break;
                }
                if (FlipHorizontal) filters.Add("hflip");
                if (FlipVertical) filters.Add("vflip");
                (int w, int h)? aspect = CropIndex switch
                {
                    1 => (16, 9), 2 => (9, 16), 3 => (1, 1), 4 => (4, 3), 5 => (4, 5), _ => null,
                };
                if (aspect is (int aw, int ah))
                    filters.Add($"crop='min(iw,ih*{aw}/{ah})':'min(ih,iw*{ah}/{aw})'");
                int height = SizeIndex switch { 1 => 2160, 2 => 1440, 3 => 1080, 4 => 720, 5 => 480, _ => 0 };
                if (height > 0) filters.Add($"scale=-2:{height}");
                filters.Add("format=yuv420p");

                output = UniquePath(folder, $"{stem}_edit", ".mp4", input);
                args = ["-i", input, "-map", "0:v:0", "-map", "0:a?", "-vf", string.Join(",", filters),
                        "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-c:a", "aac", "-b:a", "192k",
                        "-metadata:s:v:0", "rotate=0", "-movflags", "+faststart", output];
                break;
            }

            default:
                throw new InvalidOperationException("Unknown tool.");
        }

        IsIndeterminate = total is not > 0;
        try
        {
            int code = await FfmpegService.RunAsync(args, total, progress, log, ct, workingDir);
            if (code != 0)
            {
                TryDelete(output);
                throw new InvalidOperationException($"ffmpeg failed (exit code {code}). See the log below.");
            }
            return [output];
        }
        catch (OperationCanceledException)
        {
            TryDelete(output);
            throw;
        }
        finally
        {
            if (tempDir is not null) try { Directory.Delete(tempDir, recursive: true); } catch { /* temp */ }
        }
    }

    private async Task<string> MergeAsync(List<MediaFile> files, Action<double> report, Action<string> log, CancellationToken ct)
    {
        var first = files[0];
        string folder = FolderFor(first);
        Directory.CreateDirectory(folder);
        string name = string.IsNullOrWhiteSpace(MergeName) ? "merged" : string.Join("_", MergeName.Trim().Split(Path.GetInvalidFileNameChars()));

        var probes = new List<MediaProbe>();
        foreach (var f in files) probes.Add(f.Probe ?? await FfmpegService.ProbeAsync(f.FilePath, ct));
        double total = probes.Sum(p => p.Duration ?? 0);
        IsIndeterminate = total <= 0;
        bool video = probes[0].HasVideo;

        List<string> args;
        string output;
        string? listFile = null;

        if (!MergeReencode)
        {
            // Concat demuxer: instant, but every file must share the same codecs and settings.
            listFile = Path.Combine(Path.GetTempPath(), $"pulsar-concat-{Guid.NewGuid():N}.txt");
            var sb = new StringBuilder();
            foreach (var f in files) sb.Append("file '").Append(f.FilePath.Replace("\\", "/").Replace("'", "'\\''")).Append("'\n");
            await File.WriteAllTextAsync(listFile, sb.ToString(), ct);
            output = UniquePath(folder, name, Path.GetExtension(first.FilePath), first.FilePath);
            args = ["-f", "concat", "-safe", "0", "-i", listFile, "-c", "copy", output];
        }
        else if (video)
        {
            if (probes.Any(p => !p.HasAudio))
                throw new InvalidOperationException("Re-encode merge needs every file to have an audio track.");
            int w = probes[0].Width % 2 == 0 ? probes[0].Width : probes[0].Width - 1;
            int h = probes[0].Height % 2 == 0 ? probes[0].Height : probes[0].Height - 1;
            args = [];
            foreach (var f in files) args.AddRange(["-i", f.FilePath]);
            var graph = new StringBuilder();
            for (int i = 0; i < files.Count; i++)
            {
                graph.Append($"[{i}:v:0]scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v{i}];");
                graph.Append($"[{i}:a:0]aresample=48000,aformat=channel_layouts=stereo[a{i}];");
            }
            for (int i = 0; i < files.Count; i++) graph.Append($"[v{i}][a{i}]");
            graph.Append($"concat=n={files.Count}:v=1:a=1[v][a]");
            output = UniquePath(folder, name, ".mp4", first.FilePath);
            args.AddRange(["-filter_complex", graph.ToString(), "-map", "[v]", "-map", "[a]",
                           "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", output]);
        }
        else
        {
            args = [];
            foreach (var f in files) args.AddRange(["-i", f.FilePath]);
            var graph = new StringBuilder();
            for (int i = 0; i < files.Count; i++) graph.Append($"[{i}:a:0]");
            graph.Append($"concat=n={files.Count}:v=0:a=1[a]");
            string ext = Path.GetExtension(first.FilePath).ToLowerInvariant();
            var (audioArgs, outExt) = AudioCodecFor(ext);
            output = UniquePath(folder, name, outExt, first.FilePath);
            args.AddRange(["-filter_complex", graph.ToString(), "-map", "[a]", .. audioArgs, output]);
        }

        try
        {
            int code = await FfmpegService.RunAsync(args, total > 0 ? total : null, new Progress<double>(report), log, ct);
            if (code != 0)
            {
                TryDelete(output);
                throw new InvalidOperationException(MergeReencode
                    ? $"ffmpeg failed (exit code {code}). See the log below."
                    : "Fast merge failed. The files probably use different formats; turn on re-encode and try again.");
            }
            return output;
        }
        catch (OperationCanceledException)
        {
            TryDelete(output);
            throw;
        }
        finally
        {
            if (listFile is not null) TryDelete(listFile);
        }
    }

    /// <summary>Audio codec arguments that suit the container, and the output extension to use.</summary>
    private static (string[] Args, string Extension) AudioCodecFor(string ext) => ext switch
    {
        ".mp3" => (["-c:a", "libmp3lame", "-q:a", "2"], ".mp3"),
        ".flac" => (["-c:a", "flac"], ".flac"),
        ".wav" => (["-c:a", "pcm_s16le"], ".wav"),
        ".opus" or ".ogg" => (["-c:a", "libopus", "-b:a", "160k"], ".opus"),
        ".webm" => (["-c:a", "libopus", "-b:a", "160k"], ".webm"),
        ".m4a" or ".aac" => (["-c:a", "aac", "-b:a", "256k"], ".m4a"),
        ".mp4" or ".mov" or ".m4v" => (["-c:a", "aac", "-b:a", "192k"], ext),
        ".mkv" => (["-c:a", "aac", "-b:a", "192k"], ".mkv"),
        _ => (["-c:a", "aac", "-b:a", "192k"], ".mkv"),
    };

    private static string? FindSidecarSubtitle(string video)
    {
        string dir = Path.GetDirectoryName(video)!;
        string stem = Path.GetFileNameWithoutExtension(video);
        foreach (var ext in new[] { ".srt", ".ass", ".ssa", ".vtt" })
        {
            string exact = Path.Combine(dir, stem + ext);
            if (File.Exists(exact)) return exact;
        }
        // yt-dlp style: "Title [id].en.srt"
        return Directory.EnumerateFiles(dir, stem + ".*")
            .FirstOrDefault(f => f.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase));
    }

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var text = Log + line + Environment.NewLine;
        Log = text.Length > 20_000 ? text[^20_000..] : text;
    }

    private static string UniquePath(string folder, string name, string extension, string input)
    {
        string candidate = Path.Combine(folder, name + extension);
        for (int i = 2; File.Exists(candidate) || string.Equals(candidate, input, StringComparison.OrdinalIgnoreCase); i++)
            candidate = Path.Combine(folder, $"{name} ({i}){extension}");
        return candidate;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* leave partial file */ }
    }
}
