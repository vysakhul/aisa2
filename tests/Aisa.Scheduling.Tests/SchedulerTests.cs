using Xunit;
using Microsoft.Extensions.Time.Testing;

namespace Aisa.Scheduling.Tests;

public sealed class SchedulerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly InMemoryScheduleStore _store = new();
    private readonly List<ScheduledItem> _due = [];
    private readonly List<ScheduledItem> _snoozed = [];
    private SchedulerService _svc = null!;

    public Task InitializeAsync() { _svc = Make(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await _svc.DisposeAsync();

    private SchedulerService Make()
    {
        var s = new SchedulerService(_store, _time);
        s.Due += i => { lock (_due) _due.Add(i); };
        s.AutoSnoozed += i => { lock (_snoozed) _snoozed.Add(i); };
        return s;
    }

    // Waits until the loop has finished a pass after the current moment, with the delay registered.
    private async Task SettleAsync()
    {
        for (var n = 0; n < 2000; n++)
        {
            var before = _svc.Ticks;
            _svc.RecheckNow();
            for (var k = 0; k < 2000 && _svc.Ticks == before; k++) await Task.Delay(1);
            if (_svc.Ticks > before) return;
        }
        throw new TimeoutException();
    }

    private async Task AdvanceAsync(TimeSpan by)
    {
        var end = _time.GetUtcNow() + by;
        while (_time.GetUtcNow() < end)
        {
            var step = TimeSpan.FromTicks(Math.Min((end - _time.GetUtcNow()).Ticks, TimeSpan.FromSeconds(10).Ticks));
            _time.Advance(step);
            await SettleAsync();
        }
    }

    private ScheduledItem Get(Guid id) => _svc.Snapshot.Single(i => i.Id == id);

    [Fact]
    public async Task Add_rejects_past_and_now()
    {
        await _svc.StartAsync();
        var ex = await Assert.ThrowsAsync<ScheduleException>(() => _svc.AddAsync(ReminderKind.Alarm, "x", T0));
        Assert.Equal("That time has already passed", ex.Message);
        await Assert.ThrowsAsync<ScheduleException>(() => _svc.AddAsync(ReminderKind.Alarm, "x", T0.AddMinutes(-1)));
    }

    [Fact]
    public async Task Add_persists_and_raises_Changed()
    {
        await _svc.StartAsync();
        IReadOnlyList<ScheduledItem>? seen = null;
        _svc.Changed += l => seen = l;
        var item = await _svc.AddAsync(ReminderKind.Reminder, "tea", T0.AddMinutes(5));
        Assert.Single(seen!);
        Assert.Equal(item, (await _store.LoadAsync()).Single());
    }

    [Fact]
    public async Task Rings_at_due_time()
    {
        await _svc.StartAsync();
        var item = await _svc.AddAsync(ReminderKind.Reminder, "tea", T0.AddMinutes(1));
        await SettleAsync();
        Assert.Empty(_due);
        await AdvanceAsync(TimeSpan.FromMinutes(1));
        var cur = Get(item.Id);
        Assert.Equal(ScheduleState.Ringing, cur.State);
        Assert.Equal(_time.GetUtcNow(), cur.RingingSince);
        Assert.Single(_due);
    }

    [Fact]
    public async Task Long_sleep_beyond_grace_is_missed()
    {
        await _svc.StartAsync();
        var item = await _svc.AddAsync(ReminderKind.Reminder, "tea", T0.AddMinutes(1));
        await SettleAsync();
        _time.Advance(TimeSpan.FromHours(2));
        _svc.RecheckNow();
        await SettleAsync();
        Assert.Equal(ScheduleState.Missed, Get(item.Id).State);
        Assert.Equal(ScheduleState.Missed, _due.Single().State);
    }

    [Fact]
    public async Task Slightly_late_within_grace_rings()
    {
        await _svc.StartAsync();
        var item = await _svc.AddAsync(ReminderKind.Reminder, "tea", T0.AddMinutes(1));
        await SettleAsync();
        _time.Advance(TimeSpan.FromSeconds(60 + 100));
        await SettleAsync();
        Assert.Equal(ScheduleState.Ringing, Get(item.Id).State);
    }

    [Fact]
    public async Task Startup_marks_overdue_missed_and_ringing_missed()
    {
        var old = new ScheduledItem(Guid.NewGuid(), ReminderKind.Reminder, "old", T0.AddMinutes(-30), ScheduleState.Scheduled, T0.AddHours(-1));
        var ringing = new ScheduledItem(Guid.NewGuid(), ReminderKind.Alarm, "ring", T0.AddMinutes(-10), ScheduleState.Ringing, T0.AddHours(-1), T0.AddMinutes(-10));
        var recent = new ScheduledItem(Guid.NewGuid(), ReminderKind.Reminder, "recent", T0.AddSeconds(-60), ScheduleState.Scheduled, T0.AddHours(-1));
        var future = new ScheduledItem(Guid.NewGuid(), ReminderKind.Reminder, "later", T0.AddHours(1), ScheduleState.Scheduled, T0.AddHours(-1));
        await _store.SaveAsync([future, recent, old, ringing]);
        await _svc.StartAsync();

        Assert.Equal(ScheduleState.Missed, Get(old.Id).State);
        Assert.Equal(ScheduleState.Missed, Get(ringing.Id).State);
        Assert.Equal(ScheduleState.Ringing, Get(recent.Id).State);
        Assert.Equal(ScheduleState.Scheduled, Get(future.Id).State);
        Assert.Equal(["old", "ring", "recent"], _due.Select(d => d.Label).ToArray());
        Assert.Equal(ScheduleState.Missed, (await _store.LoadAsync()).Single(i => i.Id == ringing.Id).State);
    }

    [Fact]
    public async Task Alarm_auto_snoozes_after_five_minutes()
    {
        await _svc.StartAsync();
        var item = await _svc.AddAsync(ReminderKind.Alarm, "wake", T0.AddMinutes(1));
        await AdvanceAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(ScheduleState.Ringing, Get(item.Id).State);
        await AdvanceAsync(TimeSpan.FromMinutes(5));
        var cur = Get(item.Id);
        Assert.Equal(ScheduleState.Scheduled, cur.State);
        Assert.Equal(1, cur.SnoozeCount);
        Assert.Equal(_time.GetUtcNow() + _svc.Options.SnoozeLength, cur.DueAt);
        Assert.Single(_snoozed);
    }

    [Fact]
    public async Task Reminder_does_not_auto_snooze()
    {
        await _svc.StartAsync();
        var item = await _svc.AddAsync(ReminderKind.Reminder, "tea", T0.AddMinutes(1));
        await AdvanceAsync(TimeSpan.FromMinutes(20));
        Assert.Equal(ScheduleState.Ringing, Get(item.Id).State);
        Assert.Empty(_snoozed);
    }

    [Fact]
    public async Task Dismiss_snooze_cancel()
    {
        await _svc.StartAsync();
        var a = await _svc.AddAsync(ReminderKind.Reminder, "a", T0.AddMinutes(1));
        var b = await _svc.AddAsync(ReminderKind.Reminder, "b", T0.AddMinutes(1));
        var c = await _svc.AddAsync(ReminderKind.Reminder, "c", T0.AddMinutes(30));
        await AdvanceAsync(TimeSpan.FromMinutes(1));

        await _svc.DismissAsync(a.Id);
        Assert.Equal(ScheduleState.Done, Get(a.Id).State);

        await _svc.SnoozeAsync(b.Id, TimeSpan.FromMinutes(3));
        var sb = Get(b.Id);
        Assert.Equal(ScheduleState.Scheduled, sb.State);
        Assert.Equal(_time.GetUtcNow().AddMinutes(3), sb.DueAt);
        Assert.Equal(1, sb.SnoozeCount);

        Assert.True(await _svc.CancelAsync(c.Id));
        Assert.False(await _svc.CancelAsync(c.Id));
        Assert.DoesNotContain(_svc.Snapshot, i => i.Id == c.Id);
        Assert.False(await _svc.CancelAsync(a.Id));
    }

    [Fact]
    public async Task ActiveAlerts_oldest_first_and_due_order()
    {
        await _svc.StartAsync();
        await _svc.AddAsync(ReminderKind.Reminder, "second", T0.AddSeconds(50));
        await _svc.AddAsync(ReminderKind.Reminder, "first", T0.AddSeconds(40));
        await _svc.AddAsync(ReminderKind.Reminder, "third", T0.AddSeconds(60));
        await _svc.AddAsync(ReminderKind.Reminder, "far", T0.AddHours(1));
        await AdvanceAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(["first", "second", "third"], _svc.ActiveAlerts.Select(i => i.Label).ToArray());
        Assert.Equal(["first", "second", "third"], _due.Select(i => i.Label).ToArray());
    }

    [Fact]
    public async Task Done_items_pruned_on_load()
    {
        var oldDone = new ScheduledItem(Guid.NewGuid(), ReminderKind.Reminder, "old", T0.AddDays(-8), ScheduleState.Done, T0.AddDays(-9));
        var newDone = new ScheduledItem(Guid.NewGuid(), ReminderKind.Reminder, "new", T0.AddDays(-1), ScheduleState.Done, T0.AddDays(-2));
        await _store.SaveAsync([oldDone, newDone]);
        await _svc.StartAsync();
        Assert.Equal(newDone.Id, _svc.Snapshot.Single().Id);
    }

    [Fact]
    public async Task RecheckNow_wakes_loop_after_clock_jump()
    {
        await _svc.StartAsync();
        var item = await _svc.AddAsync(ReminderKind.Reminder, "x", T0.AddSeconds(10));
        await SettleAsync();
        // Jump without firing timers: set wall clock, then only RecheckNow can notice.
        _time.SetUtcNow(T0.AddSeconds(10));
        var before = _svc.Ticks;
        _svc.RecheckNow();
        for (var k = 0; k < 5000 && _svc.Ticks == before; k++) await Task.Delay(1);
        Assert.Equal(ScheduleState.Ringing, Get(item.Id).State);
    }

    [Fact]
    public async Task Dispose_stops_loop()
    {
        await _svc.StartAsync();
        await _svc.DisposeAsync();
        await _svc.DisposeAsync();
    }
}

public sealed class JsonStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aisa-test-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static ScheduledItem Item(string label) => new(Guid.NewGuid(), ReminderKind.Alarm, label,
        new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(2)), ScheduleState.Ringing,
        new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 4, 3, 6, 7, TimeSpan.Zero), 2);

    [Fact]
    public async Task Missing_file_is_empty()
    {
        Assert.Empty(await new JsonScheduleStore(Path.Combine(_dir, "s.json")).LoadAsync());
    }

    [Fact]
    public async Task Round_trip_with_envelope_and_string_enums()
    {
        var path = Path.Combine(_dir, "nested", "s.json");
        var store = new JsonScheduleStore(path);
        var items = new[] { Item("a"), Item("b") };
        await store.SaveAsync(items);
        Assert.Equal(items, await store.LoadAsync());
        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("\"version\": 1", text);
        Assert.Contains("\"Ringing\"", text);
        Assert.Contains("\"Alarm\"", text);
    }

    [Fact]
    public async Task Atomic_write_leaves_no_temp_files()
    {
        var path = Path.Combine(_dir, "s.json");
        var store = new JsonScheduleStore(path);
        await store.SaveAsync([Item("a")]);
        await store.SaveAsync([Item("b")]);
        Assert.Equal([path], Directory.GetFiles(_dir));
    }

    [Theory]
    [InlineData("not json {")]
    [InlineData("{\"version\":1}")]
    public async Task Corrupt_file_is_renamed_and_empty(string content)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "s.json");
        await File.WriteAllTextAsync(path, content);
        Assert.Empty(await new JsonScheduleStore(path).LoadAsync());
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(_dir, "s.json.corrupt-*"));
    }
}
