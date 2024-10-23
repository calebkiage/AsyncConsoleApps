namespace ConsoleProgressIndicator;

public sealed class ProgressManager : IDisposable
{
    private ProgressIndicator? _rootIndicator;
    private readonly IConsole _console;
    private readonly AutoResetEvent _displayProgressEvent;
    private readonly Task _displayProgress;
    private int _windowHeight;
    private int _cursorTop;
    private int _isDisposed;

    public ProgressManager()
    {
        _console = new StdConsole();
        _windowHeight = _console.WindowHeight;
        (_, _cursorTop) = _console.GetCursorPosition();
        _displayProgressEvent = new AutoResetEvent(false);
        _displayProgress = Task.Run(() =>
        {
            while (_isDisposed == 0)
            {
                if (!_displayProgressEvent.WaitOne(TimeSpan.FromSeconds(10)))
                    continue;
                if (_isDisposed > 0) return;
                try
                {
                    if (_rootIndicator != null) UpdateProgress(_rootIndicator);
                }
                catch
                {
                    //don't want to crash background thread
                }
            }
        });
    }

    public ProgressIndicator RootProgressIndicator(ReadOnlyMemory<char> message, int indent = 0)
    {
        Interlocked.CompareExchange(ref _rootIndicator, new ProgressIndicator(DisplayProgress)
        {
            Message = message,
            Indent = indent
        }, null);
        var rootIndicator = _rootIndicator;
        UpdateProgress(rootIndicator);
        return rootIndicator;
    }

    private void DisplayProgress() => _displayProgressEvent.Set();

    private void UpdateProgress(ProgressIndicator indicator)
    {
        var top = _cursorTop;
        Draw(indicator, ref top);
        _console.SetCursorPosition(0, _cursorTop);
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0) return;
        // make sure background task is stopped before we clean up
        _displayProgressEvent.Set();
        _displayProgress.Wait();
        
        // update one last time - needed because background task might have
        // been already in progress before Dispose was called and it might
        // have been running for a very long time due to poor performance
        // of System.Console
        if (_rootIndicator != null) UpdateProgress(_rootIndicator);

        try
        {
            var newCursorTop = Math.Min(_windowHeight, _cursorTop + _rootIndicator?.Height ?? 0);
            _console.SetCursorPosition(0, newCursorTop);
        }
        catch (Exception)
        {
            // ignored
        }
    }

    private void DrawChildren(IEnumerable<ProgressIndicator> children, ref int cursorTop)
    {
        foreach (var child in children)
        {
            if (cursorTop >= _console.WindowHeight - 2)
            {
                return;
            }

            _console.SetCursorPosition(0, ++cursorTop);
            Draw(child, ref cursorTop);
            DrawChildren(child.Children, ref cursorTop);
        }
    }

    private void Draw(ProgressIndicator indicator, ref int cursorTop)
    {
        const char barChar = '-';
        var width = _console.WindowWidth - indicator.Indent;
        try
        {
            // first line is the bar
            var writeWidth = (ulong)Math.Ceiling(width * indicator.Percentage);
            if (indicator.ShowProgressIndicator)
            {
                // Write progress bar
                ResetRow(_console, _console.WindowWidth);
                WriteIndentPrefix(_console, indicator.Indent);
                for (var i = 0ul; i < writeWidth; i++)
                {
                    _console.Out.Write(barChar);
                }
            }

            _console.SetCursorPosition(0, ++cursorTop);
            // Write message
            ResetRow(_console, _console.WindowWidth);
            WriteIndentPrefix(_console, indicator.Indent);
            _console.Out.Write(indicator.Message);
            DrawChildren(indicator.Children, ref cursorTop);
        }
        catch (Exception)
        {
            // ignored
        }

        return;

        static void WriteIndentPrefix(IConsole console, int indent = 0)
        {
            const char indentChar = '|';
            switch (indent)
            {
                case < 1:
                    return;
                case 1:
                    console.Out.Write(indentChar);
                    break;
                default:
                    console.Out.Write(indentChar);
                    for (var i = 1; i < indent - 1; i++)
                    {
                        console.Out.Write('-');
                    }
                    console.Out.Write(' ');
                    break;
            }
        }

        static void ResetRow(IConsole console, int width)
        {
            for (var i = 0; i < width; i++)
            {
                console.Out.Write(' ');
            }
            console.Out.Write('\r');
        }
    }
}

public readonly record struct ConsoleMessageEntry(int ConsoleLeft, int ConsoleTop, ReadOnlyMemory<char> Prefix, ReadOnlyMemory<char> Message);

public interface IConsole
{
    public int WindowHeight { get; set; }
    public int WindowWidth { get; set; }
    public TextWriter Out { get; }
    public (int Left, int Top) GetCursorPosition();
    public void SetCursorPosition(int left, int top);
}

class StdConsole : IConsole
{
    public StdConsole()
    {
        Console.CursorVisible = false;
    }
    public int WindowHeight
    {
        get => Console.WindowHeight;
        set => Console.WindowHeight = value;
    }
    public int WindowWidth
    {
        get => Console.WindowWidth;
        set => Console.WindowWidth = value;
    }
    public TextWriter Out => Console.Out;
    public (int Left, int Top) GetCursorPosition() => Console.GetCursorPosition();
    public void SetCursorPosition(int left, int top) => Console.SetCursorPosition(left, top);
}