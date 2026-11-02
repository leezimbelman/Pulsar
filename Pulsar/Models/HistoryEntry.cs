namespace Pulsar.Models;

public sealed class HistoryEntry
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string PresetName { get; set; } = "";
    public DateTime Completed { get; set; } = DateTime.Now;

    public string FileName => Path.GetFileName(FilePath);
    public string Folder => Path.GetDirectoryName(FilePath) ?? "";
    public bool FileExists => File.Exists(FilePath);
    public string When => Completed.Date == DateTime.Today
        ? $"Today {Completed:t}"
        : Completed.Date == DateTime.Today.AddDays(-1) ? $"Yesterday {Completed:t}" : Completed.ToString("MMM d, yyyy  h:mm tt");
    public string Details => string.IsNullOrEmpty(PresetName) ? $"{When}   •   {Folder}" : $"{When}   •   {PresetName}   •   {Folder}";
}
