using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using Pulsar.Services;
using VlcMedia = LibVLCSharp.Shared.Media;
using VlcPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace Pulsar;

/// <summary>
/// Media player built on LibVLC, which plays virtually any audio/video format.
/// LibVLC raises events on its own threads, so everything is marshalled to the UI thread via Ui(),
/// and calls that can block (Play, Stop, pause, snapshots) run on the thread pool.
/// </summary>
public sealed partial class PlayerWindow : Window
{
    private static readonly float[] Rates = [0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f];
    private const double SliderPad = 10; // half the slider thumb, so markers line up with the track

    private LibVLC? _libVlc;
    private VlcPlayer? _player;
    private string? _pendingPath;
    private string? _currentPath;
    private long _length;
    private long _time;
    private bool _updatingSeek;
    private bool _seeking;
    private bool _muted;
    private bool _hasVideo;
    private bool _closed;
    private bool _resumeChecked;

    private long? _markIn;
    private long? _markOut;
    private long? _loopA;
    private long? _loopB;

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _toastTimer;
    private Action? _toastAction;

    public PlayerWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        try { AppWindow.SetIcon(ToolPaths.AppIcon); } catch { /* icon optional */ }

        var s = App.Settings.Current;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            s.PlayerWidth >= 480 ? s.PlayerWidth : 1100,
            s.PlayerHeight >= 320 ? s.PlayerHeight : 720));
        VolumeSlider.Value = Math.Clamp(s.PlayerVolume, 0, 100);

        // Seek only when the user lets go of the slider, so playback doesn't fight the drag.
        Seek.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => _seeking = true), true);
        Seek.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => EndSeek()), true);
        Seek.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => EndSeek()), true);

        _toastTimer = DispatcherQueue.CreateTimer();
        _toastTimer.Interval = TimeSpan.FromSeconds(5);
        _toastTimer.IsRepeating = false;
        _toastTimer.Tick += (_, _) => Toast.IsOpen = false;

        Activated += (_, _) => Stage.Focus(FocusState.Programmatic);
        AppWindow.Closing += (_, _) => SaveWindowSize();
        Closed += OnClosed;
        UpdateMarks();
    }

    private bool IsFullscreen => AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
    private bool IsPip => AppWindow.Presenter.Kind == AppWindowPresenterKind.CompactOverlay;

    // ---------- Public API ----------

    public void Open(string path)
    {
        if (_player is null || _libVlc is null)
        {
            _pendingPath = path; // VideoView not ready yet; Video_Initialized picks it up
            return;
        }

        SaveResumePosition();
        _currentPath = path;
        _hasVideo = false;
        _resumeChecked = false;
        _markIn = _markOut = _loopA = _loopB = null;
        string name = Path.GetFileName(path);
        Title = $"{name}  ·  Pulsar Player";
        TitleText.Text = name;
        SetLength(0);
        SetTime(0);
        UpdateMarks();
        Toast.IsOpen = false;
        ShowPlaceholder("", name, showOpen: false); // audio look until a video track shows up
        SpeedBox.SelectedIndex = 2;
        Stage.Focus(FocusState.Programmatic);

        // Play() first stops whatever was playing, which waits on the video output; keep that off the UI thread.
        var libVlc = _libVlc;
        var player = _player;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            using var media = new VlcMedia(libVlc, path, FromType.FromPath);
            player.Play(media);
            player.SetRate(1f);
        });
    }

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

    // ---------- LibVLC setup ----------

    private async void Video_Initialized(object? sender, InitializedEventArgs e)
    {
        ShowPlaceholder("", "Loading player…", showOpen: false);
        var options = e.SwapChainOptions;

        // Loading LibVLC scans its plugin folder, which takes a few seconds the first time.
        var (libVlc, player) = await Task.Run(() =>
        {
            Core.Initialize();
            var vlc = new LibVLC(enableDebugLogs: false, options);
            return (vlc, new VlcPlayer(vlc));
        });

        if (_closed)
        {
            ThreadPool.QueueUserWorkItem(_ => { player.Dispose(); libVlc.Dispose(); });
            return;
        }

        _libVlc = libVlc;
        _player = player;

        _player.Playing += (_, _) => Ui(OnPlaying);
        _player.Paused += (_, _) => Ui(() => SetPlayIcon(false));
        _player.Stopped += (_, _) => Ui(OnStopped);
        _player.EndReached += (_, _) => Ui(OnEnded);
        _player.EncounteredError += (_, _) => Ui(() => ShowPlaceholder("", "This file couldn't be played.", showOpen: true));
        _player.LengthChanged += (_, a) => { long len = a.Length; Ui(() => SetLength(len)); };
        _player.TimeChanged += (_, a) => { long t = a.Time; Ui(() => OnTimeChanged(t)); };
        _player.ESAdded += (_, a) =>
        {
            if (a.Type == TrackType.Video) Ui(() => { _hasVideo = true; Placeholder.Visibility = Visibility.Collapsed; });
        };

        Video.MediaPlayer = _player;

        if (_pendingPath is { } path)
        {
            _pendingPath = null;
            Open(path);
        }
        else
        {
            ShowPlaceholder("", "Open a file, or drop one here", showOpen: true);
        }
    }

    private void Ui(Action action) => DispatcherQueue.TryEnqueue(() => { if (!_closed) action(); });

    private void OnPlaying()
    {
        SetPlayIcon(true);
        if (_player is null) return;
        _player.Volume = (int)VolumeSlider.Value;
        _player.Mute = _muted;
        if (!_hasVideo && _player.VideoTrackCount > 0)
        {
            _hasVideo = true;
            Placeholder.Visibility = Visibility.Collapsed;
        }
        TryResume();
    }

    private void TryResume()
    {
        if (_resumeChecked || _currentPath is null || _player is null || _length <= 0) return;
        _resumeChecked = true;
        if (ResumeService.Get(_currentPath) is not long pos || pos >= _length - 5_000) return;

        _player.Time = pos;
        SetTime(pos);
        string path = _currentPath;
        ShowToast($"Resumed at {Format(pos)}", "Start over", () =>
        {
            if (_player is null) return;
            _player.Time = 0;
            SetTime(0);
            ResumeService.Clear(path);
        });
    }

    private void OnStopped()
    {
        SetPlayIcon(false);
        SetTime(0);
    }

    private void OnEnded()
    {
        SetPlayIcon(false);
        SetTime(_length);
        if (_currentPath is not null) ResumeService.Clear(_currentPath);
    }

    private void OnTimeChanged(long t)
    {
        _time = t;
        SetTime(t);
        if (_loopA is long a && _loopB is long b && b > a && t >= b && _player is { IsSeekable: true })
        {
            _player.Time = a;
            SetTime(a);
        }
    }

    private void SaveResumePosition()
    {
        if (_currentPath is null || _player is null) return;
        try { ResumeService.Update(_currentPath, _player.Time, _length); } catch { /* best effort */ }
    }

    // ---------- Transport ----------

    private void PlayPause()
    {
        if (_player is null) return;

        var player = _player;
        if (player.IsPlaying)
            ThreadPool.QueueUserWorkItem(_ => player.SetPause(true));
        else if (player.State == VLCState.Paused)
            ThreadPool.QueueUserWorkItem(_ => player.SetPause(false));
        else if (_currentPath is not null)
            Open(_currentPath); // stopped or ended: start over
        else
            _ = OpenDialogAsync();
    }

    private void Skip(long deltaMs)
    {
        if (_player is null || !_player.IsSeekable) return;
        long target = Math.Clamp(_player.Time + deltaMs, 0, Math.Max(0, _length - 500));
        _player.Time = target;
        SetTime(target);
    }

    private void Stop()
    {
        var player = _player;
        if (player is null) return;
        SaveResumePosition();
        ThreadPool.QueueUserWorkItem(_ => player.Stop());
    }

    private void ToggleMute()
    {
        _muted = !_muted;
        if (_player is not null) _player.Mute = _muted;
        UpdateVolumeIcon();
    }

    private void ToggleFullscreen()
    {
        if (IsPip) TogglePip();
        bool goFull = !IsFullscreen;
        AppWindow.SetPresenter(goFull ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
        TitleBar.Visibility = goFull ? Visibility.Collapsed : Visibility.Visible;
        FullscreenIcon.Glyph = goFull ? "" : "";
        Stage.Focus(FocusState.Programmatic);
    }

    private void TogglePip()
    {
        if (IsFullscreen) ToggleFullscreen();
        bool goPip = !IsPip;
        AppWindow.SetPresenter(goPip ? AppWindowPresenterKind.CompactOverlay : AppWindowPresenterKind.Overlapped);
        TransportBar.Visibility = goPip ? Visibility.Collapsed : Visibility.Visible;
        PipIcon.Glyph = goPip ? "" : "";
        if (goPip) ShowToast("Picture-in-picture: double-click or press P to go back", null, null);
        Stage.Focus(FocusState.Programmatic);
    }

    private async Task OpenDialogAsync()
    {
        if (await Pickers.PickFileAsync(WinRT.Interop.WindowNative.GetWindowHandle(this)) is { } path)
            Open(path);
    }

    // ---------- Clip marks, loop, snapshot ----------

    private void MarkIn()
    {
        if (_currentPath is null) return;
        _markIn = _time;
        if (_markOut is long o && o <= _time) _markOut = null;
        UpdateMarks();
    }

    private void MarkOut()
    {
        if (_currentPath is null) return;
        _markOut = _time;
        if (_markIn is long i && i >= _time) _markIn = null;
        UpdateMarks();
    }

    private void SendToTrim()
    {
        if (_currentPath is null) return;
        if (_player?.IsPlaying == true) PlayPause();
        App.SendToTrim(_currentPath, (_markIn ?? 0) / 1000.0, _markOut is long o ? o / 1000.0 : null);
    }

    private void CycleLoop()
    {
        if (_currentPath is null) return;
        if (_loopA is null)
        {
            _loopA = _time;
            ShowToast($"Loop start (A) set at {Format(_time)}. Press L again to set B.", null, null);
        }
        else if (_loopB is null)
        {
            if (_time <= _loopA) { ShowToast("B has to come after A.", null, null); return; }
            _loopB = _time;
            ShowToast($"Looping {Format(_loopA.Value)} – {Format(_loopB.Value)}. Press L to stop.", null, null);
            if (_player is { IsSeekable: true }) _player.Time = _loopA.Value;
        }
        else
        {
            _loopA = _loopB = null;
            ShowToast("Loop off.", null, null);
        }
        UpdateMarks();
    }

    private void Snapshot()
    {
        var player = _player;
        if (player is null || _currentPath is null || !_hasVideo) return;

        Directory.CreateDirectory(ToolPaths.Snapshots);
        string stem = string.Join("_", Path.GetFileNameWithoutExtension(_currentPath).Split(Path.GetInvalidFileNameChars()));
        string file = Path.Combine(ToolPaths.Snapshots, $"{stem} {Format(_time).Replace(':', '.')}.png");
        for (int i = 2; File.Exists(file); i++)
            file = Path.Combine(ToolPaths.Snapshots, $"{stem} {Format(_time).Replace(':', '.')} ({i}).png");

        ThreadPool.QueueUserWorkItem(_ =>
        {
            bool ok = player.TakeSnapshot(0, file, 0, 0);
            Ui(() =>
            {
                if (ok) ShowToast($"Saved {Path.GetFileName(file)}", "Show", () => Pickers.ShowInExplorer(file, ToolPaths.Snapshots));
                else ShowToast("Couldn't save a snapshot of this frame.", null, null);
            });
        });
    }

    private void UpdateMarks()
    {
        bool hasFile = _currentPath is not null;
        SendToTrimButton.IsEnabled = hasFile;
        MarksText.Text = (_markIn, _markOut) switch
        {
            (null, null) => "",
            (long i, null) => $"{Format(i)} →",
            (null, long o) => $"→ {Format(o)}",
            (long i, long o) => $"{Format(i)} – {Format(o)}",
        };

        LoopButton.IsChecked = _loopA is not null;
        LoopText.Text = (_loopA, _loopB) switch
        {
            (null, _) => "A-B",
            (long, null) => "A…",
            _ => "A-B ✓",
        };

        double width = Math.Max(0, SeekHost.ActualWidth - SliderPad * 2);
        MarkerCanvas.Width = SeekHost.ActualWidth;
        double X(long ms) => SliderPad + (_length > 0 ? Math.Clamp(ms / (double)_length, 0, 1) : 0) * width;

        Place(InMark, _markIn is long a ? X(a) - 1.5 : null);
        Place(OutMark, _markOut is long b ? X(b) - 1.5 : null);

        if (_length > 0 && (_markIn is not null || _markOut is not null))
        {
            double left = X(_markIn ?? 0), right = X(_markOut ?? _length);
            Canvas.SetLeft(RangeRect, left);
            RangeRect.Width = Math.Max(0, right - left);
            RangeRect.Visibility = Visibility.Visible;
        }
        else
        {
            RangeRect.Visibility = Visibility.Collapsed;
        }

        if (_length > 0 && _loopA is long la)
        {
            double left = X(la), right = _loopB is long lb ? X(lb) : left + 3;
            Canvas.SetLeft(LoopRect, left);
            LoopRect.Width = Math.Max(3, right - left);
            LoopRect.Visibility = Visibility.Visible;
        }
        else
        {
            LoopRect.Visibility = Visibility.Collapsed;
        }
    }

    private static void Place(FrameworkElement element, double? x)
    {
        if (x is double left)
        {
            Canvas.SetLeft(element, left);
            element.Visibility = Visibility.Visible;
        }
        else
        {
            element.Visibility = Visibility.Collapsed;
        }
    }

    // ---------- UI state ----------

    private void SetLength(long length)
    {
        _length = length;
        Seek.Maximum = Math.Max(1, length);
        LengthText.Text = Format(length);
        UpdateMarks();
        if (length > 0 && _player?.IsPlaying == true) TryResume();
    }

    private void SetTime(long time)
    {
        if (_seeking) return;
        _updatingSeek = true;
        Seek.Value = Math.Clamp(time, 0, (long)Seek.Maximum);
        _updatingSeek = false;
        PositionText.Text = Format(time);
    }

    private void EndSeek()
    {
        if (!_seeking) return;
        _seeking = false;
        if (_player is { IsSeekable: true })
        {
            _player.Time = (long)Seek.Value;
            _time = (long)Seek.Value;
        }
    }

    private void SetPlayIcon(bool playing) => PlayPauseIcon.Glyph = playing ? "" : "";

    private void UpdateVolumeIcon() =>
        VolumeIcon.Glyph = _muted || VolumeSlider.Value == 0 ? "" : VolumeSlider.Value < 50 ? "" : "";

    private void ShowPlaceholder(string glyph, string text, bool showOpen)
    {
        PlaceholderIcon.Glyph = glyph;
        PlaceholderText.Text = text;
        PlaceholderOpen.Visibility = showOpen ? Visibility.Visible : Visibility.Collapsed;
        Placeholder.Visibility = Visibility.Visible;
    }

    private void ShowToast(string message, string? actionText, Action? action)
    {
        Toast.Message = message;
        _toastAction = action;
        ToastAction.Content = actionText;
        ToastAction.Visibility = actionText is null ? Visibility.Collapsed : Visibility.Visible;
        Toast.IsOpen = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private static string Format(long ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    private void SaveWindowSize()
    {
        if (AppWindow.Presenter.Kind != AppWindowPresenterKind.Overlapped) return;
        var s = App.Settings.Current;
        s.PlayerWidth = AppWindow.Size.Width;
        s.PlayerHeight = AppWindow.Size.Height;
        s.PlayerVolume = (int)VolumeSlider.Value;
        App.Settings.Save();
    }

    // ---------- Event handlers ----------

    private void PlayPause_Click(object sender, RoutedEventArgs e) => PlayPause();
    private void Back_Click(object sender, RoutedEventArgs e) => Skip(-10_000);
    private void Forward_Click(object sender, RoutedEventArgs e) => Skip(10_000);
    private void Stop_Click(object sender, RoutedEventArgs e) => Stop();
    private void Mute_Click(object sender, RoutedEventArgs e) => ToggleMute();
    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void Pip_Click(object sender, RoutedEventArgs e) => TogglePip();
    private void MarkIn_Click(object sender, RoutedEventArgs e) => MarkIn();
    private void MarkOut_Click(object sender, RoutedEventArgs e) => MarkOut();
    private void SendToTrim_Click(object sender, RoutedEventArgs e) => SendToTrim();
    private void Loop_Click(object sender, RoutedEventArgs e) => CycleLoop();
    private void Snapshot_Click(object sender, RoutedEventArgs e) => Snapshot();
    private async void Open_Click(object sender, RoutedEventArgs e) => await OpenDialogAsync();
    private void SeekHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateMarks();

    private void ToastAction_Click(object sender, RoutedEventArgs e)
    {
        Toast.IsOpen = false;
        var action = _toastAction;
        _toastAction = null;
        action?.Invoke();
    }

    private void Stage_Tapped(object sender, TappedRoutedEventArgs e) => Stage.Focus(FocusState.Pointer);

    private void Stage_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (IsPip) TogglePip();
        else if (_currentPath is not null) ToggleFullscreen();
    }

    private void Seek_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSeek) return;
        PositionText.Text = Format((long)e.NewValue); // preview while dragging; EndSeek does the seek
    }

    private void Volume_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (VolumeIcon is null) return; // fires during InitializeComponent
        if (_player is not null) _player.Volume = (int)e.NewValue;
        UpdateVolumeIcon();
    }

    private void Speed_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_player is null || SpeedBox.SelectedIndex < 0) return;
        _player.SetRate(Rates[SpeedBox.SelectedIndex]);
        Stage?.Focus(FocusState.Programmatic);
    }

    private void TracksMenu_Opening(object sender, object e)
    {
        TracksMenu.Items.Clear();
        var player = _player;
        if (player is null) return;

        AddTrackMenu("Audio track", player.AudioTrackDescription.Select(t => (t.Id, t.Name)), player.AudioTrack, id => player.SetAudioTrack(id));
        AddTrackMenu("Subtitles", player.SpuDescription.Select(t => (t.Id, t.Name)), player.Spu, id => player.SetSpu(id));

        TracksMenu.Items.Add(new MenuFlyoutSeparator());
        var load = new MenuFlyoutItem { Text = "Load subtitle file…" };
        load.Click += async (_, _) =>
        {
            if (await Pickers.PickFileAsync(WinRT.Interop.WindowNative.GetWindowHandle(this), ".srt", ".ass", ".ssa", ".vtt", ".sub") is { } path)
                player.AddSlave(MediaSlaveType.Subtitle, new Uri(path).AbsoluteUri, true);
        };
        TracksMenu.Items.Add(load);
    }

    private void AddTrackMenu(string header, IEnumerable<(int Id, string? Name)> trackList, int current, Action<int> select)
    {
        var tracks = trackList.ToList();
        var sub = new MenuFlyoutSubItem { Text = header };
        if (tracks.Count == 0)
            sub.Items.Add(new MenuFlyoutItem { Text = "None available", IsEnabled = false });

        foreach (var track in tracks)
        {
            int id = track.Id;
            var item = new ToggleMenuFlyoutItem
            {
                Text = string.IsNullOrWhiteSpace(track.Name) ? $"Track {id}" : track.Name,
                IsChecked = id == current,
            };
            item.Click += (_, _) => select(id);
            sub.Items.Add(item);
        }
        TracksMenu.Items.Add(sub);
    }

    private static bool CtrlDown =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox or ComboBox) return;

        switch (e.Key)
        {
            case VirtualKey.Space: PlayPause(); break;
            case VirtualKey.Left: Skip(-5_000); break;
            case VirtualKey.Right: Skip(5_000); break;
            case VirtualKey.Up: VolumeSlider.Value = Math.Min(100, VolumeSlider.Value + 5); break;
            case VirtualKey.Down: VolumeSlider.Value = Math.Max(0, VolumeSlider.Value - 5); break;
            case VirtualKey.M: ToggleMute(); break;
            case VirtualKey.F: ToggleFullscreen(); break;
            case VirtualKey.P: TogglePip(); break;
            case VirtualKey.Escape when IsFullscreen: ToggleFullscreen(); break;
            case VirtualKey.Escape when IsPip: TogglePip(); break;
            case VirtualKey.O when CtrlDown: _ = OpenDialogAsync(); break;
            case VirtualKey.O: MarkOut(); break;
            case VirtualKey.I: MarkIn(); break;
            case VirtualKey.L: CycleLoop(); break;
            case VirtualKey.S: Snapshot(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Play";
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        if (items.FirstOrDefault() is StorageFile file) Open(file.Path);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        SaveResumePosition();
        _toastTimer.Stop();
        _closed = true;
        var player = _player;
        var libVlc = _libVlc;
        _player = null;
        _libVlc = null;
        if (player is null) return;

        // Tear down off the UI thread; Stop() waits for the video output to close.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                player.Stop();
                player.Dispose();
                libVlc?.Dispose();
            }
            catch
            {
                // Shutting down anyway.
            }
        });
    }
}
