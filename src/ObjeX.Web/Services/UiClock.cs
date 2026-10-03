namespace ObjeX.Web.Services;

/// <summary>
/// One clock per circuit for text that ages, such as "12 min ago": a single timer however many cells listen. It runs only
/// while someone listens and stops with the circuit.
/// </summary>
public sealed class UiClock : IDisposable
{
    static readonly TimeSpan Period = TimeSpan.FromSeconds(30);

    readonly Lock _gate = new();
    Timer? _timer;
    Action? _tick;

    public event Action? Tick
    {
        add
        {
            lock (_gate)
            {
                _tick += value;
                _timer ??= new Timer(_ => Raise(), null, Period, Period);
            }
        }
        remove
        {
            lock (_gate)
            {
                _tick -= value;
                if (_tick is not null) return;
                _timer?.Dispose();
                _timer = null;
            }
        }
    }

    // An exception escaping a Timer callback ends the process; the listeners only queue work on their circuit.
    void Raise()
    {
        try { _tick?.Invoke(); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _tick = null;
        }
    }
}
