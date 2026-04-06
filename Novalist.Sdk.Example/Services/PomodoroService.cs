using System.Timers;

namespace Novalist.Sdk.Example;

/// <summary>
/// Simple Pomodoro timer service.
/// </summary>
public sealed class PomodoroService
{
    private System.Timers.Timer? _timer;
    private DateTime _endTime;

    public bool IsRunning { get; private set; }
    public int DurationMinutes { get; set; } = 25;
    public int SessionCount { get; private set; }
    public int RemainingMinutes => IsRunning ? Math.Max(0, (int)(_endTime - DateTime.UtcNow).TotalMinutes) : 0;
    public int RemainingSeconds => IsRunning ? Math.Max(0, (int)(_endTime - DateTime.UtcNow).Seconds) : 0;

    public event Action? Tick;
    public event Action? SessionCompleted;

    public void Start()
    {
        if (IsRunning) return;

        _endTime = DateTime.UtcNow.AddMinutes(DurationMinutes);
        IsRunning = true;

        _timer = new System.Timers.Timer(1000);
        _timer.Elapsed += OnTick;
        _timer.Start();
    }

    public void Stop()
    {
        IsRunning = false;
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        if (DateTime.UtcNow >= _endTime)
        {
            Stop();
            SessionCount++;
            SessionCompleted?.Invoke();
        }
        else
        {
            Tick?.Invoke();
        }
    }
}
