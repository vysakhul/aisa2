using Aisa.Nlu;

namespace Aisa.Nlu.Tests;

public class ParserTests
{
    // Tuesday 2026-11-17 10:15 +05:30
    static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 15, 0, TimeSpan.FromHours(5.5));
    static DateTimeOffset T(int d, int h, int m = 0) => new(2026, 11, d, h, m, 0, Now.Offset);

    static ParsedIntent P(string s, ParsedIntent? pending = null) => DeterministicParser.Parse(s, Now, pending).Intent;

    [Theory]
    [InlineData("alarm at 7", IntentKind.SetAlarm, 17, 19, 0)]
    [InlineData("set an alarm for 7am", IntentKind.SetAlarm, 18, 7, 0)]
    [InlineData("alarm 7 am", IntentKind.SetAlarm, 18, 7, 0)]
    [InlineData("alarm 7:30pm", IntentKind.SetAlarm, 17, 19, 30)]
    [InlineData("alarm 19:00", IntentKind.SetAlarm, 17, 19, 0)]
    [InlineData("alarm at 0730", IntentKind.SetAlarm, 18, 7, 30)]
    [InlineData("alarm at 11", IntentKind.SetAlarm, 17, 11, 0)]
    [InlineData("alarm at 9", IntentKind.SetAlarm, 17, 21, 0)]
    [InlineData("alarm at noon", IntentKind.SetAlarm, 17, 12, 0)]
    [InlineData("alarm at midnight", IntentKind.SetAlarm, 18, 0, 0)]
    [InlineData("alarm half past 7", IntentKind.SetAlarm, 17, 19, 30)]
    [InlineData("alarm quarter to 8", IntentKind.SetAlarm, 17, 19, 45)]
    [InlineData("alarm quarter past 11", IntentKind.SetAlarm, 17, 11, 15)]
    [InlineData("alarm in 10 min", IntentKind.SetAlarm, 17, 10, 25)]
    [InlineData("alarm in 10 mins", IntentKind.SetAlarm, 17, 10, 25)]
    [InlineData("alarm in 10 minutes", IntentKind.SetAlarm, 17, 10, 25)]
    [InlineData("alarm in 2 hours", IntentKind.SetAlarm, 17, 12, 15)]
    [InlineData("alarm in an hour", IntentKind.SetAlarm, 17, 11, 15)]
    [InlineData("alarm in half an hour", IntentKind.SetAlarm, 17, 10, 45)]
    [InlineData("alarm in 1h 30m", IntentKind.SetAlarm, 17, 11, 45)]
    [InlineData("alarm tomorrow at 7", IntentKind.SetAlarm, 18, 7, 0)]
    [InlineData("alarm tomorrow 7", IntentKind.SetAlarm, 18, 7, 0)]
    [InlineData("alarm at 7 tomorrow", IntentKind.SetAlarm, 18, 7, 0)]
    [InlineData("alarm tonight", IntentKind.SetAlarm, 17, 20, 0)]
    [InlineData("alarm tonight at 9", IntentKind.SetAlarm, 17, 21, 0)]
    [InlineData("alarm this evening", IntentKind.SetAlarm, 17, 18, 0)]
    [InlineData("alarm this afternoon", IntentKind.SetAlarm, 17, 15, 0)]
    [InlineData("alarm tomorrow morning", IntentKind.SetAlarm, 18, 9, 0)]
    [InlineData("alarm monday", IntentKind.SetAlarm, 23, 9, 0)]
    [InlineData("alarm on friday at 3pm", IntentKind.SetAlarm, 20, 15, 0)]
    [InlineData("alarm next friday", IntentKind.SetAlarm, 20, 9, 0)]
    [InlineData("alarm tuesday 8pm", IntentKind.SetAlarm, 17, 20, 0)]
    [InlineData("alarm tuesday 9am", IntentKind.SetAlarm, 24, 9, 0)]
    [InlineData("wake me up at 6", IntentKind.SetAlarm, 17, 18, 0)]
    [InlineData("⏰ wake me at 6:15 am!", IntentKind.SetAlarm, 18, 6, 15)]
    [InlineData("7:30 a.m. alarm", IntentKind.SetAlarm, 18, 7, 30)]
    [InlineData("alarm 7 o'clock", IntentKind.SetAlarm, 17, 19, 0)]
    public void Alarms(string text, IntentKind kind, int day, int h, int m)
    {
        var i = P(text);
        Assert.Equal(kind, i.Kind);
        Assert.Equal(T(day, h, m), i.When);
    }

    [Fact] public void RelativeKeepsSeconds() => Assert.Equal(Now.AddMinutes(10), P("alarm in 10 min").When);

    [Theory]
    [InlineData("remind me to call mom at 5pm", "Call mom", 17, 17)]
    [InlineData("Remind me about the dentist tomorrow at 9am", "The dentist", 18, 9)]
    [InlineData("remind me to take pills in 2 hours", "Take pills", 17, 12)]
    [InlineData("set a reminder to water plants at 8pm", "Water plants", 17, 20)]
    [InlineData("call mom at 5pm", "Call mom", 17, 17)]
    [InlineData("buy milk tomorrow at 10am", "Buy milk", 18, 10)]
    [InlineData("please remind me to stretch tonight", "Stretch", 17, 20)]
    public void Reminders(string text, string label, int day, int h)
    {
        var i = P(text);
        Assert.Equal(IntentKind.SetReminder, i.Kind);
        Assert.Equal(label, i.Label);
        Assert.Equal(day, i.When!.Value.Day);
        Assert.Equal(h, i.When.Value.Hour);
    }

    [Fact]
    public void ReminderWithoutLabelHasNullLabel()
    {
        var i = P("remind me at 5pm");
        Assert.Equal(IntentKind.SetReminder, i.Kind);
        Assert.Null(i.Label);
    }

    [Fact] public void AlarmWithoutLabelDefaults() => Assert.Equal("Alarm", P("alarm at 7").Label);

    [Fact] public void AlarmKeepsExtraLabel() => Assert.Equal("Gym", P("alarm for gym at 6am").Label);

    [Theory]
    [InlineData("⏰ Set an alarm", IntentKind.SetAlarm)]
    [InlineData("🔔 Remind me", IntentKind.SetReminder)]
    [InlineData("📋 Upcoming", IntentKind.List)]
    [InlineData("upcoming", IntentKind.List)]
    [InlineData("what's coming up?", IntentKind.List)]
    [InlineData("What’s next", IntentKind.List)]
    [InlineData("list", IntentKind.List)]
    [InlineData("show my alarms", IntentKind.List)]
    [InlineData("show reminders", IntentKind.List)]
    [InlineData("help", IntentKind.Help)]
    [InlineData("What can you do?", IntentKind.Help)]
    [InlineData("hi", IntentKind.Greeting)]
    [InlineData("Hello!", IntentKind.Greeting)]
    [InlineData("hey", IntentKind.Greeting)]
    [InlineData("thanks", IntentKind.Greeting)]
    [InlineData("good morning", IntentKind.Greeting)]
    [InlineData("tell me a joke", IntentKind.Unknown)]
    [InlineData("", IntentKind.Unknown)]
    [InlineData("🙂", IntentKind.Unknown)]
    public void NoTimeIntents(string text, IntentKind kind)
    {
        var i = P(text);
        Assert.Equal(kind, i.Kind);
        Assert.Null(i.When);
    }

    [Fact]
    public void ChipsNeedTime()
    {
        Assert.True(P("⏰ Set an alarm").NeedsTime);
        Assert.True(P("🔔 Remind me").NeedsTime);
        Assert.Equal("Alarm", P("⏰ Set an alarm").Label);
    }

    [Theory]
    [InlineData("cancel the 7am alarm", "7am alarm")]
    [InlineData("Delete my call mom reminder", "call mom reminder")]
    [InlineData("remove call mom", "call mom")]
    [InlineData("clear all", "all")]
    [InlineData("cancel", null)]
    public void Cancel(string text, string? q)
    {
        var i = P(text);
        Assert.Equal(IntentKind.Cancel, i.Kind);
        Assert.Equal(q, i.CancelQuery);
    }

    [Theory]
    [InlineData("at 7pm", 17, 19)]
    [InlineData("tomorrow 9", 18, 9)]
    [InlineData("7", 17, 19)]
    [InlineData("in 20 minutes", 17, 10)]
    [InlineData("for 8am please", 18, 8)]
    public void PendingFollowUp(string text, int day, int hour)
    {
        var pending = new ParsedIntent(IntentKind.SetReminder, Label: "Call mom");
        var i = P(text, pending);
        Assert.Equal(IntentKind.SetReminder, i.Kind);
        Assert.Equal("Call mom", i.Label);
        Assert.Equal(day, i.When!.Value.Day);
        Assert.Equal(hour, i.When.Value.Hour);
    }

    [Fact]
    public void PendingIgnoredWhenNotATime()
    {
        var pending = new ParsedIntent(IntentKind.SetAlarm, Label: "Alarm");
        Assert.Equal(IntentKind.Cancel, P("cancel that", pending).Kind);
    }

    [Fact]
    public void PendingNotNeedingTimeIsIgnored()
    {
        var done = new ParsedIntent(IntentKind.SetAlarm, T(18, 7), "Alarm");
        var i = P("alarm at 8pm", done);
        Assert.Equal(IntentKind.SetAlarm, i.Kind);
        Assert.Equal(20, i.When!.Value.Hour);
    }

    [Theory]
    [InlineData("alarm at 7", true)]
    [InlineData("remind me tomorrow", true)]
    [InlineData("remind me in 5 minutes", true)]
    [InlineData("remind me on friday", true)]
    [InlineData("hello there", false)]
    [InlineData("tell me a joke", false)]
    [InlineData("list", false)]
    public void LooksTimed(string text, bool expected) =>
        Assert.Equal(expected, DeterministicParser.Parse(text, Now).LooksTimed);

    [Fact]
    public void LooksTimedEvenWhenParseFails()
    {
        var o = DeterministicParser.Parse("remind me to call mom at 25:99", Now);
        Assert.True(o.LooksTimed);
        Assert.True(o.Intent.NeedsTime);
    }

    [Fact]
    public void TimeWordsInLabel()
    {
        Assert.True(DeterministicParser.Parse("remind me to buy 2 apples at 5pm", Now).TimeWordsInLabel);
        Assert.False(DeterministicParser.Parse("remind me to call mom at 5pm", Now).TimeWordsInLabel);
    }

    [Fact]
    public void ResultsAreInFutureAndUseNowOffset()
    {
        foreach (var s in new[] { "alarm at 10", "alarm at 10:15", "alarm 10:14am", "alarm at 3", "alarm at 9am" })
        {
            var w = P(s).When;
            Assert.NotNull(w);
            Assert.True(w > Now, s);
            Assert.Equal(Now.Offset, w.Value.Offset);
        }
    }

    [Fact]
    public void PastTodayWithExplicitDayWordGivesNoTime() => Assert.Null(P("alarm today at 9am").When);

    [Fact]
    public void PastClockRollsToTomorrow() => Assert.Equal(T(18, 9), P("alarm at 9am").When);

    [Fact]
    public void BareNumberInTaskIsNotATime()
    {
        var o = DeterministicParser.Parse("remind me to buy 2 apples", Now);
        Assert.True(o.Intent.NeedsTime);
        Assert.Equal("Buy 2 apples", o.Intent.Label);
    }
}
