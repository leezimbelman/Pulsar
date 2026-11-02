using CommunityToolkit.Mvvm.ComponentModel;
using Pulsar.Services;

namespace Pulsar.Models;

/// <summary>An input file on the Tools page.</summary>
public partial class MediaFile : ObservableObject
{
    public MediaFile(string path)
    {
        FilePath = path;
    }

    public string FilePath { get; }
    public string Name => Path.GetFileName(FilePath);
    public MediaProbe? Probe { get; private set; }

    [ObservableProperty] private string _info = "";

    public async Task LoadAsync()
    {
        try
        {
            long bytes = new FileInfo(FilePath).Length;
            string size = bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.00} GB" : $"{bytes / 1048576.0:0.0} MB";
            Info = size;
            if (!ToolPaths.HasFfmpeg) return;

            Probe = await FfmpegService.ProbeAsync(FilePath);
            var parts = new List<string>();
            if (Probe.Duration is double d) parts.Add(TimeParser.Format(d));
            if (Probe.HasVideo) parts.Add($"{Probe.Width}×{Probe.Height}");
            else if (Probe.HasAudio) parts.Add("audio");
            parts.Add(size);
            Info = string.Join("   •   ", parts);
        }
        catch
        {
            Info = "Couldn't read this file";
        }
    }
}
