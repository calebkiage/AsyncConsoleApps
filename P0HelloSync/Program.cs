Console.WriteLine("Starting...");
DoWork(2000);
DoWork(1000);
Console.WriteLine("Done.");
return;

void DoWork(int timeMillis)
{
    Console.WriteLine($"Doing something for {timeMillis}ms");
    Thread.Sleep(timeMillis);
}