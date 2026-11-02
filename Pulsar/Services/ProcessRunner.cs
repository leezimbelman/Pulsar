using System.Diagnostics;
using System.Text;

namespace Pulsar.Services;

/// <summary>Runs a console tool with no window, streaming stdout/stderr line by line. Callbacks run on background threads.</summary>
public static class ProcessRunner
{
    public static async Task<int> RunAsync(
        string exe,
        IEnumerable<string> args,
        Action<string>? onOutput,
        Action<string>? onError,
        CancellationToken ct = default,
        string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (workingDirectory is not null) psi.WorkingDirectory = workingDirectory;
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";

        using var process = new Process { StartInfo = psi };
        process.Start();

        using var registration = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        });

        await Task.WhenAll(
            PumpAsync(process.StandardOutput, onOutput),
            PumpAsync(process.StandardError, onError));
        await process.WaitForExitAsync(CancellationToken.None);

        ct.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    private static async Task PumpAsync(StreamReader reader, Action<string>? onLine)
    {
        // whisper.cpp and ffmpeg sometimes use bare '\r' to redraw a line; treat it as a line break too.
        var buffer = new char[4096];
        var line = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory())) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                char c = buffer[i];
                if (c is '\n' or '\r')
                {
                    if (line.Length > 0) { onLine?.Invoke(line.ToString()); line.Clear(); }
                }
                else
                {
                    line.Append(c);
                }
            }
        }
        if (line.Length > 0) onLine?.Invoke(line.ToString());
    }
}
