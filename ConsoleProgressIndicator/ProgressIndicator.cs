namespace ConsoleProgressIndicator;

public class ProgressIndicator(Action _scheduleDraw)
{
    private readonly List<ProgressIndicator> _childIndicators = [];
    private DateTime _startTime = DateTime.Now;
    private ulong _current;
    private ReadOnlyMemory<char> _message;
    private object _msgLock = new();

    public ulong Current => _current;

    public ulong Total { get; set; }
    public float Percentage => (float)Current / Total;
    public bool ShowProgressIndicator { get; set; } = true;
    public int Indent { get; set; } = 0;
    public List<ProgressIndicator> Children => _childIndicators;
    public int Height
    {
        get
        {
            var thisHeight = ShowProgressIndicator ? 2 : 1;
            return thisHeight + _childIndicators.Select(static x=> x.Height).Sum();
        }
    }

    public ReadOnlyMemory<char> Message
    {
        get => _message;
        set
        {
            lock (_msgLock)
            {
                _message = value;
            }
        }
    }

    public void Tick() => FinishTick(Increment());

    public void Tick(ulong value)
    {
        var valueChanged = Interlocked.Exchange(ref _current, value) != value;
        FinishTick(valueChanged);
    }
    public void Tick(ulong value, ReadOnlyMemory<char> message)
    {
        var valueChanged = Interlocked.Exchange(ref _current, value) != value;
        FinishTick(valueChanged, message);
    }
    public void Tick(ReadOnlyMemory<char> text) => FinishTick(Increment(), text);

    public ProgressIndicator Spawn(ulong total = 0)
    {
        var indicator = CreateIndicator(true);
        indicator.Total = total;
        return indicator;
    }
    public ProgressIndicator SpawnIndeterminate() => CreateIndicator(false);

    private ProgressIndicator CreateIndicator(bool withBar)
    {
        var indicator = new ProgressIndicator(_scheduleDraw)
        {
            Indent = Indent + 2,
            ShowProgressIndicator = withBar,
        };
        _childIndicators.Add(indicator);
        _scheduleDraw();
        return indicator;
    }
    private bool Increment()
    {
        var val = Interlocked.Increment(ref _current);
        if (val < Total) return true;
        // Clamp
        Interlocked.Exchange(ref _current, Total);
        return false;
    }

    private void FinishTick(bool valueChanged, ReadOnlyMemory<char>? message = null)
    {
        if (message != null)
        {
            valueChanged = true;
            Message = message.Value;
        }
        if (valueChanged) _scheduleDraw();
    }
}