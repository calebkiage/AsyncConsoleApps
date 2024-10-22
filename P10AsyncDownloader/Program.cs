// read file with URLs

using System.Collections.Concurrent;
using System.Diagnostics;
using ShellProgressBar;

var sourcesFile = "sources.txt";
if (args.Length > 0)
{
    sourcesFile = args[0];
}

var cts = new CancellationTokenSource();
Console.CancelKeyPress += async (_, eventArgs) =>
{
    // Don't exit the application.
    eventArgs.Cancel = true;
    await cts.CancelAsync();
};

cts.CancelAfter(5000);
using HttpClient sharedClient = new();

var tasks = new List<Task>();
var mainPbOptions = new ProgressBarOptions
{
    CollapseWhenFinished = false,
};
ProgressBar? mainPb = null;
try
{
    var idx = 0;
    // Count lines
    var urls = 0;
    await foreach (var _ in File.ReadLinesAsync(sourcesFile, cts.Token))
    {
        urls++;
    }

    mainPb = new ProgressBar(urls, "Downloading files...", mainPbOptions);
    await foreach (var url in File.ReadLinesAsync(sourcesFile, cts.Token))
    {
        tasks.Add(DownloadFile(++idx, url, sharedClient, mainPb, cts.Token));
    }

    await Task.WhenAll(tasks);
    return 0;
}
catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
{
    Console.WriteLine("Canceled");
    return 2;
}
catch (Exception e)
{
    Console.WriteLine($"Failed to download files: {e.Message}");
    return 1;
}
finally
{
    mainPb?.Dispose();
}

static (double, string) HumanBytes(double bytes)
{
    const double gb = 1024 * 1024 * 1024;
    const double mb = 1024 * 1024;
    const double kb = 1024;
    return bytes switch
    {
        > gb => (bytes / gb, "GiB"),
        > mb => (bytes / mb, "MiB"),
        > kb => (bytes / kb, "KiB"),
        _ => (bytes, "B")
    };
}

async Task DownloadFile(int id, string url, HttpClient client, ProgressBar? progressBar,
    CancellationToken cancellationToken)
{
    var fileName = Path.GetFileName(url);
    if (string.IsNullOrEmpty(fileName))
    {
        fileName = "file.dat";
    }

    var filePath = $"{id}-{fileName}";
    var timer = Stopwatch.StartNew();
    long downloaded = 0;
    long? totalBytes = null;
    double lastSpeed = 0;
    var childOptions = new ProgressBarOptions
    {
        CollapseWhenFinished = false
    };
    IProgressBar? childPb = null;
    IProgress<DownloadProgressInfo>? progress = null;
    try
    {
        timer.Restart();
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        totalBytes = response.Content.Headers.ContentLength;
        childPb = totalBytes.HasValue
            ? progressBar?.Spawn((int)totalBytes.Value, string.Empty, childOptions)
            : progressBar?.SpawnIndeterminate(string.Empty, childOptions);
        // progress = childPb?.AsProgress<DownloadProgressInfo>(GetMessageFromDownloadInfo, GetPercentageFromDownloadInfo);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);

        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(start: 0, length: read), cancellationToken)
                .ConfigureAwait(false);
            downloaded += read;
            if (downloaded % (1024 * 10) != 0) continue;
            lastSpeed = downloaded / timer.Elapsed.TotalSeconds;
            childPb?.Tick((int)downloaded,
                GetMessageFromDownloadInfo(
                    new DownloadProgressInfo(id, downloaded, totalBytes, lastSpeed, false, false)));
        }

        childPb?.Tick((int)downloaded,
            GetMessageFromDownloadInfo(new DownloadProgressInfo(id, downloaded, totalBytes, lastSpeed, false, true)));
        progressBar?.Tick(progressBar.CurrentTick + 1 == progressBar.MaxTicks
            ? "Downloads complete"
            : $"Downloading {progressBar.CurrentTick} of {progressBar.MaxTicks}");
    }
    catch (OperationCanceledException)
    {
        // File.Delete(filePath);
        childPb?.Tick((int)downloaded,
            GetMessageFromDownloadInfo(new DownloadProgressInfo(id, downloaded, totalBytes, lastSpeed, true, false)));
        throw;
    }
    finally
    {
        childPb?.Dispose();
    }

    return;

    static string GetMessageFromDownloadInfo(DownloadProgressInfo info)
    {
        var (downloadedH, unit) = HumanBytes(info.DownloadedBytes);
        var speed = string.Empty;
        if (info.DownloadSpeed.HasValue)
        {
            var (downloadSpdH, unitSpd) = HumanBytes(info.DownloadSpeed.Value);
            speed = $" [{downloadSpdH:F2} {unitSpd}ps]";
        }

        if (info.IsCancelled) return $"{info.Id} download cancelled: {downloadedH:F2} {unit} {speed}";
        if (info.IsCompleted) return $"{info.Id} download completed: {downloadedH:F2} {unit} {speed}";

        var total = info.TotalBytes;
        if (total is not null)
        {
            var (totalH, totalUnit) = HumanBytes(info.DownloadedBytes);
            return $"{info.Id} downloading {downloadedH:F2} {unit} / {totalH:F2} {totalUnit}{speed}";
        }

        return $"{info.Id} downloading {downloadedH:F2} {unit}{speed}";
    }
}

readonly record struct DownloadProgressInfo(
    int Id,
    long DownloadedBytes,
    long? TotalBytes,
    double? DownloadSpeed,
    bool IsCancelled,
    bool IsCompleted);