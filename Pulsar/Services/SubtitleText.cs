using System.Text;
using System.Text.RegularExpressions;

namespace Pulsar.Services;

/// <summary>Converts SRT subtitles to WebVTT or plain text.</summary>
public static partial class SubtitleText
{
    [GeneratedRegex(@"^\d+$")]
    private static partial Regex IndexLine();

    public static string SrtToVtt(string srt)
    {
        var sb = new StringBuilder("WEBVTT").AppendLine().AppendLine();
        foreach (var raw in srt.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (IndexLine().IsMatch(line)) continue;
            sb.AppendLine(line.Contains("-->") ? line.Replace(',', '.') : line);
        }
        return sb.ToString();
    }

    public static string SrtToText(string srt)
    {
        var sb = new StringBuilder();
        foreach (var raw in srt.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || IndexLine().IsMatch(line) || line.Contains("-->")) continue;
            sb.AppendLine(line);
        }
        return sb.ToString();
    }
}
