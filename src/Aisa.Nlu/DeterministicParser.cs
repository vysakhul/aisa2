using System.Text.RegularExpressions;

namespace Aisa.Nlu;

public static class DeterministicParser
{
    private const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string Days = "monday|tuesday|wednesday|thursday|friday|saturday|sunday";

    private static readonly Regex TimeWord = new(
        @"\d|\b(?:am|pm|a\.m|p\.m|tomorrow|tmrw|tmr|tonight|today|minutes?|mins?|hours?|hrs?|noon|midnight|midday|morning|afternoon|evening|night|o'?clock|quarter|half|" + Days + @")\b", O);
    private static readonly Regex Greeting = new(
        @"^(?:hi|hello|hey|hiya|yo|thanks|thank you|thx|good (?:morning|afternoon|evening))(?: there| aisa| friend)?$", O);
    private static readonly Regex Cancel = new(@"^(?:please\s+)?(?:cancel|delete|remove|clear)\b(?<q>.*)$", O);
    private static readonly Regex SetStart = new(@"^(?:please\s+|can you\s+|could you\s+)?(?:remind|set|wake|add|create|make|new)\b", O);
    private static readonly Regex List = new(
        @"\b(?:upcoming|coming up|what'?s next|what is next|what'?s on)\b|^(?:please\s+)?(?:list|show|view|see|check)\b", O);
    private static readonly Regex Help = new(@"^(?:please\s+)?(?:help|what can you do|how do you work|what do you do)\b|\bhelp me\b$", O);
    private static readonly Regex Remind = new(@"\bremind(?:er|ers|s)?\b", O);
    private static readonly Regex Alarm = new(@"\balarms?\b|\bwake\s+me\b", O);

    private static readonly Regex[] Triggers =
    [
        new(@"\b(?:please|pls|can you|could you|would you|i need you to|i want you to|i'd like you to|i need to|i have to)\b", O),
        new(@"\b(?:set|add|create|make|new)\s+(?:me\s+)?(?:an?\s+|my\s+|the\s+)?(?=remind|alarm)", O),
        new(@"\bremind(?:er|ers|s)?\b(?:\s+me)?(?:\s+(?:to|about|of|that))?", O),
        new(@"\bwake\s+me(?:\s+up)?\b", O),
        new(@"\balarms?\b(?:\s+(?:for|to|at|called|named))?", O),
    ];
    private static readonly Regex Connector = new(@"^(?:(?:at|for|on|in|to|by|about|of|and|then|it|is|set|make)\b\s*)+|(?:\s*\b(?:at|for|on|in|to|by|about|of|and|then|please)\b)+$", O);

    public static ParseOutcome Parse(string text, DateTimeOffset now, ParsedIntent? pending = null)
    {
        text ??= "";
        var looks = TimeWord.IsMatch(text);
        var clean = Clean(text);
        if (clean.Length == 0) return new(new(IntentKind.Unknown), looks, false);
        if (Greeting.IsMatch(clean)) return new(new(IntentKind.Greeting), false, false);

        if (pending is { NeedsTime: true }
            && TimeExtractor.Extract(clean, now, out var left, out var w) && w is not null
            && Strip(Triggers.Aggregate(left, (a, r) => r.Replace(a, " "))).Length == 0)
            return new(pending with { When = w }, looks, false);

        var c = Cancel.Match(clean);
        if (c.Success)
        {
            var q = Regex.Replace(c.Groups["q"].Value, @"^\s*(?:my|the|that|this)\b", "", O).Trim();
            return new(new(IntentKind.Cancel, CancelQuery: q.Length == 0 ? null : q), looks, false);
        }

        var setting = SetStart.IsMatch(clean);
        if (!setting && List.IsMatch(clean)) return new(new(IntentKind.List), looks, false);
        if (!setting && Help.IsMatch(clean)) return new(new(IntentKind.Help), looks, false);

        var isRemind = Remind.IsMatch(clean);
        var isAlarm = !isRemind && Alarm.IsMatch(clean);

        var found = TimeExtractor.Extract(clean, now, out var rest, out var when);
        if (!found) rest = clean;
        var label = rest;
        foreach (var r in Triggers) label = r.Replace(label, " ");
        label = Strip(label);
        var labelText = label.Length == 0 ? null : char.ToUpperInvariant(label[0]) + label[1..];

        IntentKind kind;
        if (isRemind) kind = IntentKind.SetReminder;
        else if (isAlarm) kind = IntentKind.SetAlarm;
        else if (found) kind = labelText is null ? IntentKind.SetAlarm : IntentKind.SetReminder;
        else return new(new(IntentKind.Unknown), looks, false);

        if (kind == IntentKind.SetAlarm && labelText is null) labelText = "Alarm";
        return new(new(kind, when, labelText), looks, labelText is not null && TimeWord.IsMatch(labelText));
    }

    private static string Clean(string s)
    {
        s = s.Replace('’', '\'').Replace('‘', '\'');
        s = Regex.Replace(s, @"\b([ap])\.m\b\.?", "$1m", O);
        s = Regex.Replace(s, @"(?<=\d)[.](?=\d{2}\b)", ":");
        s = Regex.Replace(s, @"[^\p{L}\p{N}:'@ ]+", " ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    private static string Strip(string s)
    {
        s = Regex.Replace(s, @"\s+", " ").Trim();
        string prev;
        do { prev = s; s = Connector.Replace(s, "").Trim(); } while (s != prev);
        return s;
    }
}
