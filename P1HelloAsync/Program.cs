Console.WriteLine("Starting...");
var watch = System.Diagnostics.Stopwatch.StartNew();
await DoWork(2000);
await DoWork(1000);
Console.WriteLine($"Done in {watch.ElapsedMilliseconds}ms.");
return;

async Task DoWork(int timeMillis)
{
    Console.WriteLine($"Doing something for {timeMillis}ms");
    await Task.Delay(timeMillis);
}