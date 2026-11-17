using System.Collections.ObjectModel;
using Aisa.Audio;
using Aisa.Character;
using Aisa.Nlu;
using Aisa.Scheduling;

namespace Aisa.App.Chat;

/// <summary>
/// One chat turn (Spec 3.2 C): Thinking for at least 350 ms → deterministic parser → LM Studio fallback →
/// character outcome. UI-thread only: it mutates the observable message list.
/// </summary>
public sealed class ChatController : ObservableObject
{
    private static readonly TimeSpan MinThinking = TimeSpan.FromMilliseconds(350);
    private const string HelpText =
        "I can set alarms and reminders. Try \"wake me at 7\", \"remind me to call mom in 20 minutes\", \"what's coming up?\" or \"cancel the 7am alarm\".";

    private readonly NluPipeline _nlu;
    private readonly SchedulerService _scheduler;
    private readonly CharacterEngine _character;
    private readonly SoundService _sound;
    private readonly TimeProvider _time;

    private ParsedIntent? _pending;
    private bool _isThinking;
    private CancellationTokenSource? _turn;

    public ChatController(NluPipeline nlu, SchedulerService scheduler, CharacterEngine character, SoundService sound, TimeProvider time)
    {
        _nlu = nlu;
        _scheduler = scheduler;
        _character = character;
        _sound = sound;
        _time = time;
    }

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    public bool IsThinking
    {
        get => _isThinking;
        private set => Set(ref _isThinking, value);
    }

    /// <summary>Asks the view to put text into the input (chips, Edit).</summary>
    public event Action<string>? PrefillRequested;

    public async Task SendAsync(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || IsThinking) return;

        Messages.Add(new ChatMessage(ChatRole.User, text));
        _sound.Play(SoundCue.Send);
        _character.SetDialogState(null);
        _character.BeginOverride(CharacterState.Thinking);
        IsThinking = true;

        _turn?.Cancel();
        _turn = new CancellationTokenSource();
        NluResult result;
        try
        {
            var now = _time.GetLocalNow();
            var parse = _nlu.ParseAsync(text, now, _pending, _turn.Token);
            await Task.WhenAll(parse, Task.Delay(MinThinking, _time));
            result = await parse;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            IsThinking = false;
            _character.EndOverride();
        }

        Handle(text, result);
    }

    public void Chip(string chip)
    {
        switch (chip)
        {
            case "alarm": PrefillRequested?.Invoke("Wake me at "); break;
            case "remind": PrefillRequested?.Invoke("Remind me to "); break;
            case "upcoming": _ = SendAsync("What's coming up?"); break;
        }
    }

    public void Reset()
    {
        _pending = null;
        _character.SetDialogState(null);
    }

    private void Handle(string text, NluResult result)
    {
        var intent = result.Intent;
        var now = _time.GetLocalNow();

        switch (intent.Kind)
        {
            case IntentKind.SetAlarm or IntentKind.SetReminder when intent.When is { } when:
            {
                _pending = null;
                var kind = intent.Kind == IntentKind.SetAlarm ? ReminderKind.Alarm : ReminderKind.Reminder;
                string label = string.IsNullOrWhiteSpace(intent.Label) ? (kind == ReminderKind.Alarm ? "Alarm" : "Reminder") : intent.Label!;
                var card = new ConfirmationCard(kind, when, label, text, now);
                card.SetCommand = new RelayCommand(() => _ = ConfirmAsync(card));
                card.EditCommand = new RelayCommand(() => PrefillRequested?.Invoke(card.OriginalText));
                Messages.Add(new ChatMessage(ChatRole.Assistant, "", card));
                _character.PlayEmote(Emote.Happy);
                _sound.Play(SoundCue.Finish);
                break;
            }

            case IntentKind.SetAlarm or IntentKind.SetReminder:
                _pending = intent;
                Messages.Add(new ChatMessage(ChatRole.Assistant, intent.Kind == IntentKind.SetAlarm ? "When should I ring?" : "When should I remind you?"));
                _character.SetDialogState(CharacterState.Question); // held until the user answers
                _sound.Play(SoundCue.Finish);
                break;

            case IntentKind.List:
                _pending = null;
                Messages.Add(new ChatMessage(ChatRole.Assistant, "", BuildUpcoming(now)));
                _character.PlayEmote(Emote.Happy);
                _sound.Play(SoundCue.Finish);
                break;

            case IntentKind.Cancel:
                _pending = null;
                HandleCancel(intent.CancelQuery, now);
                break;

            case IntentKind.Help:
            case IntentKind.Greeting:
                _pending = null;
                Messages.Add(new ChatMessage(ChatRole.Assistant, intent.Kind == IntentKind.Greeting ? "Hi! " + HelpText : HelpText));
                _character.PlayEmote(Emote.Wink);
                _sound.Play(SoundCue.Blip);
                break;

            default:
                Messages.Add(new ChatMessage(ChatRole.Assistant, "Sorry, I didn't catch that. Try \"remind me to stretch in 30 minutes\"."));
                _character.Flash(CharacterState.Error, TimeSpan.FromSeconds(1.2));
                _sound.Play(SoundCue.Error);
                break;
        }
    }

    private async Task ConfirmAsync(ConfirmationCard card)
    {
        if (!card.IsPending) return;
        try
        {
            await _scheduler.AddAsync(card.Kind, card.Label, card.When);
            card.State = ConfirmationState.Saved;
            card.Note = "Set ✓";
            _character.Flash(CharacterState.Finished, TimeSpan.FromSeconds(1.6));
            _character.PlayEmote(Emote.Proud);
            _sound.Play(SoundCue.Proud);
        }
        catch (ScheduleException ex)
        {
            card.State = ConfirmationState.Failed;
            card.Note = ex.Message;
            _character.Flash(CharacterState.Error, TimeSpan.FromSeconds(1.2));
            _sound.Play(SoundCue.Error);
        }
    }

    private UpcomingCard BuildUpcoming(DateTimeOffset now)
    {
        var card = new UpcomingCard();
        foreach (var item in _scheduler.Snapshot.Where(i => i.State != ScheduleState.Done).OrderBy(i => i.DueAt))
        {
            var row = ToRow(item, now);
            row.RemoveCommand = new RelayCommand(() => _ = RemoveAsync(card, row));
            card.Rows.Add(row);
        }
        card.NotifyRowsChanged();
        return card;
    }

    private async Task RemoveAsync(UpcomingCard card, UpcomingRow row)
    {
        if (await _scheduler.CancelAsync(row.Id))
        {
            card.Rows.Remove(row);
            card.NotifyRowsChanged();
            _sound.Play(SoundCue.Pop);
        }
    }

    private void HandleCancel(string? query, DateTimeOffset now)
    {
        var live = _scheduler.Snapshot.Where(i => i.State != ScheduleState.Done).OrderBy(i => i.DueAt).ToList();
        var matches = Match(live, query, now);

        if (matches.Count == 0)
        {
            Messages.Add(new ChatMessage(ChatRole.Assistant, live.Count == 0 ? "There's nothing scheduled." : "I couldn't find that one. Here's what's coming up:",
                live.Count == 0 ? null : BuildUpcoming(now)));
            _character.PlayEmote(Emote.Happy);
            _sound.Play(SoundCue.Finish);
            return;
        }

        var rows = matches.Select(i => ToRow(i, now)).ToList();
        var card = new CancelCard(rows);
        card.ConfirmCommand = new RelayCommand(() => _ = ConfirmCancelAsync(card));
        Messages.Add(new ChatMessage(ChatRole.Assistant, "", card));
        _character.PlayEmote(Emote.Happy);
        _sound.Play(SoundCue.Finish);
    }

    private async Task ConfirmCancelAsync(CancelCard card)
    {
        if (card.Done) return;
        int removed = 0;
        foreach (var row in card.Matches)
            if (await _scheduler.CancelAsync(row.Id)) removed++;
        card.Done = true;
        card.Note = removed == 1 ? "Cancelled." : $"Cancelled {removed}.";
        _sound.Play(SoundCue.Pop);
    }

    private static List<ScheduledItem> Match(List<ScheduledItem> items, string? query, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(query)) return items.Count == 1 ? items : [];
        string q = query.Trim().ToLowerInvariant();
        if (q is "all" or "everything" or "all of them") return items;

        bool wantsAlarm = q.Contains("alarm"), wantsReminder = q.Contains("reminder");
        string words = q.Replace("alarms", "").Replace("alarm", "").Replace("reminders", "").Replace("reminder", "")
            .Replace("the ", "").Replace("my ", "").Trim();

        // Let the parser read a time out of the query ("7am alarm") so it can match by due time.
        var parsedTime = DeterministicParser.Parse("remind me " + words, now).Intent.When;

        return items.Where(i =>
        {
            if (wantsAlarm && i.Kind != ReminderKind.Alarm) return false;
            if (wantsReminder && i.Kind != ReminderKind.Reminder) return false;
            if (words.Length == 0) return true;
            if (i.Label.Contains(words, StringComparison.OrdinalIgnoreCase)) return true;
            return parsedTime is { } t && i.DueAt.ToLocalTime().TimeOfDay == t.ToLocalTime().TimeOfDay;
        }).ToList();
    }

    private static UpcomingRow ToRow(ScheduledItem item, DateTimeOffset now)
    {
        string? tag = item.State switch
        {
            ScheduleState.Missed => "missed",
            ScheduleState.Ringing => "ringing",
            _ when item.SnoozeCount > 0 => "snoozed",
            _ => null,
        };
        return new UpcomingRow(item.Id, TimeText.Icon(item.Kind), TimeText.Describe(item.DueAt, now), item.Label, tag);
    }
}
