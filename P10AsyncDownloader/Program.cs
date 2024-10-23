using System.Diagnostics;
using System.Text;
using ConsoleProgressIndicator;

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
using var pm = new ProgressManager();
try
{
    // Count lines
    var urls = new List<string>();
    await foreach (var url in File.ReadLinesAsync(sourcesFile, cts.Token))
    {
        if (IsUrlValid(url)) urls.Add(url);
    }

    var mainPb = pm.RootProgressIndicator($"0 downloaded, 0 cancelled, 0 failed, 0 incomplete, {urls.Count} unused of {urls.Count}".AsMemory());
    mainPb.Total = (ulong)urls.Count;
    // Track stats
    var downloaded = 0;
    var cancelled = 0;
    var failed = 0;
    var incomplete = 0;
    var completed = 0ul;
    // unused URLs
    var nextUnusedUrlIdx = 0;
    // Queue up the first batch of downloads.
    while (!cts.IsCancellationRequested && nextUnusedUrlIdx < Math.Min(urls.Count, tasks.Length))
    {
        var url = urls[nextUnusedUrlIdx];
        tasks[nextUnusedUrlIdx++] = DownloadFile(nextUnusedUrlIdx, url, sharedClient, mainPb, cts.Token);
        incomplete++;
    }

    // While we haven't processed all files, wait for any task to complete then add any unprocessed urls to the just
    // completed slot.
    // If there are no more urls to process, wait on just the incomplete tasks
    while (!cts.IsCancellationRequested && downloaded + cancelled + failed < urls.Count)
    {
        // Exclude completed tasks
        var filtered = tasks.Where(static t => !t.IsCompleted).ToArray();
        if (filtered.Length == 0) continue;
        var completedTask = await Task.WhenAny(filtered);
        completed++;
        incomplete--;
        switch (completedTask.Status)
        {
            case TaskStatus.RanToCompletion:
                downloaded++;
                break;
            case TaskStatus.Canceled:
                cancelled++;
                break;
            case TaskStatus.Faulted:
                failed++;
                break;
            case TaskStatus.Created:
            case TaskStatus.WaitingForActivation:
            case TaskStatus.WaitingToRun:
            case TaskStatus.Running:
            case TaskStatus.WaitingForChildrenToComplete:
            default:
                break;
        }

        // just completed task
        var finishedIdx = Array.IndexOf(tasks, completedTask);
        mainPb.Message =
            $"{downloaded} downloaded, {cancelled} cancelled, {failed} failed, {incomplete} incomplete, {urls.Count - nextUnusedUrlIdx} unused of {urls.Count}".AsMemory();
        mainPb.Tick(completed);

        if (nextUnusedUrlIdx < urls.Count)
        {
            // download any unprocessed URLs.
            var url = urls[nextUnusedUrlIdx];
            tasks[finishedIdx] = DownloadFile(++nextUnusedUrlIdx, url, sharedClient, mainPb, cts.Token);
            incomplete++;
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

async Task DownloadFile(int id, string url, HttpClient client, ProgressIndicator? progressBar,
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
    ProgressIndicator? childPb = null;
    var sb = new StringBuilder();
    try
    {
        timer.Restart();
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        totalBytes = response.Content.Headers.ContentLength;
        childPb = totalBytes.HasValue
            ? progressBar?.Spawn((ulong)totalBytes.Value)
            : progressBar?.SpawnIndeterminate();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);

        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(start: 0, length: read), cancellationToken)
                .ConfigureAwait(false);
            downloaded += read;
            lastSpeed = downloaded / timer.Elapsed.TotalSeconds;
            childPb?.Tick((ulong)downloaded,
                GetMessageFromDownloadInfo(sb,
                    new DownloadProgressInfo(id, downloaded, totalBytes, lastSpeed, false, false)).AsMemory());
        }

        childPb?.Tick((ulong)downloaded,
            GetMessageFromDownloadInfo(sb, new DownloadProgressInfo(id, downloaded, totalBytes, lastSpeed, false, true)).AsMemory());
    }
    catch (OperationCanceledException)
    {
        childPb?.Tick((ulong)downloaded,
            GetMessageFromDownloadInfo(sb, new DownloadProgressInfo(id, downloaded, totalBytes, lastSpeed, true, false)).AsMemory());
        throw;
    }
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

static string GetMessageFromDownloadInfo(StringBuilder buffer, DownloadProgressInfo info)
{
    // Use string builder as function is called a lot
    buffer.Clear();
    buffer.Append(info.Id);
    var (downloadedH, unit) = HumanBytes(info.DownloadedBytes);
    Span<char> numberFormatted = stackalloc char[10];
    var success = downloadedH.TryFormat(numberFormatted, out var written, "F2");
    Debug.Assert(success);
    if (info.IsCancelled)
    {
        buffer.Append(" download cancelled: ");
        buffer.Append(numberFormatted[..written]);
        buffer.Append(' ');
        buffer.Append(unit);
    }
    else if (info.IsCompleted)
    {
        buffer.Append(" download completed: ");
        buffer.Append(numberFormatted[..written]);
        buffer.Append(' ');
        buffer.Append(unit);
    }
    else
    {
        buffer.Append(" downloading ");
        buffer.Append(numberFormatted[..written]);
        buffer.Append(' ');
        buffer.Append(unit);
        var total = info.TotalBytes;
        if (total is not null)
        {
            var (totalH, totalUnit) = HumanBytes(total.Value);
            success = totalH.TryFormat(numberFormatted, out written, "F2");
            Debug.Assert(success);
            buffer.Append(" / ");
            buffer.Append(numberFormatted[..written]);
            buffer.Append(' ');
            buffer.Append(totalUnit);
        }
    }

    if (!info.DownloadSpeed.HasValue) return buffer.ToString();

    var (downloadSpdH, spdUnit) = HumanBytes(info.DownloadSpeed.Value);
    success = downloadSpdH.TryFormat(numberFormatted, out written, "F2");
    Debug.Assert(success);
    buffer.Append(" [");
    buffer.Append(numberFormatted[..written]);
    buffer.Append(' ');
    buffer.Append(spdUnit);
    buffer.Append("ps]");
    // Allocates a lot
    return buffer.ToString();
}

internal readonly record struct DownloadProgressInfo(
    int Id,
    long DownloadedBytes,
    long? TotalBytes,
    double? DownloadSpeed,
    bool IsCancelled,
    bool IsCompleted);