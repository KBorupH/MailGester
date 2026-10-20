namespace MailGester.Runtime;

/// <summary>Lets the API wake the scheduler loop, either for an immediate run or because settings changed.</summary>
public sealed class RunCoordinator
{
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private int _runNow;

    public void WakeForSettings() => _wake.Release();

    public void RequestRunNow()
    {
        Interlocked.Exchange(ref _runNow, 1);
        _wake.Release();
    }

    public bool ConsumeRunNow() => Interlocked.Exchange(ref _runNow, 0) == 1;

    /// <summary>Returns true when woken, false when the wait simply timed out.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _wake.WaitAsync(timeout, ct);
}

public sealed record RunStatusSnapshot(
    bool Running,
    string Stage,
    DateTimeOffset? StartedUtc,
    string? LastError,
    DateTimeOffset? LastRunUtc,
    DateTimeOffset? NextRunUtc);

public sealed class RunStatus
{
    private readonly object _gate = new();
    private bool _running;
    private string _stage = "Idle";
    private DateTimeOffset? _started;
    private string? _lastError;
    private DateTimeOffset? _lastRun;
    private DateTimeOffset? _nextRun;

    public void Begin()
    {
        lock (_gate)
        {
            _running = true;
            _stage = "Starting";
            _started = DateTimeOffset.UtcNow;
            _lastError = null;
        }
    }

    public void SetStage(string stage)
    {
        lock (_gate)
        {
            _stage = stage;
        }
    }

    public void Finish(string? error)
    {
        lock (_gate)
        {
            _running = false;
            _stage = "Idle";
            _lastError = error;
        }
    }

    public void SetSchedule(DateTimeOffset? lastRun, DateTimeOffset? nextRun)
    {
        lock (_gate)
        {
            _lastRun = lastRun;
            _nextRun = nextRun;
        }
    }

    public RunStatusSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new RunStatusSnapshot(_running, _stage, _started, _lastError, _lastRun, _nextRun);
        }
    }
}
