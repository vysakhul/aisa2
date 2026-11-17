namespace Aisa.Nlu;

public enum IntentKind { SetAlarm, SetReminder, List, Cancel, Help, Greeting, Unknown }

public sealed record ParsedIntent(IntentKind Kind, DateTimeOffset? When = null, string? Label = null, string? CancelQuery = null)
{
    public bool NeedsTime => Kind is IntentKind.SetAlarm or IntentKind.SetReminder && When is null;
}

public sealed record ParseOutcome(ParsedIntent Intent, bool LooksTimed, bool TimeWordsInLabel);

public sealed record NluResult(ParsedIntent Intent, bool UsedLlm);
