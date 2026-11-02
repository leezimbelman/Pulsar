namespace Pulsar.Services;

/// <summary>
/// Everything Pulsar writes lives in %LOCALAPPDATA%\Pulsar, so updating yt-dlp, ffmpeg or whisper
/// never needs admin rights.
/// </summary>
public static class ToolPaths
{
    public static string AppData { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pulsar");

    public static string Bin { get; } = Path.Combine(AppData, "bin");
    public static string Whisper { get; } = Path.Combine(Bin, "whisper");
    public static string Models { get; } = Path.Combine(AppData, "models");
    public static string Archives { get; } = Path.Combine(AppData, "archives");

    public static string YtDlp => Path.Combine(Bin, "yt-dlp.exe");
    public static string Ffmpeg => Path.Combine(Bin, "ffmpeg.exe");
    public static string Ffprobe => Path.Combine(Bin, "ffprobe.exe");

    public static bool HasYtDlp => File.Exists(YtDlp);
    public static bool HasFfmpeg => File.Exists(Ffmpeg) && File.Exists(Ffprobe);

    public static string Snapshots =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Pulsar");

    public static string ArchiveFor(Guid subscriptionId) => Path.Combine(Archives, $"{subscriptionId:N}.txt");

    public static string AppIcon => Path.Combine(AppContext.BaseDirectory, "Assets", "Pulsar.ico");

    static ToolPaths()
    {
        MigrateFromYtDlpStudio();
        foreach (var dir in new[] { AppData, Bin, Models, Archives })
            Directory.CreateDirectory(dir);
    }

    /// <summary>Pulsar was called "YtDlp Studio" during early development; carry its tools and settings over once.</summary>
    private static void MigrateFromYtDlpStudio()
    {
        try
        {
            string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YtDlpStudio");
            if (Directory.Exists(AppData) || !Directory.Exists(old)) return;

            Directory.CreateDirectory(AppData);
            string oldSettings = Path.Combine(old, "settings.json");
            if (File.Exists(oldSettings)) File.Copy(oldSettings, Path.Combine(AppData, "settings.json"));

            string oldBin = Path.Combine(old, "bin");
            if (Directory.Exists(oldBin))
            {
                Directory.CreateDirectory(Bin);
                foreach (var file in Directory.GetFiles(oldBin))
                    File.Copy(file, Path.Combine(Bin, Path.GetFileName(file)), overwrite: true);
            }
        }
        catch
        {
            // Best effort only: Pulsar will offer to download the tools again.
        }
    }
}
