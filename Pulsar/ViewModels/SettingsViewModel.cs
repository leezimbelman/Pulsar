using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulsar.Models;
using Pulsar.Services;

namespace Pulsar.ViewModels;

public partial class ToggleItem : ObservableObject
{
    private readonly Action _changed;

    public ToggleItem(string id, string label, bool isChecked, Action changed)
    {
        Id = id;
        Label = label;
        _isChecked = isChecked;
        _changed = changed;
    }

    public string Id { get; }
    public string Label { get; }

    [ObservableProperty] private bool _isChecked;

    partial void OnIsCheckedChanged(bool value) => _changed();
}

public partial class SettingsViewModel : ObservableObject
{
    private static readonly string[] Channels = ["stable", "nightly"];
    private readonly SettingsService _service;
    private bool _loading = true;
    private AppSettings S => _service.Current;

    public SettingsViewModel(SettingsService service)
    {
        _service = service;
        _downloadFolder = S.DownloadFolder;
        _fileTemplate = S.FileTemplate;
        _defaultPresetIndex = Math.Clamp(S.DefaultPresetIndex, 0, FormatPreset.All.Count - 1);
        _maxConcurrent = S.MaxConcurrent;
        _embedThumbnail = S.EmbedThumbnail;
        _embedMetadata = S.EmbedMetadata;
        _embedChapters = S.EmbedChapters;
        _watchClipboard = S.WatchClipboard;
        _cookiesIndex = Math.Max(0, Array.FindIndex(YtDlpService.Browsers, b => b.Id == S.CookiesBrowser));
        _rateLimit = S.RateLimit;
        _sponsorBlockMode = Math.Clamp(S.SponsorBlockMode, 0, 2);
        _checkForUpdatesOnStartup = S.CheckForUpdatesOnStartup;
        _updateChannelIndex = Math.Max(0, Array.IndexOf(Channels, S.UpdateChannel));
        _themeIndex = Math.Clamp(S.Theme, 0, 2);
        _minimizeToTray = S.MinimizeToTray;
        _closeToTray = S.CloseToTray;
        _notifyOnComplete = S.NotifyOnComplete;
        _whisperEngineIndex = Math.Clamp(S.WhisperEngine, 0, 1);
        _whisperModelIndex = Math.Max(0, Array.FindIndex(WhisperService.Models, m => m.Id == S.WhisperModel));
        _whisperCustomExe = S.WhisperCustomExe;
        _whisperServerUrl = S.WhisperServerUrl;
        _whisperServerModel = S.WhisperServerModel;
        _whisperApiKey = Secret.Unprotect(S.WhisperApiKeyProtected);
        _hasYtDlp = ToolPaths.HasYtDlp;
        _hasFfmpeg = ToolPaths.HasFfmpeg;

        try
        {
            _startWithWindows = ShellIntegration.StartsWithWindows;
            _sendToEnabled = ShellIntegration.IsSendToInstalled;
        }
        catch
        {
            // Registry or shell folder unavailable.
        }

        var chosen = S.SponsorBlockCategories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        SponsorCategories = YtDlpService.SponsorBlockCategories
            .Select(c => new ToggleItem(c.Id, c.Label, chosen.Contains(c.Id), SaveSponsorCategories))
            .ToList();

        RefreshWhisperStatus();
        _loading = false;
    }

    public IReadOnlyList<FormatPreset> Presets => FormatPreset.All;
    public IReadOnlyList<string> BrowserNames { get; } = YtDlpService.Browsers.Select(b => b.Label).ToList();
    public IReadOnlyList<string> WhisperModelNames { get; } = WhisperService.Models.Select(m => m.Label).ToList();
    public IReadOnlyList<ToggleItem> SponsorCategories { get; }
    public string BinFolder => ToolPaths.Bin;
    public string AppDataFolder => ToolPaths.AppData;
    public string VersionText => $"Pulsar {typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";
    private string Channel => Channels[Math.Clamp(UpdateChannelIndex, 0, Channels.Length - 1)];

    private void Save()
    {
        if (!_loading) _service.Save();
    }

    // ---- Appearance ----
    [ObservableProperty] private int _themeIndex;

    partial void OnThemeIndexChanged(int value)
    {
        if (value < 0) return;
        S.Theme = value;
        Save();
        App.ApplyTheme();
    }

    // ---- Downloads (auto-saved) ----
    [ObservableProperty] private string _downloadFolder;
    [ObservableProperty] private string _fileTemplate;
    [ObservableProperty] private int _defaultPresetIndex;
    [ObservableProperty] private double _maxConcurrent;
    [ObservableProperty] private bool _embedThumbnail;
    [ObservableProperty] private bool _embedMetadata;
    [ObservableProperty] private bool _embedChapters;
    [ObservableProperty] private bool _watchClipboard;
    [ObservableProperty] private int _cookiesIndex;
    [ObservableProperty] private int _sponsorBlockMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RateLimitError), nameof(HasRateLimitError))]
    private string _rateLimit;

    public bool HasRateLimitError => !YtDlpService.IsValidRate(RateLimit);
    public string RateLimitError => HasRateLimitError ? "Use a number with an optional K, M or G, like 500K or 5M." : "";

    partial void OnDownloadFolderChanged(string value) { S.DownloadFolder = value; Save(); }
    partial void OnFileTemplateChanged(string value)
    {
        S.FileTemplate = string.IsNullOrWhiteSpace(value) ? "%(title)s [%(id)s].%(ext)s" : value;
        Save();
    }
    partial void OnDefaultPresetIndexChanged(int value) { if (value >= 0) { S.DefaultPresetIndex = value; Save(); } }
    partial void OnMaxConcurrentChanged(double value)
    {
        S.MaxConcurrent = (int)Math.Clamp(double.IsNaN(value) ? 2 : value, 1, 8);
        Save();
    }
    partial void OnEmbedThumbnailChanged(bool value) { S.EmbedThumbnail = value; Save(); }
    partial void OnEmbedMetadataChanged(bool value) { S.EmbedMetadata = value; Save(); }
    partial void OnEmbedChaptersChanged(bool value) { S.EmbedChapters = value; Save(); }
    partial void OnWatchClipboardChanged(bool value) { S.WatchClipboard = value; Save(); }
    partial void OnCookiesIndexChanged(int value)
    {
        if (value < 0) return;
        S.CookiesBrowser = YtDlpService.Browsers[Math.Clamp(value, 0, YtDlpService.Browsers.Length - 1)].Id;
        Save();
    }
    partial void OnSponsorBlockModeChanged(int value) { if (value >= 0) { S.SponsorBlockMode = value; Save(); } }
    partial void OnRateLimitChanged(string value)
    {
        if (!YtDlpService.IsValidRate(value)) return;
        S.RateLimit = value.Trim();
        Save();
    }

    private void SaveSponsorCategories()
    {
        S.SponsorBlockCategories = string.Join(",", SponsorCategories.Where(c => c.IsChecked).Select(c => c.Id));
        Save();
    }

    [RelayCommand]
    private async Task BrowseDownloadFolderAsync()
    {
        if (await Pickers.PickFolderAsync() is { } folder) DownloadFolder = folder;
    }

    // ---- Background & notifications ----
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _notifyOnComplete;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _sendToEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShellError))]
    private string _shellError = "";

    public bool HasShellError => ShellError.Length > 0;

    partial void OnMinimizeToTrayChanged(bool value) { S.MinimizeToTray = value; Save(); App.UpdateTray(); }
    partial void OnCloseToTrayChanged(bool value) { S.CloseToTray = value; Save(); App.UpdateTray(); }
    partial void OnNotifyOnCompleteChanged(bool value) { S.NotifyOnComplete = value; Save(); }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading) return;
        try
        {
            ShellIntegration.StartsWithWindows = value;
            ShellError = "";
            App.UpdateTray();
        }
        catch (Exception ex)
        {
            ShellError = $"Couldn't change the startup setting: {ex.Message}";
        }
    }

    partial void OnSendToEnabledChanged(bool value)
    {
        if (_loading) return;
        try
        {
            if (value) ShellIntegration.InstallSendTo();
            else ShellIntegration.RemoveSendTo();
            ShellError = "";
        }
        catch (Exception ex)
        {
            ShellError = $"Couldn't update the Send to menu: {ex.Message}";
        }
    }

    // ---- Transcription ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalWhisper), nameof(IsServerWhisper))]
    private int _whisperEngineIndex;

    [ObservableProperty] private int _whisperModelIndex;
    [ObservableProperty] private string _whisperCustomExe;
    [ObservableProperty] private string _whisperServerUrl;
    [ObservableProperty] private string _whisperServerModel;
    [ObservableProperty] private string _whisperApiKey;
    [ObservableProperty] private string _whisperStatus = "";

    public bool IsLocalWhisper => WhisperEngineIndex == 0;
    public bool IsServerWhisper => WhisperEngineIndex == 1;
    private string SelectedModelId => WhisperService.Models[Math.Clamp(WhisperModelIndex, 0, WhisperService.Models.Length - 1)].Id;

    partial void OnWhisperEngineIndexChanged(int value)
    {
        if (value < 0) return;
        S.WhisperEngine = value;
        Save();
        App.Tools.RefreshEngineSummary();
    }

    partial void OnWhisperModelIndexChanged(int value)
    {
        if (value < 0) return;
        S.WhisperModel = SelectedModelId;
        Save();
        RefreshWhisperStatus();
        if (!_loading) App.Tools.RefreshEngineSummary();
    }

    partial void OnWhisperCustomExeChanged(string value) { S.WhisperCustomExe = value.Trim(); Save(); RefreshWhisperStatus(); }
    partial void OnWhisperServerUrlChanged(string value) { S.WhisperServerUrl = value.Trim(); Save(); if (!_loading) App.Tools.RefreshEngineSummary(); }
    partial void OnWhisperServerModelChanged(string value) { S.WhisperServerModel = value.Trim(); Save(); }
    partial void OnWhisperApiKeyChanged(string value)
    {
        try { S.WhisperApiKeyProtected = Secret.Protect(value.Trim()); } catch { S.WhisperApiKeyProtected = ""; }
        Save();
    }

    private void RefreshWhisperStatus()
    {
        bool engine = WhisperService.HasEngine;
        bool model = WhisperService.HasModel(SelectedModelId);
        WhisperStatus = (engine, model) switch
        {
            (true, true) => "Ready: whisper.cpp and this model are installed.",
            (true, false) => "whisper.cpp is installed. This model downloads the first time you transcribe, or now.",
            (false, true) => "Model installed. whisper.cpp downloads the first time you transcribe, or now.",
            _ => "Nothing downloaded yet. Pulsar fetches whisper.cpp and the model the first time you transcribe, or now.",
        };
    }

    [RelayCommand]
    private Task DownloadWhisperAsync() => RunBusyAsync("Downloading whisper.cpp and the speech model…", indeterminate: false, async p =>
    {
        if (!WhisperService.HasEngine) await WhisperService.InstallEngineAsync(p);
        if (!WhisperService.HasModel(SelectedModelId)) await WhisperService.DownloadModelAsync(SelectedModelId, p);
    });

    [RelayCommand]
    private async Task BrowseWhisperExeAsync()
    {
        if (await Pickers.PickFileAsync(0, ".exe") is { } exe) WhisperCustomExe = exe;
    }

    // ---- Components ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsSetup), nameof(YtDlpInstallLabel))]
    private bool _hasYtDlp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsSetup), nameof(FfmpegInstallLabel))]
    private bool _hasFfmpeg;

    [ObservableProperty] private string _ytDlpVersion = "Not installed";
    [ObservableProperty] private string _ffmpegVersion = "Not installed";
    [ObservableProperty] private string _latestVersion = "";
    [ObservableProperty] private bool _checkForUpdatesOnStartup;
    [ObservableProperty] private int _updateChannelIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateMessage))]
    private bool _updateAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    [ObservableProperty] private double _toolProgress;
    [ObservableProperty] private bool _toolProgressIndeterminate;
    [ObservableProperty] private string _toolStatus = "";
    [ObservableProperty] private string _toolLog = "";

    public bool IsIdle => !IsBusy;
    public bool NeedsSetup => !HasYtDlp || !HasFfmpeg;
    public string YtDlpInstallLabel => HasYtDlp ? "Reinstall" : "Install";
    public string FfmpegInstallLabel => HasFfmpeg ? "Reinstall" : "Install";
    public string UpdateMessage => $"yt-dlp {LatestVersion} is available. You have {YtDlpVersion}.";

    partial void OnCheckForUpdatesOnStartupChanged(bool value) { S.CheckForUpdatesOnStartup = value; Save(); }
    partial void OnUpdateChannelIndexChanged(int value)
    {
        if (value < 0) return;
        S.UpdateChannel = Channel;
        Save();
        UpdateAvailable = false;
        LatestVersion = "";
    }

    /// <summary>Screenshot mode: show installed tools without touching the disk or network.</summary>
    public void SetDemoVersions(string ytDlp, string ffmpeg)
    {
        HasYtDlp = true;
        HasFfmpeg = true;
        YtDlpVersion = ytDlp;
        LatestVersion = ytDlp;
        FfmpegVersion = ffmpeg;
        ToolStatus = $"yt-dlp is up to date ({ytDlp}).";
        WhisperStatus = "Ready: whisper.cpp and this model are installed.";
    }

    public async Task RefreshVersionsAsync()
    {
        HasYtDlp = ToolPaths.HasYtDlp;
        HasFfmpeg = ToolPaths.HasFfmpeg;
        try
        {
            YtDlpVersion = HasYtDlp ? await UpdateService.GetYtDlpVersionAsync() ?? "Unknown" : "Not installed";
            FfmpegVersion = HasFfmpeg ? await UpdateService.GetFfmpegVersionAsync() ?? "Unknown" : "Not installed";
        }
        catch (Exception ex)
        {
            ToolStatus = $"Couldn't read tool versions: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        await RefreshVersionsAsync();
        if (!HasYtDlp) return;
        try
        {
            ToolStatus = "Checking for yt-dlp updates…";
            LatestVersion = await UpdateService.GetLatestYtDlpVersionAsync(Channel) ?? "";
            UpdateAvailable = LatestVersion.Length > 0 &&
                              !string.Equals(LatestVersion, YtDlpVersion, StringComparison.OrdinalIgnoreCase);
            ToolStatus = UpdateAvailable ? UpdateMessage : $"yt-dlp is up to date ({YtDlpVersion}).";
        }
        catch (Exception ex)
        {
            ToolStatus = $"Update check failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task UpdateYtDlpAsync()
    {
        if (!ToolPaths.HasYtDlp)
        {
            await InstallYtDlpAsync();
            return;
        }

        await RunBusyAsync($"Updating yt-dlp ({Channel})…", indeterminate: true, async _ =>
        {
            IProgress<string> log = new Progress<string>(AppendLog);
            int code = await UpdateService.SelfUpdateAsync(Channel, log.Report);
            if (code != 0) throw new InvalidOperationException($"yt-dlp's updater exited with code {code}. See the log.");
        });
        UpdateAvailable = false;
    }

    [RelayCommand]
    private Task InstallYtDlpAsync() =>
        RunBusyAsync("Downloading yt-dlp…", indeterminate: false, p => UpdateService.InstallYtDlpAsync(p));

    [RelayCommand]
    private Task InstallFfmpegAsync() =>
        RunBusyAsync("Downloading ffmpeg (about 130 MB)…", indeterminate: false, p => UpdateService.InstallFfmpegAsync(p));

    [RelayCommand]
    private async Task InstallMissingAsync()
    {
        if (!ToolPaths.HasYtDlp) await InstallYtDlpAsync();
        if (!ToolPaths.HasFfmpeg) await InstallFfmpegAsync();
    }

    [RelayCommand]
    private void OpenBinFolder() => Process.Start("explorer.exe", $"\"{ToolPaths.Bin}\"");

    [RelayCommand]
    private void OpenDataFolder() => Process.Start("explorer.exe", $"\"{ToolPaths.AppData}\"");

    private async Task RunBusyAsync(string status, bool indeterminate, Func<IProgress<double>, Task> work)
    {
        if (IsBusy) return;
        IsBusy = true;
        ToolProgress = 0;
        ToolProgressIndeterminate = indeterminate;
        ToolStatus = status;
        AppendLog(status);
        bool ok = false;
        try
        {
            await work(new Progress<double>(v => ToolProgress = v));
            ok = true;
        }
        catch (Exception ex)
        {
            ToolStatus = $"Failed: {ex.Message}";
            AppendLog(ToolStatus);
        }
        finally
        {
            IsBusy = false;
            await RefreshVersionsAsync();
            RefreshWhisperStatus();
            App.Tools.RefreshEngineSummary();
        }

        if (ok)
        {
            ToolStatus = $"Done. yt-dlp {YtDlpVersion}, ffmpeg {FfmpegVersion}.";
            AppendLog(ToolStatus);
        }
    }

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var text = ToolLog + line.Trim() + Environment.NewLine;
        ToolLog = text.Length > 20_000 ? text[^20_000..] : text;
    }
}
