namespace Pulsar.Services;

public static class Http
{
    public static HttpClient Client { get; } = Create();

    private static HttpClient Create()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Pulsar/1.0 (+https://github.com)");
        return client;
    }

    /// <summary>Downloads to a temp file next to the destination, then moves it into place.</summary>
    public static async Task DownloadFileAsync(string url, string destination, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temp = destination + ".download";

        using (var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(temp);
            var buffer = new byte[81920];
            long read = 0;
            double lastReported = -1;
            int n;
            while ((n = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total is > 0)
                {
                    double pct = read * 100.0 / total.Value;
                    if (pct - lastReported >= 0.5) { lastReported = pct; progress?.Report(pct); }
                }
            }
        }

        File.Move(temp, destination, overwrite: true);
        progress?.Report(100);
    }
}
