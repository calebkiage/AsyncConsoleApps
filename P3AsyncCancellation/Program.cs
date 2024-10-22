using System.Diagnostics;

Console.WriteLine("Starting...");
var cts = new CancellationTokenSource();
var watch = Stopwatch.StartNew();
var isCancelled = false;
Console.CancelKeyPress += async (sender, eventArgs) =>
{
    // Don't exit the application.
    eventArgs.Cancel = true;
    isCancelled = true;
    var key = eventArgs.SpecialKey switch
    {
        ConsoleSpecialKey.ControlBreak => "Ctrl+Break",
        ConsoleSpecialKey.ControlC => "Ctrl+C",
        _ => throw new ArgumentOutOfRangeException(nameof(eventArgs), eventArgs, "Invalid special key.")
    };
    Console.WriteLine($"Cancelling via {key}");
    await cts.CancelAsync();
};
// Exclude the setup code timings
watch.Restart();

await Task.WhenAll(Sequential(cts.Token), Concurrent(cts.Token));

Console.WriteLine(isCancelled
    ? $"Cancelled after {watch.ElapsedMilliseconds}ms."
    : $"Completed in {watch.ElapsedMilliseconds}ms.");
return;

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