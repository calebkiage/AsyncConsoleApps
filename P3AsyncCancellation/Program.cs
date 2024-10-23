using System.Diagnostics;

Console.WriteLine("Starting...");
var cts = new CancellationTokenSource();
var isCancelled = false;
var watch = Stopwatch.StartNew();
await Task.WhenAll(CancelAfter(1200, cts, () => isCancelled = true), Sequential(cts.Token), Concurrent(cts.Token));

Console.WriteLine(isCancelled
    ? $"Cancelled after {watch.ElapsedMilliseconds}ms."
    : $"Completed in {watch.ElapsedMilliseconds}ms.");
return;

async Task CancelAfter(int milliseconds, CancellationTokenSource tokenSource, Action callback)
{
    await Task.Delay(milliseconds);
    tokenSource.Cancel();
    callback();
}
async Task Sequential(CancellationToken token = default)
{
    await DoWork("A sequential", 2000, token);
    // Must complete
    await DoWork("B sequential", 1000, token);
    await DoWork("C sequential (non-cancel)", 2500, CancellationToken.None);
    await DoWork("D concurrent", 1000, token);
}

async Task Concurrent(CancellationToken token = default)
{
    await Task.WhenAll(
        DoWork("A concurrent", 2000, token),
        DoWork("B concurrent", 1000, token),
        DoWork("C concurrent (non-cancel)", 2500, CancellationToken.None),
        DoWork("D concurrent", 1000, token)
    );
}

async Task DoWork(string id, int timeMillis, CancellationToken cancellationToken = default)
{
    Console.WriteLine($"{id}: Started task ({timeMillis}ms)");
    try
    {
        await Task.Delay(timeMillis, cancellationToken);
        Console.WriteLine($"{id}: Completed!");
    }
    catch (TaskCanceledException)
    {
        // Console.WriteLine($"{id}: Cancelled :(");
    }
}