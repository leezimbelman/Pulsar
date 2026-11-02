using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Pulsar.Models;
using Pulsar.Services;

namespace Pulsar.ViewModels;

/// <summary>Channels and playlists that Pulsar checks on a schedule, downloading new uploads automatically.</summary>
public partial class SubscriptionsViewModel : ObservableObject
{
    private const string FileName = "subscriptions.json";
    private DispatcherQueueTimer? _timer;
    private readonly Dictionary<Guid, DownloadJob> _running = [];

    public SubscriptionsViewModel()
    {
        foreach (var sub in JsonStore.Load(FileName, () => new List<Subscription>()))
            Items.Add(sub);
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        _newFolder = App.Settings.Current.DownloadFolder;
        _newPresetIndex = Math.Clamp(App.Settings.Current.DefaultPresetIndex, 0, FormatPreset.All.Count - 1);
    }

    public ObservableCollection<Subscription> Items { get; } = [];
    public bool IsEmpty => Items.Count == 0;
    public IReadOnlyList<FormatPreset> Presets => FormatPreset.All;

    public static readonly int[] Intervals = [1, 3, 6, 12, 24, 168];
    public static readonly int[] ItemCounts = [5, 10, 25, 50];

    // ---- New subscription form ----
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _newUrl = "";

    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string _newFolder;
    [ObservableProperty] private int _newPresetIndex;
    [ObservableProperty] private int _newIntervalIndex = 2;
    [ObservableProperty] private int _newItemsIndex = 1;
    [ObservableProperty] private bool _onlyNew = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private bool _isAdding;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = "";

    public bool HasMessage => Message.Length > 0;

    private bool CanAdd() => !IsAdding && YtDlpService.LooksLikeUrl(NewUrl);

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (await Pickers.PickFolderAsync() is { } folder) NewFolder = folder;
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        if (!ToolPaths.HasYtDlp)
        {
            Message = "Install yt-dlp first (Settings → Components).";
            return;
        }

        var sub = new Subscription
        {
            Url = NewUrl.Trim(),
            Name = string.IsNullOrWhiteSpace(NewName) ? "" : NewName.Trim(),
            Folder = string.IsNullOrWhiteSpace(NewFolder) ? App.Settings.Current.DownloadFolder : NewFolder.Trim(),
            PresetIndex = NewPresetIndex,
            IntervalHours = Intervals[Math.Clamp(NewIntervalIndex, 0, Intervals.Length - 1)],
            MaxItems = ItemCounts[Math.Clamp(NewItemsIndex, 0, ItemCounts.Length - 1)],
        };

        IsAdding = true;
        Message = "";
        try
        {
            if (sub.Name.Length == 0)
            {
                try
                {
                    using var nameCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    var info = await YtDlpService.GetInfoAsync(sub.Url, true, App.Settings.Current.CookiesBrowser, nameCts.Token);
                    sub.Name = info.Uploader ?? info.Title;
                }
                catch
                {
                    sub.Name = new Uri(sub.Url).Host;
                }
            }

            if (OnlyNew)
            {
                Message = "Noting the videos that are already there so only new uploads download…";
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                await YtDlpService.PrimeArchiveAsync(sub.Url, ToolPaths.ArchiveFor(sub.Id), Math.Max(50, sub.MaxItems),
                    App.Settings.Current.CookiesBrowser, cts.Token);
                sub.LastChecked = DateTime.Now;
                sub.LastResult = "watching for new uploads";
            }

            Items.Insert(0, sub);
            Save();
            NewUrl = "";
            NewName = "";
            Message = OnlyNew
                ? $"Subscribed to {sub.Name}. New uploads will download automatically."
                : $"Subscribed to {sub.Name}. Downloading the latest {sub.MaxItems} now.";
            if (!OnlyNew) Check(sub);
        }
        catch (Exception ex)
        {
            Message = $"Couldn't subscribe: {ex.Message}";
        }
        finally
        {
            IsAdding = false;
        }
    }

    // ---- Scheduling ----
    public void Start()
    {
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMinutes(1);
        _timer.Tick += (_, _) => CheckDue();
        _timer.Start();
        CheckDue();
    }

    public void Stop() => _timer?.Stop();

    private void CheckDue()
    {
        if (!ToolPaths.HasYtDlp) return;
        foreach (var sub in Items.Where(s => s.IsDue).ToList()) Check(sub);
        foreach (var sub in Items) sub.RefreshStatus();
    }

    [RelayCommand]
    private void CheckAll()
    {
        foreach (var sub in Items.Where(s => s.Enabled)) Check(sub);
    }

    public void Check(Subscription sub)
    {
        if (_running.TryGetValue(sub.Id, out var existing) && existing.IsActive) return; // still checking

        var settings = App.Settings.Current;
        var preset = FormatPreset.All[Math.Clamp(sub.PresetIndex, 0, FormatPreset.All.Count - 1)];
        var request = new DownloadRequest
        {
            Url = sub.Url,
            FormatArgs = preset.Args,
            AudioOnly = preset.AudioOnly,
            PresetName = preset.Name,
            OutputFolder = sub.Folder,
            FileTemplate = settings.FileTemplate,
            EmbedThumbnail = settings.EmbedThumbnail,
            EmbedMetadata = settings.EmbedMetadata,
            EmbedChapters = settings.EmbedChapters,
            Playlist = true,
            SponsorBlockMode = settings.SponsorBlockMode,
            SponsorBlockCategories = settings.SponsorBlockCategories,
            CookiesBrowser = string.IsNullOrWhiteSpace(settings.CookiesBrowser) ? null : settings.CookiesBrowser,
            RateLimit = string.IsNullOrWhiteSpace(settings.RateLimit) ? null : settings.RateLimit,
            ArchivePath = ToolPaths.ArchiveFor(sub.Id),
            PlaylistItems = $"1:{sub.MaxItems}",
            IgnoreErrors = true,
            SubscriptionName = sub.Name,
        };

        sub.LastChecked = DateTime.Now;
        sub.LastResult = "checking…";
        Save();

        var job = App.Downloads.Enqueue(request, $"{sub.Name}: checking for new videos");
        _running[sub.Id] = job;
        job.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(DownloadJob.Status) || job.IsActive) return;
            sub.LastResult = job.Status switch
            {
                JobStatus.Completed when job.FilesDownloaded == 0 => "no new videos",
                JobStatus.Completed => job.FilesDownloaded == 1 ? "1 new video" : $"{job.FilesDownloaded} new videos",
                JobStatus.Canceled => "canceled",
                _ => "check failed",
            };
            Save();
        };
    }

    public void Remove(Subscription sub)
    {
        Items.Remove(sub);
        try { File.Delete(ToolPaths.ArchiveFor(sub.Id)); } catch { /* fine */ }
        Save();
    }

    public void SetEnabled(Subscription sub, bool enabled)
    {
        sub.Enabled = enabled;
        Save();
    }

    public static void OpenFolder(Subscription sub) => Pickers.ShowInExplorer(sub.Folder, sub.Folder);

    private void Save() => JsonStore.Save(FileName, Items.ToList());
}
