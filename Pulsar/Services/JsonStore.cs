using System.Text.Json;

namespace Pulsar.Services;

/// <summary>Small JSON files in %LOCALAPPDATA%\Pulsar, written atomically so a crash never leaves a half-written file.</summary>
public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly object Gate = new();

    /// <summary>When true (screenshot mode), nothing is written to disk.</summary>
    public static bool ReadOnly { get; set; }

    public static string PathFor(string name) => Path.Combine(ToolPaths.AppData, name);

    public static T Load<T>(string name, Func<T> fallback)
    {
        try
        {
            string path = PathFor(name);
            if (File.Exists(path))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? fallback();
        }
        catch
        {
            // Corrupt or unreadable: start fresh.
        }
        return fallback();
    }

    public static void Save<T>(string name, T value)
    {
        if (ReadOnly) return;
        try
        {
            string json = JsonSerializer.Serialize(value, Options);
            lock (Gate)
            {
                Directory.CreateDirectory(ToolPaths.AppData);
                string path = PathFor(name);
                string temp = path + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, path, overwrite: true);
            }
        }
        catch
        {
            // Non-fatal: the data just won't persist this time.
        }
    }
}
