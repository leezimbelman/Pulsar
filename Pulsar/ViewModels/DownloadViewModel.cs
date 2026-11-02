using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Pulsar.Models;
using Pulsar.Services;

namespace Pulsar.ViewModels;

public partial class DownloadViewModel : ObservableObject
{
    private readonly SemaphoreSlim _gate;
    private string? _lastClipboardText;

    public DownloadViewModel(AppSettings settings)
    {
        _gate = new SemaphoreSlim(Math.Clamp(settings.MaxConcurrent, 1, 8));
        _outputFolder = settings.DownloadFolder;
        _embedThumbnail = settings.EmbedThumbnail;
        _embedMetadata = settings.EmbedMetadata;
        _embedChapters = settings.EmbedChapters;
        _sponsorBlockIndex = Math.Clamp(settings.SponsorBlockMode, 0, 2);
        _selectedPresetIndex = Math.Clamp(settings.DefaultPresetIndex, 0, FormatPreset.All.Count - 1);
        Jobs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoJobs));
    }

    /// <summary>Raised on the UI thread when a job completes, fails or is canceled.</summary>
    public event Action<DownloadJob>? JobFinished;

    /// <summary>Raised on the UI thread for every file a job saves (playlists raise it once per video).</summary>
    public event Action<DownloadJob, string, string?>? FileDownloaded;

    public IReadOnlyList<FormatPreset> Presets => FormatPreset.All;
    public ObservableCollection<FormatOption> Formats { get; } = [new("Use preset", null, false)];
    public ObservableCollection<DownloadJob> Jobs { get; } = [];
    public bool HasNoJobs => Jobs.Count == 0;
    public bool HasFormats => Formats.Count > 1;

    // ---- Input ----
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddToQueueCommand), nameof(FetchInfoCommand))]
    private string _url = "";

    [ObservableProperty] private int _selectedPresetIndex;
    [ObservableProperty] private int _selectedFormatIndex;
    [ObservableProperty] private string _outputFolder;
    [ObservableProperty] private bool? _embedThumbnail;
    [ObservableProperty] private bool? _embedMetadata;
    [ObservableProperty] private bool? _embedChapters;
    [ObservableProperty] private bool? _splitChapters = false;
    [ObservableProperty] private bool? _subtitles = false;
    [ObservableProperty] private bool? _playlist = false;
    [ObservableProperty] private int _sponsorBlockIndex;
    [ObservableProperty] private string _sectionStart = "";
    [ObservableProperty] private string _sectionEnd = "";

    // ---- Preview ----
    [ObservableProperty] private bool _isFetching;
    [ObservableProperty] private BitmapImage? _thumbnail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInfo), nameof(InfoTitle), nameof(InfoSubtitle))]
    private VideoInfo? _info;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _error = "";

    public bool HasInfo => Info is not null;
    public bool HasError => Error.Length > 0;
    public string InfoTitle => Info?.Title ?? "";
    public string InfoSubtitle => Info switch
    {
        null => "",
        { IsPlaylist: true } i => $"Playlist   •   {i.EntryCount} videos" + (i.Uploader is null ? "" : $"   •   {i.Uploader}"),
        var i => string.Join("   •   ", new[] { i.Uploader, i.Duration }.Where(s => !string.IsNullOrEmpty(s))),
    };

    // ---- Clipboard watch ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClipboardSuggestion))]
    private string _clipboardUrl = "";

    public bool HasClipboardSuggestion => ClipboardUrl.Length > 0;

    partial void OnUrlChanged(string value)
    {
        Info = null;
        Thumbnail = null;
        Error = "";
        ResetFormats();
        if (string.Equals(value.Trim(), ClipboardUrl, StringComparison.Ordinal)) ClipboardUrl = "";
    }

    private void ResetFormats()
    {
        while (Formats.Count > 1) Formats.RemoveAt(Formats.Count - 1);
        SelectedFormatIndex = 0;
        OnPropertyChanged(nameof(HasFormats));
    }

    private bool HasUrl() => !string.IsNullOrWhiteSpace(Url);

    /// <summary>Called when Pulsar gets focus or the clipboard changes: offers to download a copied link.</summary>
    public async Task CheckClipboardAsync()
    {
        if (!App.Settings.Current.WatchClipboard) return;
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text)) return;
            string text = (await content.GetTextAsync()).Trim();
            if (text == _lastClipboardText) return;
            _lastClipboardText = text;

            if (YtDlpService.LooksLikeUrl(text) && text != Url.Trim() && Jobs.All(j => j.Request.Url != text))
                ClipboardUrl = text;
        }
        catch
        {
            // Clipboard busy or holding something we can't read.
        }
    }

    [RelayCommand]
    private async Task UseClipboardAsync()
    {
        if (!HasClipboardSuggestion) return;
        Url = ClipboardUrl;
        ClipboardUrl = "";
        await FetchInfoCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void QueueClipboard()
    {
        if (!HasClipboardSuggestion) return;
        Url = ClipboardUrl;
        ClipboardUrl = "";
        AddToQueue();
    }

    [RelayCommand]
    private void DismissClipboard() => ClipboardUrl = "";

    [RelayCommand]
    private async Task PasteAsync()
    {
        var content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Text)) return;
        Url = (await content.GetTextAsync()).Trim();
        if (HasUrl()) await FetchInfoCommand.ExecuteAsync(null);
    }

    [RelayCommand(CanExecute = nameof(HasUrl))]
    private async Task FetchInfoAsync()
    {
        if (!ToolPaths.HasYtDlp)
        {
            Error = "yt-dlp isn't installed yet. Use the Install now button above, or go to Settings.";
            return;
        }

        string url = Url.Trim();
        IsFetching = true;
        Error = "";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            var info = await YtDlpService.GetInfoAsync(url, Playlist == true, App.Settings.Current.CookiesBrowser, cts.Token);
            if (url != Url.Trim()) return; // URL changed while we were fetching

            ResetFormats();
            foreach (var f in info.Formats.Skip(1)) Formats.Add(f);
            OnPropertyChanged(nameof(HasFormats));

            Info = info;
            Thumbnail = info.Thumbnail is { } t && Uri.TryCreate(t, UriKind.Absolute, out var uri) ? new BitmapImage(uri) : null;
        }
        catch (OperationCanceledException)
        {
            Error = "Timed out looking up the video.";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsFetching = false;
        }
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (await Pickers.PickFolderAsync() is { } folder) OutputFolder = folder;
    }

    [RelayCommand(CanExecute = nameof(HasUrl))]
    private void AddToQueue()
    {
        if (!TimeParser.TryParse(SectionStart, out var start, allowEmpty: true) ||
            !TimeParser.TryParse(SectionEnd, out var end, allowEmpty: true) ||
            (!string.IsNullOrWhiteSpace(SectionEnd) && end <= start))
        {
            Error = "Clip times must look like 90, 1:30 or 1:02:03, and the end must be after the start.";
            return;
        }
        if (string.IsNullOrWhiteSpace(OutputFolder))
        {
            Error = "Choose a folder to save to.";
            return;
        }

        var settings = App.Settings.Current;
        var preset = Presets[Math.Clamp(SelectedPresetIndex, 0, Presets.Count - 1)];
        var format = SelectedFormatIndex > 0 && SelectedFormatIndex < Formats.Count ? Formats[SelectedFormatIndex] : null;
        bool customFormat = format?.FormatSpec is not null;

        var request = new DownloadRequest
        {
            Url = Url.Trim(),
            FormatArgs = customFormat ? ["-f", format!.FormatSpec!] : preset.Args,
            AudioOnly = customFormat ? format!.AudioOnly : preset.AudioOnly,
            PresetName = customFormat ? "Custom format" : preset.Name,
            OutputFolder = OutputFolder.Trim(),
            FileTemplate = settings.FileTemplate,
            EmbedThumbnail = EmbedThumbnail == true,
            EmbedMetadata = EmbedMetadata == true,
            EmbedChapters = EmbedChapters == true,
            SplitChapters = SplitChapters == true,
            Subtitles = Subtitles == true,
            Playlist = Playlist == true,
            SectionStart = NullIfBlank(SectionStart),
            SectionEnd = NullIfBlank(SectionEnd),
            SponsorBlockMode = SponsorBlockIndex,
            SponsorBlockCategories = settings.SponsorBlockCategories,
            CookiesBrowser = NullIfBlank(settings.CookiesBrowser),
            RateLimit = NullIfBlank(settings.RateLimit),
        };

        Enqueue(request, Info?.Title);

        Url = "";
        SectionStart = "";
        SectionEnd = "";
    }

    /// <summary>Adds a request to the queue and starts it when a slot is free.</summary>
    public DownloadJob Enqueue(DownloadRequest request, string? title)
    {
        var job = new DownloadJob(request, title);
        Jobs.Insert(0, job);
        _ = RunJobAsync(job);
        return job;
    }

    [RelayCommand]
    private void ClearFinished()
    {
        foreach (var job in Jobs.Where(j => j.IsFinished).ToList()) Jobs.Remove(job);
    }

    public void CancelAll()
    {
        foreach (var job in Jobs.Where(j => j.IsActive)) job.Cts?.Cancel();
    }

    // ---- Per-item actions (called from the page) ----
    public void Cancel(DownloadJob job) => job.Cts?.Cancel();

    public void Retry(DownloadJob job)
    {
        job.ResetForRetry();
        _ = RunJobAsync(job);
    }

    public void Remove(DownloadJob job)
    {
        if (job.IsActive) job.Cts?.Cancel();
        Jobs.Remove(job);
    }

    public static void ShowInFolder(DownloadJob job) => Pickers.ShowInExplorer(job.OutputPath, job.Request.OutputFolder);

    // ---- Queue runner ----
    private async Task RunJobAsync(DownloadJob job)
    {
        job.Cts = new CancellationTokenSource();
        var ct = job.Cts.Token;
        try
        {
            await _gate.WaitAsync(ct);
            try
            {
                if (!ToolPaths.HasYtDlp) throw new InvalidOperationException("yt-dlp isn't installed. Install it from Settings.");
                Directory.CreateDirectory(job.Request.OutputFolder);

                job.Status = JobStatus.Running;
                // Progress<T> captures the UI SynchronizationContext, so Apply runs on the UI thread.
                var progress = new Progress<DownloadUpdate>(u => Apply(job, u));
                int code = await YtDlpService.DownloadAsync(job.Request, progress, ct);
                await Task.Yield(); // let any queued progress callbacks land first

                // With --ignore-errors (subscriptions) a partial success still counts.
                if (code == 0 || (job.Request.IgnoreErrors && job.FilesDownloaded > 0))
                {
                    job.Percent = 100;
                    job.IsIndeterminate = false;
                    job.Status = JobStatus.Completed;
                }
                else
                {
                    job.LastMessage = job.LastError ?? (job.LastMessage.Length > 0 ? job.LastMessage : $"yt-dlp exited with code {code}.");
                    job.Status = JobStatus.Failed;
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            job.Status = JobStatus.Canceled;
        }
        catch (Exception ex)
        {
            job.LastMessage = ex.Message;
            job.Status = JobStatus.Failed;
        }

        JobFinished?.Invoke(job);
    }

    private void Apply(DownloadJob job, DownloadUpdate u)
    {
        if (!job.IsActive) return;

        if (u.Title is { Length: > 0 } title) job.Title = title;
        if (u.FilePath is { Length: > 0 } path)
        {
            job.OutputPath = path;
            job.FilesDownloaded++;
            FileDownloaded?.Invoke(job, path, u.FileUrl);
        }

        if (u.Percent is double pct)
        {
            job.IsIndeterminate = false;
            job.Percent = pct;
            job.Speed = u.Speed ?? "";
            job.Eta = u.Eta ?? "";
            if (job.Status == JobStatus.Processing) job.Status = JobStatus.Running; // next playlist item
        }
        else if (u.Speed is not null)
        {
            // Size unknown (some live/HLS streams): keep the bar indeterminate but show speed.
            job.IsIndeterminate = true;
            job.LastMessage = $"Downloading…   {u.Speed}";
        }

        if (u.Processing)
        {
            job.Status = JobStatus.Processing;
            job.IsIndeterminate = true;
            job.LastMessage = u.Message ?? "";
        }
        else if (u.IsError)
        {
            job.LastError = u.Message;
            job.LastMessage = u.Message ?? "";
        }
        else if (u.Message is { Length: > 0 } msg && u.Percent is null && u.Speed is null)
        {
            job.LastMessage = msg;
        }
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
