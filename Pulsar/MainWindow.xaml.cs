using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Pulsar.Pages;
using Pulsar.Services;

namespace Pulsar;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        try { AppWindow.SetIcon(ToolPaths.AppIcon); } catch { /* icon optional */ }

        RestorePlacement();
        Nav.SelectedItem = Nav.MenuItems[0];

        Activated += OnActivated;
        AppWindow.Closing += OnClosing;
        AppWindow.Changed += OnAppWindowChanged;
        Closed += (_, _) => App.OnMainWindowClosed();

        try { Clipboard.ContentChanged += (_, _) => DispatcherQueue.TryEnqueue(() => _ = App.Downloads.CheckClipboardAsync()); }
        catch { /* clipboard events unavailable */ }
    }

    public bool IsActive { get; private set; }

    /// <summary>Fixed size and a solid background so RenderTargetBitmap captures look like the real window.</summary>
    public void PrepareForScreenshots(int width, int height)
    {
        CaptureBackground.Visibility = Visibility.Visible;
        if (AppWindow.Presenter is OverlappedPresenter p) p.Restore();
        AppWindow.MoveAndResize(new RectInt32(0, 0, width, height));
    }
    public bool IsVisible => AppWindow.IsVisible;

    // ---------- Navigation ----------

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type page = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : ((args.SelectedItem as NavigationViewItem)?.Tag as string) switch
            {
                "tools" => typeof(ToolsPage),
                "history" => typeof(HistoryPage),
                "subscriptions" => typeof(SubscriptionsPage),
                _ => typeof(DownloadPage),
            };

        if (ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page);
    }

    private void Nav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag as string == "player") App.OpenPlayer();
    }

    public void Navigate(string tag)
    {
        if (tag == "settings")
        {
            Nav.SelectedItem = Nav.SettingsItem;
            return;
        }
        var item = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag as string == tag);
        if (item is not null) Nav.SelectedItem = item;
    }

    // ---------- Show / hide / tray ----------

    public void ShowWindow()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p) p.Restore();
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        SavePlacement();
        if (!App.IsExiting && App.Settings.Current.CloseToTray && App.HasTray)
        {
            args.Cancel = true;
            AppWindow.Hide();
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange || args.DidSizeChange)
        {
            if (App.Settings.Current.MinimizeToTray && App.HasTray &&
                AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
            {
                AppWindow.Hide();
            }
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        IsActive = args.WindowActivationState != WindowActivationState.Deactivated;
        if (IsActive) _ = App.Downloads.CheckClipboardAsync();
    }

    // ---------- Theme ----------

    public void ApplyTheme(ElementTheme theme)
    {
        if (Content is FrameworkElement root) root.RequestedTheme = theme;
        try
        {
            AppWindow.TitleBar.PreferredTheme = theme switch
            {
                ElementTheme.Light => TitleBarTheme.Light,
                ElementTheme.Dark => TitleBarTheme.Dark,
                _ => TitleBarTheme.UseDefaultAppMode,
            };
        }
        catch
        {
            // Older Windows App SDK runtime.
        }
    }

    // ---------- Remember size and position ----------

    private void RestorePlacement()
    {
        var s = App.Settings.Current;
        if (s.MainWidth >= 600 && s.MainHeight >= 450 && s.MainX != int.MinValue)
        {
            var rect = new RectInt32(s.MainX, s.MainY, s.MainWidth, s.MainHeight);
            // Only restore if that spot is still on a connected monitor.
            if (DisplayArea.GetFromRect(rect, DisplayAreaFallback.None) is not null)
            {
                AppWindow.MoveAndResize(rect);
                if (s.MainMaximized && AppWindow.Presenter is OverlappedPresenter p) p.Maximize();
                return;
            }
        }
        AppWindow.Resize(new SizeInt32(1220, 880));
    }

    private void SavePlacement()
    {
        var s = App.Settings.Current;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            s.MainMaximized = p.State == OverlappedPresenterState.Maximized;
            if (p.State != OverlappedPresenterState.Restored) { App.Settings.Save(); return; }
        }
        s.MainX = AppWindow.Position.X;
        s.MainY = AppWindow.Position.Y;
        s.MainWidth = AppWindow.Size.Width;
        s.MainHeight = AppWindow.Size.Height;
        App.Settings.Save();
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);
}
