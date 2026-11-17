using System.Windows.Threading;
using Aisa.App.Chat;
using Aisa.Audio;
using Aisa.Character;
using Aisa.Core.Island;
using Aisa.Scheduling;

namespace Aisa.App.Alerts;

/// <summary>
/// Spec 3.2 E: a due item forces Expanded(Alert) and pins the island, sets the character state, and rings on
/// the alert channel. Reminders re-chime once after 1 min and fold (still pinned) after 5 min.
/// UI-thread only; scheduler events are marshalled here through the dispatcher.
/// </summary>
public sealed class AlertCoordinator : ObservableObject
{
    private static readonly TimeSpan ReminderRepeat = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ReminderFold = TimeSpan.FromMinutes(5);

    private readonly SchedulerService _scheduler;
    private readonly IslandStateMachine _island;
    private readonly CharacterEngine _character;
    private readonly SoundService _sound;
    private readonly TimeProvider _time;
    private readonly Func<int> _snoozeMinutes;
    private readonly DispatcherTimer _repeat, _fold;

    private ScheduledItem? _current;
    private int _queued;

    public AlertCoordinator(SchedulerService scheduler, IslandStateMachine island, CharacterEngine character,
        SoundService sound, TimeProvider time, Func<int> snoozeMinutes, Dispatcher dispatcher)
    {
        _scheduler = scheduler;
        _island = island;
        _character = character;
        _sound = sound;
        _time = time;
        _snoozeMinutes = snoozeMinutes;

        _repeat = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = ReminderRepeat };
        _repeat.Tick += (_, _) => { _repeat.Stop(); if (_current is { Kind: ReminderKind.Reminder }) _sound.StartAlert(AlertTone.ReminderChime); };
        _fold = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = ReminderFold };
        _fold.Tick += (_, _) => { _fold.Stop(); _island.FoldAlert(); };

        scheduler.Due += _ => dispatcher.BeginInvoke(Refresh);
        scheduler.AutoSnoozed += _ => dispatcher.BeginInvoke(Refresh);
        scheduler.Changed += _ => dispatcher.BeginInvoke(Refresh);

        DismissCommand = new RelayCommand(() => _ = AnswerAsync(snooze: false));
        SnoozeCommand = new RelayCommand(() => _ = AnswerAsync(snooze: true));
    }

    public ScheduledItem? Current => _current;
    public bool HasAlert => _current is not null;
    public string Title { get; private set; } = "";
    public string Label { get; private set; } = "";
    public string MoreText => _queued > 0 ? $"+{_queued} more" : "";
    public bool HasMore => _queued > 0;
    public bool IsAlarm => _current?.Kind == ReminderKind.Alarm;
    public string SnoozeText => $"Snooze {_snoozeMinutes()} min";
    public RelayCommand DismissCommand { get; }
    public RelayCommand SnoozeCommand { get; }

    /// <summary>Re-reads the queue; raises, advances or clears the alert as needed.</summary>
    public void Refresh()
    {
        var queue = _scheduler.ActiveAlerts;
        var next = queue.Count > 0 ? queue[0] : null;
        _queued = Math.Max(0, queue.Count - 1);

        if (next is null)
        {
            if (_current is not null) Clear();
            RaiseAll();
            return;
        }

        if (_current?.Id != next.Id || _current.State != next.State) Show(next);
        RaiseAll();
    }

    private void Show(ScheduledItem item)
    {
        bool first = _current is null;
        _current = item;
        var now = _time.GetLocalNow();
        bool missed = item.State == ScheduleState.Missed;
        string kind = item.Kind == ReminderKind.Alarm ? "Alarm" : "Reminder";
        Title = missed
            ? $"{TimeText.Icon(item.Kind)} Missed {kind.ToLowerInvariant()} · was due {TimeText.Describe(item.DueAt, now)}"
            : $"{TimeText.Icon(item.Kind)} {kind} · {TimeText.Describe(item.DueAt, now)}";
        Label = item.Label;

        _island.RaiseAlert();
        _sound.StopAlert();
        _repeat.Stop();
        _fold.Stop();

        if (item.Kind == ReminderKind.Alarm && !missed)
        {
            _character.SetAlertState(CharacterState.Approval);
            _sound.StartAlert(AlertTone.AlarmLoop); // scheduler auto-snoozes after 5 min
        }
        else
        {
            _character.SetAlertState(CharacterState.Question);
            if (first) _character.PlayEmote(Emote.Surprised);
            _sound.StartAlert(AlertTone.ReminderChime);
            if (!missed) _repeat.Start();
            _fold.Start();
        }
    }

    private void Clear()
    {
        _current = null;
        _repeat.Stop();
        _fold.Stop();
        _sound.StopAlert();
        _character.SetAlertState(null);
        _character.PlayEmote(Emote.Happy);
        _island.AlertsCleared();
    }

    private async Task AnswerAsync(bool snooze)
    {
        if (_current is not { } item) return;
        _sound.StopAlert();
        if (snooze) await _scheduler.SnoozeAsync(item.Id, TimeSpan.FromMinutes(_snoozeMinutes()));
        else await _scheduler.DismissAsync(item.Id);
        Refresh();
    }

    private void RaiseAll()
    {
        Raise(nameof(Current));
        Raise(nameof(HasAlert));
        Raise(nameof(Title));
        Raise(nameof(Label));
        Raise(nameof(MoreText));
        Raise(nameof(HasMore));
        Raise(nameof(IsAlarm));
        Raise(nameof(SnoozeText));
    }
}
