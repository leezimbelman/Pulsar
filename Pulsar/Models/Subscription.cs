using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Pulsar.Models;

/// <summary>A channel or playlist Pulsar checks on a schedule for new uploads.</summary>
public partial class Subscription : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; } = "";
    public string Folder { get; set; } = "";
    public int PresetIndex { get; set; }
    public int IntervalHours { get; set; } = 6;
    public int MaxItems { get; set; } = 10;

    [ObservableProperty] private string _name = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private bool _enabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private DateTime? _lastChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private string _lastResult = "";

    [JsonIgnore]
    public DateTime NextCheck => (LastChecked ?? DateTime.MinValue).AddHours(IntervalHours);

    [JsonIgnore]
    public bool IsDue => Enabled && DateTime.Now >= NextCheck;

    [JsonIgnore]
    public string Status
    {
        get
        {
            string every = IntervalHours switch
            {
                1 => "every hour",
                24 => "daily",
                168 => "weekly",
                _ => $"every {IntervalHours} hours",
            };
            if (!Enabled) return $"Paused   •   {Url}";
            string last = LastChecked is { } t ? $"last checked {t:MMM d h:mm tt}" : "not checked yet";
            string result = string.IsNullOrEmpty(LastResult) ? "" : $"   •   {LastResult}";
            return $"Checks {every}   •   {last}{result}";
        }
    }

    public void RefreshStatus() => OnPropertyChanged(nameof(Status));
}
