using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Pulsar.Models;
using Pulsar.Services;
using Pulsar.ViewModels;

namespace Pulsar;

public partial class App : Application
{
    public static SettingsService Settings { get; } = new();
    public static HistoryService History { get; } = new();
    public static MainWindow MainWindow { get; private set; } = null!;
    public static DownloadViewModel Downloads { get; private set; } = null!;
    public static ToolsViewModel Tools { get; private set; } = null!;
    public static SettingsViewModel SettingsVM { get; private set; } = null!;
    public static HistoryViewModel HistoryVM { get; private set; } = null!;
    public static SubscriptionsViewModel Subscriptions { get; private set; } = null!;

    public static bool IsExiting { get; private set; }
    public static bool IsMainWindowActive => MainWindow?.IsActive == true;
    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);

    private static DispatcherQueue? _dispatcher;
    private static PlayerWindow? _player;
    private static TrayIcon? _tray;
    private static bool _startedHidden;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            CrashLog.Write(e.Exception);
            e.Handled = true; // keep running; most UI exceptions aren't fatal
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        var startupArgs = Environment.GetCommandLineArgs().Skip(1).ToList();

        int shotIndex = startupArgs.FindIndex(a => a.Equals("--screenshots", StringComparison.OrdinalIgnoreCase));
        if (shotIndex >= 0)
        {
            string dir = shotIndex + 1 < startupArgs.Count ? startupArgs[shotIndex + 1] : Path.Combine(Environment.CurrentDirectory, "screenshots");
            LaunchForScreenshots(dir);
            return;
        }

        Settings.Load();
        History.Load();
        NotificationService.Initialize();

        Tools = new ToolsViewModel();
        SettingsVM = new SettingsViewModel(Settings);
        Downloads = new DownloadViewModel(Settings.Current);
        HistoryVM = new HistoryViewModel(History);
        Subscriptions = new SubscriptionsViewModel();
        Downloads.FileDownloaded += OnFileDownloaded;
        Downloads.JobFinished += OnJobFinished;

        MainWindow = new MainWindow();
        ApplyTheme();

        bool fromNotification = false;
        try
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            fromNotification = activation.Kind == ExtendedActivationKind.AppNotification;
        }
        catch
        {
            // Not available; treat as a normal launch.
        }

        _startedHidden = startupArgs.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase)) && !fromNotification;
        UpdateTray();
        if (!_startedHidden) MainWindow.Activate();

        HandleArguments(startupArgs);

        if (Settings.Current.CheckForUpdatesOnStartup && ToolPaths.HasYtDlp)
            _ = SettingsVM.CheckForUpdatesCommand.ExecuteAsync(null);
        else
            _ = SettingsVM.RefreshVersionsAsync();

        Subscriptions.Start();
    }

    /// <summary>
    /// Screenshot mode (used by the Screenshots workflow): sample content, no disk writes,
    /// no network, no tray or notifications. Captures each page in dark and light, then exits.
    /// </summary>
    private void LaunchForScreenshots(string outputFolder)
    {
        JsonStore.ReadOnly = true;
        Settings.Current.DownloadFolder = @"C:\Users\you\Downloads";
        Settings.Current.WatchClipboard = false;
        Tools = new ToolsViewModel();
        SettingsVM = new SettingsViewModel(Settings);
        Downloads = new DownloadViewModel(Settings.Current);
        HistoryVM = new HistoryViewModel(History);
        Subscriptions = new SubscriptionsViewModel();

        MainWindow = new MainWindow();
        MainWindow.Activate();
        _ = ScreenshotRunner.RunAsync(outputFolder);
    }

    public static void Dispatch(Action action) => _dispatcher?.TryEnqueue(() => action());

    // ---------- Activation & arguments ----------

    /// <summary>Another copy of Pulsar was started (Send to, startup shortcut, notification click) and forwarded here.</summary>
    public static void OnRedirectedActivation(AppActivationArguments e)
    {
        List<string> args = [];
        try
        {
            if (e.Kind == ExtendedActivationKind.Launch && e.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch)
            {
                args = ShellIntegration.SplitCommandLine(launch.Arguments).ToList();
                // Unpackaged apps receive the full command line; drop the program path if it's there.
                if (args.Count > 0 && args[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) args.RemoveAt(0);
            }
        }
        catch
        {
            // Unknown activation; just show the window.
        }

        Dispatch(() =>
        {
            bool onlyPlay = args.Count > 0 && (args[0].Equals("--play", StringComparison.OrdinalIgnoreCase) || File.Exists(args[0]));
            bool onlyTray = args.Count == 1 && args[0].Equals("--tray", StringComparison.OrdinalIgnoreCase);
            if (!onlyPlay && !onlyTray) ShowMainWindow();
            HandleArguments(args);
        });
    }

    /// <summary>
    /// Command line: --play FILE, --tools FILE..., --url URL, --tray, or a bare URL or file.
    /// </summary>
    public static void HandleArguments(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--tray":
                    break;
                case "--play":
                    OpenPlayer(i + 1 < args.Count ? args[++i] : null);
                    break;
                case "--tools":
                    Tools.AddFiles(args.Skip(i + 1).Where(File.Exists));
                    ShowMainWindow("tools");
                    return;
                case "--url":
                    if (i + 1 < args.Count) QueueUrl(args[++i]);
                    break;
                default:
                    if (YtDlpService.LooksLikeUrl(a)) QueueUrl(a);
                    else if (File.Exists(a)) OpenPlayer(a);
                    break;
            }
        }
    }

    public static void HandleNotification(IDictionary<string, string> arguments)
    {
        arguments.TryGetValue("action", out var action);
        arguments.TryGetValue("path", out var path);
        switch (action)
        {
            case "play" when path is not null:
                OpenPlayer(path);
                break;
            case "folder" when path is not null:
                Pickers.ShowInExplorer(path, Path.GetDirectoryName(path));
                break;
            default:
                ShowMainWindow();
                break;
        }
    }

    public static void QueueUrl(string url)
    {
        ShowMainWindow("download");
        Downloads.Url = url;
        _ = Downloads.FetchInfoCommand.ExecuteAsync(null);
    }

    // ---------- Windows ----------

    public static void ShowMainWindow(string? page = null)
    {
        MainWindow.ShowWindow();
        if (page is not null) MainWindow.Navigate(page);
    }

    /// <summary>Opens (or reuses) the player window, optionally loading a file.</summary>
    public static void OpenPlayer(string? path = null)
    {
        if (_player is null)
        {
            _player = new PlayerWindow();
            _player.Closed += (_, _) => _player = null;
            _player.ApplyTheme(CurrentTheme);
        }
        if (path is not null && File.Exists(path)) _player.Open(path);
        _player.Activate();
    }

    public static void ClosePlayer() => _player?.Close();

    /// <summary>Called from the player: load the file into the Trim tool with the chosen in/out points.</summary>
    public static void SendToTrim(string path, double startSeconds, double? endSeconds)
    {
        Tools.SetupTrim(path, startSeconds, endSeconds);
        ShowMainWindow("tools");
    }

    public static ElementTheme CurrentTheme => Settings.Current.Theme switch
    {
        1 => ElementTheme.Light,
        2 => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    public static void ApplyTheme()
    {
        MainWindow?.ApplyTheme(CurrentTheme);
        _player?.ApplyTheme(CurrentTheme);
    }

    // ---------- Tray ----------

    public static bool WantsTray =>
        Settings.Current.MinimizeToTray || Settings.Current.CloseToTray || _startedHidden || SafeStartsWithWindows();

    private static bool SafeStartsWithWindows()
    {
        try { return ShellIntegration.StartsWithWindows; } catch { return false; }
    }

    public static void UpdateTray()
    {
        if (MainWindow is null) return;
        if (WantsTray && _tray is null)
        {
            try
            {
                _tray = new TrayIcon(WindowHandle, ToolPaths.AppIcon, "Pulsar");
                _tray.AddMenuItem("Open Pulsar", () => ShowMainWindow());
                _tray.AddMenuItem("Open player", () => OpenPlayer());
                _tray.AddMenuItem(null);
                _tray.AddMenuItem("Check subscriptions now", () => Subscriptions.CheckAllCommand.Execute(null));
                _tray.AddMenuItem(null);
                _tray.AddMenuItem("Exit", ExitApp);
                _tray.Activated += () => ShowMainWindow();
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
                _tray = null;
            }
        }
        else if (!WantsTray && _tray is not null && MainWindow.IsVisible)
        {
            _tray.Dispose();
            _tray = null;
        }
    }

    public static bool HasTray => _tray is not null;

    // ---------- Downloads → history & notifications ----------

    private static void OnFileDownloaded(DownloadJob job, string path, string? url)
    {
        History.Add(new HistoryEntry
        {
            Title = string.IsNullOrWhiteSpace(job.Title) ? Path.GetFileNameWithoutExtension(path) : job.Title,
            Url = url ?? job.Request.Url,
            FilePath = path,
            PresetName = job.Request.PresetName,
            Completed = DateTime.Now,
        });
    }

    private static void OnJobFinished(DownloadJob job)
    {
        if (!Settings.Current.NotifyOnComplete || IsMainWindowActive) return;

        if (job.Status == JobStatus.Completed)
        {
            if (job.Request.SubscriptionName is { } sub)
            {
                if (job.FilesDownloaded > 0)
                    NotificationService.Show(
                        job.FilesDownloaded == 1 ? $"New video from {sub}" : $"{job.FilesDownloaded} new videos from {sub}",
                        job.Title, job.OutputPath);
            }
            else
            {
                NotificationService.Show("Download finished", job.Title, job.OutputPath);
            }
        }
        else if (job.Status == JobStatus.Failed)
        {
            NotificationService.Show("Download failed", $"{job.Title}: {job.LastMessage}");
        }
    }

    // ---------- Shutdown ----------

    /// <summary>Real exit (tray menu "Exit"), bypassing close-to-tray.</summary>
    public static void ExitApp()
    {
        IsExiting = true;
        MainWindow.Close();
    }

    /// <summary>Called when the main window is really closing.</summary>
    public static void OnMainWindowClosed()
    {
        IsExiting = true;
        try { Subscriptions.Stop(); } catch { /* exiting */ }
        try { Downloads.CancelAll(); } catch { /* exiting */ }
        ClosePlayer();
        _tray?.Dispose();
        _tray = null;
        NotificationService.Shutdown();
    }
}
