using System.Diagnostics;
using ShellProgressBar;

var sourcesFile = "sources.txt";
var batchSize = 5;
if (args.Length > 0)
{
    sourcesFile = args[0];
}

if (args.Length > 1 && int.TryParse(args[1], out var value))
{
    batchSize = value;
}

var cts = new CancellationTokenSource();
Console.CancelKeyPress += async (_, eventArgs) =>
{
    // Don't exit the application.
    eventArgs.Cancel = true;
    await cts.CancelAsync();
};

// cts.CancelAfter(1000);
using HttpClient sharedClient = new();

var tasks = new Task[batchSize];
var mainPbOptions = new ProgressBarOptions
{
    CollapseWhenFinished = true,
    ProgressCharacter = '─',
};
ProgressBar? mainPb = null;
try
{
    // Count lines
    var urls = new List<string>();
    await foreach (var url in File.ReadLinesAsync(sourcesFile, cts.Token))
    {
        if (IsUrlValid(url)) urls.Add(url);
    }

    mainPb = new ProgressBar(urls.Count, $"Downloading 0 of {urls.Count}", mainPbOptions);
    var downloaded = 0;
    var cancelled = 0;
    var failed = 0;
    var nextIdx = 0;
    while (nextIdx < Math.Min(urls.Count, tasks.Length))
    {
        var url = urls[nextIdx];
        tasks[nextIdx++] = DownloadFile(nextIdx, url, sharedClient, mainPb, cts.Token);
    }

    var completedTasks = 0;
    while ((downloaded + cancelled + failed) < urls.Count)
    {
        // Exclude completed tasks
        var trimmed = tasks.Take(tasks.Length - completedTasks);
        var completedTask = await Task.WhenAny(trimmed);
        if (completedTask.IsCompletedSuccessfully)
        {
            downloaded++;
        }
        else if (completedTask.IsCanceled)
        {
            cancelled++;
        }
        else
        {
            failed++;
        }

        var allFinal = downloaded + cancelled + failed;
        mainPb.Tick(allFinal);
        // just completed task
        var finishedIdx = Array.IndexOf(tasks, completedTask);
        
        
        mainPb.Message = allFinal == urls.Count
            ? $"Downloaded {downloaded}, cancelled {cancelled}, failed {failed}"
            : $"Processed {allFinal} of {urls.Count} files";
        
        var lastIndex = tasks.Length - 1;
        if (nextIdx < urls.Count)
        {
            // schedule any unscheduled downloads.
            var url = urls[nextIdx];
            tasks[finishedIdx] = DownloadFile(++nextIdx, url, sharedClient, mainPb, cts.Token);
        }
        else
        {
            // swap finished idx with last, then exclude last
            (tasks[finishedIdx], tasks[lastIndex - completedTasks]) = (tasks[lastIndex - completedTasks], tasks[finishedIdx]);
            completedTasks++;
        }
    }

    return 0;
}
catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
{
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

static bool IsUrlValid(string? url)
{
    if (string.IsNullOrWhiteSpace(url)) return false;
    try
    {
        _ = new Uri(url);
        return true;
    }
    catch (Exception)
    {
        return false;
    }
}

async Task DownloadFile(int id, string url, HttpClient client, ProgressBarBase? progressBar,
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
    IProgressBar? childPb = null;
    try
    {
        timer.Restart();
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        totalBytes = response.Content.Headers.ContentLength;
        childPb = totalBytes.HasValue
            ? progressBar?.Spawn((int)totalBytes.Value, string.Empty)
            : progressBar?.SpawnIndeterminate(string.Empty);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);

        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(start: 0, length: read), cancellationToken)
                .ConfigureAwait(false);
            downloaded += read;
            // if (downloaded % (1024 * 10) != 0) continue;
            lastSpeed = downloaded / timer.Elapsed.TotalSeconds;
            childPb?.Tick((int)downloaded,
                GetMessageFromDownloadInfo(
                    new DownloadProgressInfo(id, downloaded, totalBytes, lastSpeed, false, false)));
        }

        childPb?.Tick((int)downloaded,
            GetMessageFromDownloadInfo(new DownloadProgressInfo(id, downloaded, totalBytes, lastSpeed, false, true)));
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
            var (totalH, totalUnit) = HumanBytes(total.Value);
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