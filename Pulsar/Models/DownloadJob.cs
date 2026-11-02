using CommunityToolkit.Mvvm.ComponentModel;

namespace Pulsar.Models;

public enum JobStatus { Queued, Running, Processing, Completed, Failed, Canceled }

/// <summary>One item in the download queue. All property changes happen on the UI thread.</summary>
public partial class DownloadJob : ObservableObject
{
    public DownloadJob(DownloadRequest request, string? title)
    {
        Request = request;
        _title = string.IsNullOrWhiteSpace(title) ? request.Url : title;
    }

    public DownloadRequest Request { get; }
    public string? OutputPath { get; set; }
    public int FilesDownloaded { get; set; }
    public string? LastError { get; set; }
    public CancellationTokenSource? Cts { get; set; }

    [ObservableProperty] private string _title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private double _percent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _speed = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _eta = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _lastMessage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIndeterminate), nameof(StatusText))]
    private bool _isIndeterminate = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsActive), nameof(IsFinished), nameof(IsDone),
        nameof(CanPlay), nameof(IsFailed), nameof(IsCanceled), nameof(CanRetry), nameof(ShowIndeterminate))]
    private JobStatus _status = JobStatus.Queued;

    public bool IsActive => Status is JobStatus.Queued or JobStatus.Running or JobStatus.Processing;
    public bool IsFinished => !IsActive;
    public bool IsDone => Status == JobStatus.Completed;
    public bool CanPlay => IsDone && OutputPath is not null;
    public bool IsFailed => Status == JobStatus.Failed;
    public bool IsCanceled => Status == JobStatus.Canceled;
    public bool CanRetry => IsFailed || IsCanceled;
    public bool ShowIndeterminate => IsIndeterminate && (Status is JobStatus.Running or JobStatus.Processing);

    public string StatusText => Status switch
    {
        JobStatus.Queued => "Waiting in queue…",
        JobStatus.Running when IsIndeterminate => string.IsNullOrEmpty(LastMessage) ? "Starting…" : LastMessage,
        JobStatus.Running => $"{Percent:0.0}%   •   {Speed}   •   ETA {Eta}",
        JobStatus.Processing => string.IsNullOrEmpty(LastMessage) ? "Post-processing…" : LastMessage,
        JobStatus.Completed when Request.SubscriptionName is not null && FilesDownloaded == 0 => "Done   •   no new videos",
        JobStatus.Completed when FilesDownloaded > 1 => $"Done   •   {FilesDownloaded} files",
        JobStatus.Completed => OutputPath is null ? "Done" : $"Done   •   {Path.GetFileName(OutputPath)}",
        JobStatus.Failed => $"Failed: {LastMessage}",
        JobStatus.Canceled => "Canceled",
        _ => "",
    };

    public void ResetForRetry()
    {
        OutputPath = null;
        FilesDownloaded = 0;
        LastError = null;
        Percent = 0;
        Speed = "";
        Eta = "";
        LastMessage = "";
        IsIndeterminate = true;
        Status = JobStatus.Queued;
    }
}
