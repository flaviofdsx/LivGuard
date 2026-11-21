namespace Flavirus.Core.Watchdog;

public class Watchdog : IDisposable
{
    private readonly Timer _timer;
    private readonly Func<bool> _healthCheck;
    private readonly Action _repair;
    private readonly int _intervalMs;

    public Watchdog(Func<bool> healthCheck, Action repair, int intervalMs = 5000)
    {
        _healthCheck = healthCheck;
        _repair = repair;
        _intervalMs = intervalMs;
        _timer = new Timer(_ => Tick(), null, intervalMs, intervalMs);
    }

    private void Tick()
    {
        try { if (!_healthCheck()) _repair(); } catch { }
    }

    public void Dispose() => _timer.Dispose();
}
