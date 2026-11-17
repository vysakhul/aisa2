namespace Aisa.Scheduling;

public enum ReminderKind { Alarm, Reminder }
public enum ScheduleState { Scheduled, Ringing, Done, Missed }

public sealed record ScheduledItem(
    Guid Id, ReminderKind Kind, string Label, DateTimeOffset DueAt, ScheduleState State,
    DateTimeOffset CreatedAt, DateTimeOffset? RingingSince = null, int SnoozeCount = 0);

public sealed class SchedulerOptions
{
    public TimeSpan Grace { get; set; } = TimeSpan.FromSeconds(120);
    public TimeSpan AlarmAutoSnoozeAfter { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan SnoozeLength { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan MaxRecheckInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan KeepDoneFor { get; set; } = TimeSpan.FromDays(7);
}

public sealed class ScheduleException(string message) : Exception(message);
