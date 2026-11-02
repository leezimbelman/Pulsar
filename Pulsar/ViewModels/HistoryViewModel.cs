using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.ApplicationModel.DataTransfer;
using Pulsar.Models;
using Pulsar.Services;

namespace Pulsar.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly HistoryService _history;

    public HistoryViewModel(HistoryService history)
    {
        _history = history;
        _history.Entries.CollectionChanged += OnHistoryChanged;
        Refresh();
    }

    public ObservableCollection<HistoryEntry> Items { get; } = [];
    public bool IsEmpty => Items.Count == 0;
    public string EmptyText => _history.Entries.Count == 0
        ? "Nothing here yet. Finished downloads show up here."
        : "No downloads match your search.";
    public string CountText => _history.Entries.Count == 1 ? "1 download" : $"{_history.Entries.Count} downloads";

    [ObservableProperty] private string _search = "";

    partial void OnSearchChanged(string value) => Refresh();

    private void OnHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        string q = Search.Trim();
        var matches = string.IsNullOrEmpty(q)
            ? _history.Entries
            : _history.Entries.Where(e =>
                e.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.FilePath.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Url.Contains(q, StringComparison.OrdinalIgnoreCase));

        Items.Clear();
        foreach (var entry in matches.Take(500)) Items.Add(entry);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(CountText));
    }

    public static void Play(HistoryEntry entry) => App.OpenPlayer(entry.FilePath);

    public static void ShowInFolder(HistoryEntry entry) => Pickers.ShowInExplorer(entry.FilePath, entry.Folder);

    public static void CopyLink(HistoryEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Url)) return;
        var package = new DataPackage();
        package.SetText(entry.Url);
        Clipboard.SetContent(package);
    }

    public static void DownloadAgain(HistoryEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Url)) return;
        App.QueueUrl(entry.Url);
    }

    public void Remove(HistoryEntry entry) => _history.Remove(entry);

    [RelayCommand]
    private void ClearAll() => _history.Clear();
}
