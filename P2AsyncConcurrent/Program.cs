var watch0 = System.Diagnostics.Stopwatch.StartNew();
try
{
    await Task.WhenAll(RunTaskAsync(1000), RunTaskAsync(2000), FailAsync(1500), FailAsync(1600));
    Console.WriteLine($"Done0 in {watch0.ElapsedMilliseconds}ms.");
}
catch (Exception e)
{
    Console.WriteLine($"Failed0 in {watch0.ElapsedMilliseconds}ms.\n{e}");
}

var watch1 = System.Diagnostics.Stopwatch.StartNew();
try
{
    await WhenAllManualAsync(RunTaskAsync(1000), RunTaskAsync(2000), FailAsync(1500));
    Console.WriteLine($"Done1 in {watch1.ElapsedMilliseconds}ms.");
}
catch (Exception e)
{
    Console.WriteLine($"Failed1 in {watch1.ElapsedMilliseconds}ms.\n{e}");
}

return;

async Task RunTaskAsync(int timeMillis)
{
    await Task.Delay(timeMillis);
    Console.WriteLine($"Ran task for {timeMillis}ms");
}

async Task FailAsync(int afterMillis)
{
    await Task.Delay(afterMillis);
    throw new Exception("Sorry, I failed.");
}

Task WhenAllManualAsync(params Task[] tasks)
{
    var completion = new TaskCompletionSource();
    var completedCount = 0;
    var anyCancelled = 0;
    var exceptions = new List<Exception>();
    var mLockCmp = new object();

    foreach (var task in tasks)
    {
        task.ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                lock (mLockCmp)
                {
                    exceptions.AddRange(t.Exception.InnerExceptions);
                }
            }
            else if (t.IsCanceled)
            {
                lock (mLockCmp)
                {
                    anyCancelled = 1;
                }
            }
            else if (t.IsCompleted)
            {
                lock (mLockCmp)
                {
                    completedCount++;
                }
            }

            lock (mLockCmp)
            {
                if (completedCount + exceptions.Count >= tasks.Length)
                {
                    if (exceptions.Count > 0)
                    {
                        // Any failed.
                        completion.SetException(exceptions);
                    }
                    else
                    {
                        // All completed
                        completion.SetResult();
                    }
                }
                else if (anyCancelled > 0)
                {
                    // Any cancelled
                    completion.SetCanceled();
                }
            }
        });
    }

    return completion.Task;
}