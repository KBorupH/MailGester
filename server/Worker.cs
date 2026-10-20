using MailGester.Data;
using MailGester.Runtime;

namespace MailGester;

public sealed class Worker(
    DigestRunner runner,
    SettingsService settings,
    RunCoordinator coordinator,
    RunStatus status,
    ILogger<Worker> log) : BackgroundService
{
    private const int MaxAttempts = 3;

    /// <summary>Upper bound on sleeping, so accounts added in the UI are noticed without needing a wake up.</summary>
    private static readonly TimeSpan MaxSleep = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(10);

    private readonly DigestRunner _runner = runner;
    private readonly SettingsService _settings = settings;
    private readonly RunCoordinator _coordinator = coordinator;
    private readonly RunStatus _status = status;
    private readonly ILogger<Worker> _log = log;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _settings.Changed += _coordinator.WakeForSettings;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var sleep = await TickAsync(ct);
                await _coordinator.WaitAsync(sleep, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down.
        }
        finally
        {
            _settings.Changed -= _coordinator.WakeForSettings;
        }
    }

    private async Task<TimeSpan> TickAsync(CancellationToken ct)
    {
        try
        {
            var d = _settings.Current.Digest;
            var zone = TimeZoneInfo.FindSystemTimeZoneById(d.TimeZone);
            var runAt = TimeOnly.Parse(d.RunAt);

            if (_coordinator.ConsumeRunNow())
            {
                await RunOnceAsync(ct);
            }
            else if (d.SchedulerEnabled && await _runner.IsConfiguredAsync(ct))
            {
                // The PC may be off at the scheduled time. A run is due when the last success is older
                // than the most recent scheduled slot, which also covers the very first start.
                var lastDue = Occurrence(DateTimeOffset.UtcNow, zone, runAt, previous: true);
                var lastRun = await _runner.GetLastRunAsync(ct);

                if (lastRun is null || lastRun < lastDue)
                {
                    await RunWithRetriesAsync(ct);
                }
            }

            var next = d.SchedulerEnabled ? Occurrence(DateTimeOffset.UtcNow, zone, runAt, previous: false) : (DateTimeOffset?)null;
            _status.SetSchedule(await _runner.GetLastRunAsync(ct), next);

            var wait = next is { } n ? n - DateTimeOffset.UtcNow : MaxSleep;
            return wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait > MaxSleep ? MaxSleep : wait;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Scheduler tick failed");
            return TimeSpan.FromMinutes(1);
        }
    }

    /// <summary>A manual run reports its error in the UI instead of retrying or sending a degraded post.</summary>
    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await _runner.RunAsync(aiFallback: false, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Manual digest failed");
        }
    }

    private async Task RunWithRetriesAsync(CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                // Only the last attempt may fall back to a degraded post.
                await _runner.RunAsync(aiFallback: attempt == MaxAttempts, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Digest attempt {Attempt} of {Max} failed", attempt, MaxAttempts);

                if (attempt < MaxAttempts)
                {
                    await Task.Delay(RetryDelay, ct);
                }
            }
        }
    }

    private static DateTimeOffset Occurrence(DateTimeOffset now, TimeZoneInfo zone, TimeOnly runAt, bool previous)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        var candidate = local.Date.Add(runAt.ToTimeSpan());

        if (previous && candidate > local)
        {
            candidate = candidate.AddDays(-1);
        }
        else if (!previous && candidate <= local)
        {
            candidate = candidate.AddDays(1);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
    }
}
