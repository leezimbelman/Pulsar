using System.Collections.ObjectModel;
using Pulsar.Models;

namespace Pulsar.Services;

/// <summary>Completed downloads, newest first, saved to history.json. Use from the UI thread.</summary>
public sealed class HistoryService
{
    private const string FileName = "history.json";
    private const int MaxEntries = 2000;

    public ObservableCollection<HistoryEntry> Entries { get; } = [];

    public void Load()
    {
        Entries.Clear();
        foreach (var entry in JsonStore.Load(FileName, () => new List<HistoryEntry>()).OrderByDescending(e => e.Completed))
            Entries.Add(entry);
    }

    public void Add(HistoryEntry entry)
    {
        var existing = Entries.FirstOrDefault(e => string.Equals(e.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) Entries.Remove(existing);
        Entries.Insert(0, entry);
        while (Entries.Count > MaxEntries) Entries.RemoveAt(Entries.Count - 1);
        Save();
    }

    public void Remove(HistoryEntry entry)
    {
        Entries.Remove(entry);
        Save();
    }

    public void Clear()
    {
        Entries.Clear();
        Save();
    }

    private void Save() => JsonStore.Save(FileName, Entries.ToList());
}
