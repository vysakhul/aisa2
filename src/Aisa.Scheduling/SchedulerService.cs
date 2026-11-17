namespace Aisa.Scheduling;

public sealed class SchedulerService : IAsyncDisposable
{
    private readonly IScheduleStore _store;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private List<ScheduledItem> _items = [];
    private volatile IReadOnlyList<ScheduledItem> _snapshot = [];
    private CancellationTokenSource? _wait;
    private readonly object _waitGate = new();
    private Task? _loop;
    private long _ticks;

    public SchedulerService(IScheduleStore store, TimeProvider time, SchedulerOptions? options = null)
    {
        _store = store;
        _time = time;
        Options = options ?? new SchedulerOptions();
    }

    public SchedulerOptions Options { get; }
    public IReadOnlyList<ScheduledItem> Snapshot => _snapshot;
    public IReadOnlyList<ScheduledItem> ActiveAlerts =>
        _snapshot.Where(i => i.State is ScheduleState.Ringing or ScheduleState.Missed)
                 .OrderBy(i => i.DueAt).ToList();

    public event Action<IReadOnlyList<ScheduledItem>>? Changed;
    public event Action<ScheduledItem>? Due;
    public event Action<ScheduledItem>? AutoSnoozed;

    /// <summary>Number of completed loop passes; lets tests wait deterministically.</summary>
    internal long Ticks => Interlocked.Read(ref _ticks);

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_loop is not null) return;
        var loaded = await _store.LoadAsync(ct).ConfigureAwait(false);
        var events = new Events();
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = _time.GetUtcNow();
            _items = loaded.ToList();
            var missed = new List<ScheduledItem>();
            for (var i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                bool miss = it.State == ScheduleState.Ringing
                    || (it.State == ScheduleState.Scheduled && now - it.DueAt > Options.Grace);
                if (miss)
                {
                    _items[i] = it with { State = ScheduleState.Missed, RingingSince = null };
                    missed.Add(_items[i]);
                }
            }
            events.Due.AddRange(missed.OrderBy(m => m.DueAt));
            await PersistLocked(now, ct).ConfigureAwait(false);
            // Items overdue by <= Grace ring immediately.
            ProcessLocked(now, events);
            if (events.Due.Count > 0) await PersistLocked(now, ct).ConfigureAwait(false);
            events.Changed = _snapshot;
        }
        finally { _lock.Release(); }
        Raise(events);
        _loop = Task.Run(() => RunLoop(_stop.Token));
    }

    public async Task<ScheduledItem> AddAsync(ReminderKind kind, string label, DateTimeOffset dueAt, CancellationToken ct = default)
    {
        ScheduledItem item;
        Events events = new();
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = _time.GetUtcNow();
            if (dueAt <= now) throw new ScheduleException("That time has already passed");
            item = new ScheduledItem(Guid.NewGuid(), kind, label, dueAt, ScheduleState.Scheduled, now);
            _items.Add(item);
            await PersistLocked(now, ct).ConfigureAwait(false);
            events.Changed = _snapshot;
        }
        finally { _lock.Release(); }
        Raise(events);
        RecheckNow();
        return item;
    }

    public Task DismissAsync(Guid id, CancellationToken ct = default) =>
        Mutate(id, it => it.State is ScheduleState.Ringing or ScheduleState.Missed or ScheduleState.Scheduled
            ? it with { State = ScheduleState.Done, RingingSince = null } : null, ct);

    public Task SnoozeAsync(Guid id, TimeSpan? length = null, CancellationToken ct = default)
    {
        var len = length ?? Options.SnoozeLength;
        return Mutate(id, (it, now) => it.State == ScheduleState.Done ? null
            : it with { State = ScheduleState.Scheduled, DueAt = now + len, RingingSince = null, SnoozeCount = it.SnoozeCount + 1 }, ct);
    }

    public async Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var events = new Events();
        bool removed;
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var idx = _items.FindIndex(i => i.Id == id && i.State != ScheduleState.Done);
            removed = idx >= 0;
            if (removed)
            {
                _items.RemoveAt(idx);
                await PersistLocked(_time.GetUtcNow(), ct).ConfigureAwait(false);
                events.Changed = _snapshot;
            }
        }
        finally { _lock.Release(); }
        Raise(events);
        if (removed) RecheckNow();
        return removed;
    }

    public void RecheckNow()
    {
        lock (_waitGate) { try { _wait?.Cancel(); } catch (ObjectDisposedException) { } }
    }

    public async ValueTask DisposeAsync()
    {
        if (_stop.IsCancellationRequested) return;
        _stop.Cancel();
        RecheckNow();
        if (_loop is not null) { try { await _loop.ConfigureAwait(false); } catch (OperationCanceledException) { } }
        _stop.Dispose();
    }

    private Task Mutate(Guid id, Func<ScheduledItem, ScheduledItem?> f, CancellationToken ct) =>
        Mutate(id, (it, _) => f(it), ct);

    private async Task Mutate(Guid id, Func<ScheduledItem, DateTimeOffset, ScheduledItem?> f, CancellationToken ct)
    {
        var events = new Events();
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var idx = _items.FindIndex(i => i.Id == id);
            if (idx < 0) return;
            var now = _time.GetUtcNow();
            var updated = f(_items[idx], now);
            if (updated is null) return;
            _items[idx] = updated;
            await PersistLocked(now, ct).ConfigureAwait(false);
            events.Changed = _snapshot;
        }
        finally { _lock.Release(); }
        Raise(events);
        RecheckNow();
    }

    private sealed class Events
    {
        public List<ScheduledItem> Due { get; } = [];
        public List<ScheduledItem> Snoozed { get; } = [];
        public IReadOnlyList<ScheduledItem>? Changed { get; set; }
    }

    private void Raise(Events e)
    {
        foreach (var d in e.Due) Safe(() => Due?.Invoke(d));
        foreach (var s in e.Snoozed) Safe(() => AutoSnoozed?.Invoke(s));
        if (e.Changed is not null) Safe(() => Changed?.Invoke(e.Changed));
    }

    private static void Safe(Action a) { try { a(); } catch { } }

    private async Task PersistLocked(DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now - Options.KeepDoneFor;
        _items.RemoveAll(i => i.State == ScheduleState.Done && i.DueAt < cutoff);
        _snapshot = _items.OrderBy(i => i.DueAt).ToList();
        await _store.SaveAsync(_snapshot, ct).ConfigureAwait(false);
    }

    // Applies due transitions; returns true if anything changed.
    private bool ProcessLocked(DateTimeOffset now, Events ev)
    {
        var changed = false;
        foreach (var it in _items.Where(i => i.State == ScheduleState.Scheduled && i.DueAt <= now)
                                 .OrderBy(i => i.DueAt).ToList())
        {
            var idx = _items.FindIndex(x => x.Id == it.Id);
            _items[idx] = now - it.DueAt <= Options.Grace
                ? it with { State = ScheduleState.Ringing, RingingSince = now }
                : it with { State = ScheduleState.Missed, RingingSince = null };
            ev.Due.Add(_items[idx]);
            changed = true;
        }
        foreach (var it in _items.Where(i => i.State == ScheduleState.Ringing && i.Kind == ReminderKind.Alarm
                     && i.RingingSince is { } rs && now - rs >= Options.AlarmAutoSnoozeAfter).ToList())
        {
            var idx = _items.FindIndex(x => x.Id == it.Id);
            _items[idx] = it with
            {
                State = ScheduleState.Scheduled, RingingSince = null,
                DueAt = now + Options.SnoozeLength, SnoozeCount = it.SnoozeCount + 1,
            };
            ev.Snoozed.Add(_items[idx]);
            changed = true;
        }
        if (changed) _snapshot = _items.OrderBy(i => i.DueAt).ToList();
        return changed;
    }

    private async Task RunLoop(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            TimeSpan delay;
            var events = new Events();
            try
            {
                await _lock.WaitAsync(stop).ConfigureAwait(false);
                try
                {
                    var now = _time.GetUtcNow();
                    if (ProcessLocked(now, events))
                    {
                        await PersistLocked(now, stop).ConfigureAwait(false);
                        events.Changed = _snapshot;
                    }
                    delay = NextDelay(now);
                    // Register the wait token before releasing the lock so RecheckNow can't be missed.
                    var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
                    lock (_waitGate) { _wait?.Dispose(); _wait = cts; }
                }
                finally { _lock.Release(); }
                Raise(events);
                Interlocked.Increment(ref _ticks);
                CancellationTokenSource? w;
                lock (_waitGate) w = _wait;
                try { await Task.Delay(delay, _time, w!.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!stop.IsCancellationRequested) { }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception) when (!stop.IsCancellationRequested)
            {
                try { await Task.Delay(Options.MaxRecheckInterval, _time, stop).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private TimeSpan NextDelay(DateTimeOffset now)
    {
        var delay = Options.MaxRecheckInterval;
        foreach (var it in _items)
        {
            DateTimeOffset? at = it.State switch
            {
                ScheduleState.Scheduled => it.DueAt,
                ScheduleState.Ringing when it.Kind == ReminderKind.Alarm && it.RingingSince is { } rs => rs + Options.AlarmAutoSnoozeAfter,
                _ => null,
            };
            if (at is { } t) delay = TimeSpan.FromTicks(Math.Min(delay.Ticks, Math.Max(0, (t - now).Ticks)));
        }
        return delay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : delay;
    }
}
