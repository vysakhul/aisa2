using System.Text.RegularExpressions;

namespace Aisa.Nlu;

/// <summary>Finds and removes a time phrase from already-cleaned text.</summary>
internal static class TimeExtractor
{
    private const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string Days = "monday|tuesday|wednesday|thursday|friday|saturday|sunday";
    private const string Unit = @"(?!\s*(?:minutes?|mins?|hours?|hrs?|days?|times?|people|things|items)\b)";

    private static readonly Regex Relative = new(
        @"\bin\s+(?:half\s+an?\s+hour|(?:(?:an?|\d+(?:\.\d+)?)\s*(?:hours?|hrs?|h|minutes?|mins?|m)\b\s*(?:and\s+)?)+)", O);
    private static readonly Regex RelPart = new(@"(an?|\d+(?:\.\d+)?)\s*(hours?|hrs?|h|minutes?|mins?|m)\b", O);
    private static readonly Regex HalfPast = new(@"\bhalf\s+past\s+(?<h>\d{1,2})(?:\s*(?<ap>am|pm))?\b", O);
    private static readonly Regex Quarter = new(@"\bquarter\s+(?<d>to|past)\s+(?<h>\d{1,2})(?:\s*(?<ap>am|pm))?\b", O);
    private static readonly Regex Noon = new(@"(?:\b(?:at|by)\s+)?\b(?<w>noon|midday|midnight)\b", O);
    private static readonly Regex AmPm = new(@"(?:\b(?:at|by|for)\s+|@\s*)?\b(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ap>am|pm)\b", O);
    private static readonly Regex Colon = new(@"(?:\b(?:at|by|for)\s+|@\s*)?\b(?<h>\d{1,2}):(?<m>\d{2})\b", O);
    private static readonly Regex OClock = new(@"(?:\b(?:at|by|for)\s+)?\b(?<h>\d{1,2})\s*o'?clock\b", O);
    private static readonly Regex Bare = new(
        @"(?:\b(?:at|for|by)\s+|@\s*)(?<n>\d{1,4})\b" + Unit +
        @"|(?<=\b(?:alarm|alarms|tomorrow|today|tonight|morning|afternoon|evening|night|" + Days + @")\s)(?<n>\d{1,4})\b" + Unit +
        @"|^\s*(?<n>\d{1,4})\s*$", O);
    private static readonly Regex Tomorrow = new(@"\b(?:tomorrow|tmrw|tmr)\b", O);
    private static readonly Regex Today = new(@"\btoday\b", O);
    private static readonly Regex Tonight = new(@"\btonight\b", O);
    private static readonly Regex Weekday = new(@"\b(?:(?<k>next|this|on)\s+)?(?<d>" + Days + @")\b", O);
    private static readonly Regex Part = new(@"\b(?:(?:this|in\s+the|at|every)\s+)?(?<p>morning|afternoon|evening|night)\b", O);

    private sealed record Clock(int Hour, int Minute, bool Ambiguous);

    public static bool Extract(string text, DateTimeOffset now, out string rest, out DateTimeOffset? when)
    {
        rest = text; when = null;
        var s = text;

        var rel = Relative.Match(s);
        if (rel.Success)
        {
            var span = TimeSpan.Zero;
            if (rel.Value.Contains("half", StringComparison.OrdinalIgnoreCase)) span = TimeSpan.FromMinutes(30);
            else
                foreach (Match p in RelPart.Matches(rel.Value))
                {
                    var n = p.Groups[1].Value.StartsWith('a') || p.Groups[1].Value.StartsWith('A')
                        ? 1 : double.Parse(p.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    span += char.ToLowerInvariant(p.Groups[2].Value[0]) == 'h' ? TimeSpan.FromHours(n) : TimeSpan.FromMinutes(n);
                }
            rest = Tidy(Remove(s, rel));
            if (span > TimeSpan.Zero) when = now + span;
            return true;
        }

        Clock? clock = null;
        Match? m;
        if ((m = Take(ref s, HalfPast)) != null) clock = Make(int.Parse(m.Groups["h"].Value), 30, m.Groups["ap"].Value);
        else if ((m = Take(ref s, Quarter)) != null)
        {
            var h = int.Parse(m.Groups["h"].Value);
            clock = m.Groups["d"].Value.Equals("to", StringComparison.OrdinalIgnoreCase)
                ? Make(h == 1 ? 12 : h - 1, 45, m.Groups["ap"].Value)
                : Make(h, 15, m.Groups["ap"].Value);
        }
        else if ((m = Take(ref s, Noon)) != null)
            clock = new Clock(m.Groups["w"].Value.Equals("midnight", StringComparison.OrdinalIgnoreCase) ? 0 : 12, 0, false);
        else if ((m = Take(ref s, AmPm)) != null)
            clock = Make(int.Parse(m.Groups["h"].Value), m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value) : 0, m.Groups["ap"].Value);
        else if ((m = Take(ref s, Colon)) != null)
            clock = Make(int.Parse(m.Groups["h"].Value), int.Parse(m.Groups["m"].Value), "");
        else if ((m = Take(ref s, OClock)) != null)
            clock = Make(int.Parse(m.Groups["h"].Value), 0, "");
        else if ((m = Take(ref s, Bare)) != null)
        {
            var n = m.Groups["n"].Value;
            if (n.Length <= 2) clock = Make(int.Parse(n), 0, "");
            else
            {
                var v = int.Parse(n);
                clock = Make(v / 100, v % 100, "");
                if (n.Length == 3 || n.Length == 4) clock = clock with { Ambiguous = clock.Hour is >= 1 and <= 12 && n.Length == 3 };
            }
        }
        if (clock is { Hour: > 23 } or { Minute: > 59 }) clock = null;
        if (clock == null) s = text;

        int? dayOffset = null; DayOfWeek? weekday = null; bool next = false, today = false;
        string? part = null;
        if (Take(ref s, Tomorrow) != null) dayOffset = 1;
        else if (Take(ref s, Tonight) != null) { part = "night"; today = true; }
        else if (Take(ref s, Today) != null) today = true;
        if ((m = Take(ref s, Weekday)) != null)
        {
            weekday = Enum.Parse<DayOfWeek>(m.Groups["d"].Value, true);
            next = m.Groups["k"].Value.Equals("next", StringComparison.OrdinalIgnoreCase);
        }
        if ((m = Take(ref s, Part)) != null) part ??= m.Groups["p"].Value.ToLowerInvariant();

        if (clock == null && dayOffset == null && weekday == null && !today && part == null) return false;
        rest = Tidy(s);

        var candidates = new List<TimeSpan>();
        if (clock != null)
        {
            var hs = clock.Ambiguous ? new[] { clock.Hour % 12, clock.Hour % 12 + 12 } : new[] { clock.Hour };
            if (hs.Length > 1 && part != null)
                hs = part == "morning" ? new[] { hs[0] } : new[] { hs[1] };
            candidates.AddRange(hs.Select(h => new TimeSpan(h, clock.Minute, 0)));
        }
        else
        {
            candidates.Add(TimeSpan.FromHours(part switch { "afternoon" => 15, "evening" => 18, "night" => 20, _ => 9 }));
        }
        candidates.Sort();

        DateTimeOffset At(DateOnly d, TimeSpan t) => new(d.ToDateTime(TimeOnly.FromTimeSpan(t)), now.Offset);
        DateTimeOffset? FirstFuture(DateOnly d) => candidates.Select(t => At(d, t)).Cast<DateTimeOffset?>().FirstOrDefault(c => c > now);

        var todayDate = DateOnly.FromDateTime(now.DateTime);
        if (dayOffset != null) when = FirstFuture(todayDate.AddDays(dayOffset.Value));
        else if (weekday != null)
        {
            var d = ((int)weekday.Value - (int)now.DayOfWeek + 7) % 7;
            if (d == 0) when = next ? null : FirstFuture(todayDate);
            if (when == null) when = FirstFuture(todayDate.AddDays(d == 0 ? 7 : d));
        }
        else
        {
            when = FirstFuture(todayDate);
            if (when == null && !(today && part != "night" && clock != null)) when = FirstFuture(todayDate.AddDays(1));
        }
        return true;
    }

    private static Clock Make(int h, int min, string ap)
    {
        if (ap.Length == 0) return new Clock(h, min, h is >= 1 and <= 12);
        if (h is < 1 or > 12) return new Clock(99, 0, false);
        var pm = ap.Equals("pm", StringComparison.OrdinalIgnoreCase);
        return new Clock(h % 12 + (pm ? 12 : 0), min, false);
    }

    private static Match? Take(ref string s, Regex r)
    {
        var m = r.Match(s);
        if (!m.Success) return null;
        s = Remove(s, m);
        return m;
    }

    private static string Remove(string s, Match m) => s.Remove(m.Index, m.Length).Insert(m.Index, " ");
    private static string Tidy(string s) => Regex.Replace(s, @"\s+", " ").Trim();
}
