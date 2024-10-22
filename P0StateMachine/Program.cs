var enumerator = new FibonacciStateMachine();
while (enumerator.MoveNext())
{
    if (enumerator.X >= 10) break;
    Console.Write(enumerator.Current);
    Console.Write(' ');
}

Console.WriteLine();

foreach (var i in Fib().Take(10))
{
    Console.Write(i);
    Console.Write(' ');
}

return;

IEnumerable<int> Fib()
{
    int prev = 0, next = 1;
    yield return prev;
    yield return next;

    while (true)
    {
        int sum = prev + next;
        yield return sum;
        prev = next;
        next = sum;
    }
}

struct FibonacciStateMachine
{
    private int _prev = 0;

    public FibonacciStateMachine()
    {
    }

    public int X { get; private set; } = -1;
    public int Current { get; private set; }

    public bool MoveNext()
    {
        switch (++X)
        {
            case 0:
            {
                Current = 0;
                return true;
            }
            case 1:
            {
                _prev = Current;
                Current = 1;
                return true;
            }
            default:
            {
                var sum = _prev + Current;
                _prev = Current;
                Current = sum;
                return true;
            }
        }
    }
}
