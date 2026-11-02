namespace Pulsar.Services;

/// <summary>Remembers where you stopped watching each file (resume.json).</summary>
public static class ResumeService
{
    private const string FileName = "resume.json";
    private const int MaxEntries = 500;

    public sealed class Entry
    {
        public long PositionMs { get; set; }
        public DateTime Saved { get; set; }
    }

    private static Dictionary<string, Entry>? _entries;

    private static Dictionary<string, Entry> Entries =>
        _entries ??= new Dictionary<string, Entry>(
            JsonStore.Load(FileName, () => new Dictionary<string, Entry>()), StringComparer.OrdinalIgnoreCase);

    public static long? Get(string path) =>
        Entries.TryGetValue(path, out var e) && e.PositionMs > 0 ? e.PositionMs : null;

    /// <summary>
    /// Saves the position for longer files (3+ minutes) when you're somewhere in the middle;
    /// near the start or end the entry is cleared instead.
    /// </summary>
    public static void Update(string path, long positionMs, long lengthMs)
    {
        bool worthKeeping = lengthMs >= 180_000 && positionMs > 15_000 && positionMs < lengthMs - 20_000;
        if (worthKeeping)
        {
            Entries[path] = new Entry { PositionMs = positionMs, Saved = DateTime.Now };
            if (Entries.Count > MaxEntries)
            {
                foreach (var old in Entries.OrderBy(kv => kv.Value.Saved).Take(Entries.Count - MaxEntries).ToList())
                    Entries.Remove(old.Key);
            }
        }
        else if (!Entries.Remove(path))
        {
            return; // nothing changed
        }
        JsonStore.Save(FileName, Entries);
    }

    public static void Clear(string path)
    {
        if (Entries.Remove(path)) JsonStore.Save(FileName, Entries);
    }
}
