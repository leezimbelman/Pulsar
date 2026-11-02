using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Pulsar.Models;

namespace Pulsar.Services;

/// <summary>
/// Fills the app with sample content and saves a PNG of every page in dark and light theme.
/// Run with: Pulsar.exe --screenshots C:\path\to\folder   (the Screenshots workflow does this).
/// </summary>
public static class ScreenshotRunner
{
    private static readonly string[] Pages = ["download", "subscriptions", "history", "tools", "settings"];

    public static async Task RunAsync(string outputFolder)
    {
        try
        {
            Directory.CreateDirectory(outputFolder);
            App.Settings.Current.WatchClipboard = false;
            LoadSampleContent();
            App.MainWindow.PrepareForScreenshots(1360, 900);
            await Task.Delay(2500);

            foreach (var (themeName, theme) in new[] { ("dark", 2), ("light", 1) })
            {
                App.SettingsVM.ThemeIndex = theme; // applies the theme and updates the Settings page
                App.ApplyTheme();
                foreach (var page in Pages)
                {
                    App.MainWindow.Navigate(page);
                    await Task.Delay(1800);
                    if (App.MainWindow.Content is UIElement root)
                        await CaptureAsync(root, Path.Combine(outputFolder, $"{page}-{themeName}.png"));
                }
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(outputFolder, "error.txt"), ex.ToString());
        }
        finally
        {
            Application.Current.Exit();
        }
    }

    private static async Task CaptureAsync(UIElement element, string path)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = await bitmap.GetPixelsAsync();

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();

        stream.Seek(0);
        await using var file = File.Create(path);
        await stream.AsStreamForRead().CopyToAsync(file);
    }

    private static void LoadSampleContent()
    {
        string downloads = @"C:\Users\you\Downloads";
        DateTime now = DateTime.Now;

        // ---- Download queue (Blender open movies, CC BY) ----
        var d = App.Downloads;
        DownloadRequest Req(string url, string preset) => new()
        {
            Url = url,
            FormatArgs = [],
            OutputFolder = downloads,
            FileTemplate = "%(title)s.%(ext)s",
            PresetName = preset,
        };

        var failed = new DownloadJob(Req("https://example.com/cosmos-laundromat", "1080p MP4"), "Cosmos Laundromat – First Cycle")
        {
            LastMessage = "HTTP Error 403: Forbidden. Try updating yt-dlp in Settings.",
        };
        failed.Status = JobStatus.Failed;

        var done = new DownloadJob(Req("https://example.com/tears-of-steel", "Best quality (MP4)"), "Tears of Steel")
        {
            OutputPath = Path.Combine(downloads, "Tears of Steel.mp4"),
            FilesDownloaded = 1,
        };
        done.Percent = 100;
        done.IsIndeterminate = false;
        done.Status = JobStatus.Completed;

        var queued = new DownloadJob(Req("https://example.com/lofi", "Audio — MP3 (best VBR)"), "Late-night lo-fi mix (2 hours)");

        var processing = new DownloadJob(Req("https://example.com/sintel", "1080p MP4"), "Sintel – Open Movie by Blender");
        processing.Status = JobStatus.Processing;
        processing.LastMessage = "Post-processing (Merger)…";

        var running = new DownloadJob(Req("https://example.com/big-buck-bunny", "Best quality (MP4)"), "Big Buck Bunny (4K, 60 fps)");
        running.Status = JobStatus.Running;
        running.IsIndeterminate = false;
        running.Percent = 62.4;
        running.Speed = "8.21MiB/s";
        running.Eta = "00:14";

        foreach (var job in new[] { failed, done, queued, processing, running }) d.Jobs.Insert(0, job);
        d.ClipboardUrl = "https://vimeo.com/1084537";

        // ---- History ----
        var history = new (string Title, string File, string Preset, double HoursAgo)[]
        {
            ("Tears of Steel", "Tears of Steel.mp4", "Best quality (MP4)", 0.2),
            ("Spring – Blender Open Movie", "Spring.mp4", "1080p MP4", 3),
            ("Agent 327: Operation Barbershop", "Agent 327 Operation Barbershop.mp4", "1080p MP4", 26),
            ("Late-night lo-fi mix (2 hours)", "Late-night lo-fi mix.mp3", "Audio — MP3 (best VBR)", 30),
            ("Caminandes 3: Llamigos", "Caminandes 3 Llamigos.mp4", "720p MP4", 52),
            ("Charge – Blender Open Movie", "Charge.mp4", "Best quality (MP4)", 75),
            ("Hero – Grease Pencil Showcase", "Hero.webm", "Best quality (original container)", 120),
        };
        foreach (var h in history)
        {
            App.History.Entries.Add(new HistoryEntry
            {
                Title = h.Title,
                Url = "https://example.com/" + h.File.Replace(' ', '-'),
                FilePath = Path.Combine(downloads, h.File),
                PresetName = h.Preset,
                Completed = now.AddHours(-h.HoursAgo),
            });
        }

        // ---- Subscriptions ----
        var subs = App.Subscriptions;
        subs.Items.Clear();
        subs.Items.Add(new Subscription { Name = "Blender Studio", Url = "https://www.youtube.com/@BlenderStudio/videos", IntervalHours = 6, LastChecked = now.AddMinutes(-42), LastResult = "2 new videos" });
        subs.Items.Add(new Subscription { Name = "NASA", Url = "https://www.youtube.com/@NASA/videos", IntervalHours = 24, LastChecked = now.AddHours(-5), LastResult = "no new videos" });
        subs.Items.Add(new Subscription { Name = "Weekly podcast", Url = "https://example.com/podcast/playlist", IntervalHours = 168, LastChecked = now.AddDays(-2), LastResult = "1 new video", PresetIndex = 5 });
        subs.Items.Add(new Subscription { Name = "Conference talks", Url = "https://example.com/talks", IntervalHours = 12, Enabled = false });

        // ---- Tools ----
        var tools = App.Tools;
        tools.OperationIndex = 1; // Convert
        tools.AddDemoFile(Path.Combine(downloads, "Tears of Steel.mp4"), "12:14   •   3840×1714   •   1.62 GB");
        tools.AddDemoFile(Path.Combine(downloads, "Spring.mp4"), "7:44   •   1920×804   •   412.8 MB");
        tools.AddDemoFile(Path.Combine(downloads, "Charge.mp4"), "3:48   •   3840×1608   •   690.1 MB");
        tools.ConvertPresetIndex = 1;
        tools.IsRunning = true;
        tools.Percent = 48;
        tools.Status = "File 2 of 3: Convert   →   Spring.mp4";

        // ---- Settings ----
        App.SettingsVM.SetDemoVersions("2026.10.22", "N-121640-g2f5c0e4b1a-20261031");
    }
}
