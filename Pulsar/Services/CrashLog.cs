namespace Pulsar.Services;

/// <summary>Appends unexpected exceptions to %LOCALAPPDATA%\Pulsar\pulsar.log for troubleshooting.</summary>
public static class CrashLog
{
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(ToolPaths.AppData, "pulsar.log");

    public static void Write(Exception ex)
    {
        try
        {
            lock (Gate)
            {
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1_000_000) info.Delete();
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Never throw from the logger.
        }
    }
}
