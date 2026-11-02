namespace Pulsar.Services;

public sealed class AppSettings
{
    // Downloads
    public string DownloadFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public string FileTemplate { get; set; } = "%(title)s [%(id)s].%(ext)s";
    public int DefaultPresetIndex { get; set; }
    public int MaxConcurrent { get; set; } = 2;
    public bool EmbedThumbnail { get; set; } = true;
    public bool EmbedMetadata { get; set; } = true;
    public bool EmbedChapters { get; set; } = true;
    public string CookiesBrowser { get; set; } = "";
    public string RateLimit { get; set; } = "";
    /// <summary>0 = off, 1 = mark segments as chapters, 2 = remove segments.</summary>
    public int SponsorBlockMode { get; set; }
    public string SponsorBlockCategories { get; set; } = "sponsor,selfpromo,interaction";
    public bool WatchClipboard { get; set; } = true;

    // yt-dlp updates
    public string UpdateChannel { get; set; } = "stable";
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    // Background, tray and notifications
    public bool MinimizeToTray { get; set; }
    public bool CloseToTray { get; set; }
    public bool NotifyOnComplete { get; set; } = true;

    // Appearance and windows
    /// <summary>0 = follow Windows, 1 = light, 2 = dark.</summary>
    public int Theme { get; set; }
    public int MainX { get; set; } = int.MinValue;
    public int MainY { get; set; } = int.MinValue;
    public int MainWidth { get; set; }
    public int MainHeight { get; set; }
    public bool MainMaximized { get; set; }
    public int PlayerWidth { get; set; }
    public int PlayerHeight { get; set; }
    public int PlayerVolume { get; set; } = 80;

    // Transcription
    /// <summary>0 = local whisper.cpp, 1 = OpenAI-compatible server.</summary>
    public int WhisperEngine { get; set; }
    public string WhisperModel { get; set; } = "base";
    public string WhisperCustomExe { get; set; } = "";
    public string WhisperServerUrl { get; set; } = "";
    public string WhisperServerModel { get; set; } = "whisper-1";
    /// <summary>API key encrypted with Windows DPAPI (current user).</summary>
    public string WhisperApiKeyProtected { get; set; } = "";
}

/// <summary>Settings persisted as JSON in %LOCALAPPDATA%\Pulsar\settings.json.</summary>
public sealed class SettingsService
{
    private const string FileName = "settings.json";

    public AppSettings Current { get; private set; } = new();

    public void Load() => Current = JsonStore.Load(FileName, () => new AppSettings());

    public void Save() => JsonStore.Save(FileName, Current);
}
