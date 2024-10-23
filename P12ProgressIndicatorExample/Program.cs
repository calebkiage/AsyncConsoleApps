using ConsoleProgressIndicator;

var pm = new ProgressManager();

var rootPb = pm.RootProgressIndicator("Downloading files...".AsMemory());
rootPb.Total = 3;
// await RunChild(2000, rootPb);
// await RunChild(2300, rootPb);
// await RunChild(1500, rootPb);
await Task.WhenAll(RunChild(2000, rootPb), RunChild(2300, rootPb), RunChild(1500, rootPb));

pm.Dispose();

static Task RunChild(int durationMillis, ProgressIndicator rootPb)
{
    return Task.Run(async () =>
    {
        var tickDuration = durationMillis / 100;
        var pb = rootPb.Spawn();
        pb.Message = $"Duration: {durationMillis}".AsMemory();
        pb.Total = 100;
        for (var i = 0; i < 100; i++)
        {
            await Task.Delay(tickDuration).ConfigureAwait(false);
            pb.Tick($"Finished {tickDuration * i} of {durationMillis}".AsMemory());
        }
        pb.Tick("Done".AsMemory());
        rootPb.Tick();
    });
}