using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Aisa.Scheduling;

namespace Aisa.App.Chat;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public enum ChatRole { User, Assistant }

public sealed class ChatMessage(ChatRole role, string text, object? card = null) : ObservableObject
{
    public ChatRole Role { get; } = role;
    public bool IsUser => Role == ChatRole.User;
    public string Text { get; } = text;
    public bool HasText => !string.IsNullOrEmpty(Text);
    public object? Card { get; } = card;
}

public enum ConfirmationState { Pending, Saved, Failed }

public sealed class ConfirmationCard : ObservableObject
{
    private ConfirmationState _state;
    private string? _note;

    public ConfirmationCard(ReminderKind kind, DateTimeOffset when, string label, string originalText, DateTimeOffset now)
    {
        Kind = kind;
        When = when;
        Label = label;
        OriginalText = originalText;
        Title = $"{TimeText.Icon(kind)} {kind} · {TimeText.Describe(when, now)}";
    }

    public ReminderKind Kind { get; }
    public DateTimeOffset When { get; }
    public string Label { get; }
    public string OriginalText { get; }
    public string Title { get; }

    public ConfirmationState State
    {
        get => _state;
        set { if (Set(ref _state, value)) { Raise(nameof(IsPending)); Raise(nameof(IsSaved)); } }
    }

    public bool IsPending => State == ConfirmationState.Pending;
    public bool IsSaved => State == ConfirmationState.Saved;

    public string? Note
    {
        get => _note;
        set { if (Set(ref _note, value)) Raise(nameof(HasNote)); }
    }

    public bool HasNote => !string.IsNullOrEmpty(Note);
    public ICommand SetCommand { get; set; } = new RelayCommand(() => { });
    public ICommand EditCommand { get; set; } = new RelayCommand(() => { });
}

public sealed class UpcomingRow(Guid id, string icon, string time, string label, string? tag)
{
    public Guid Id { get; } = id;
    public string Icon { get; } = icon;
    public string Time { get; } = time;
    public string Label { get; } = label;
    public string? Tag { get; } = tag;
    public bool HasTag => Tag is not null;
    public ICommand RemoveCommand { get; set; } = new RelayCommand(() => { });
}

public sealed class UpcomingCard : ObservableObject
{
    public ObservableCollection<UpcomingRow> Rows { get; } = [];
    public bool IsEmpty => Rows.Count == 0;
    public void NotifyRowsChanged() => Raise(nameof(IsEmpty));
}

public sealed class CancelCard : ObservableObject
{
    private bool _done;
    private string? _note;

    public CancelCard(IReadOnlyList<UpcomingRow> matches) => Matches = matches;

    public IReadOnlyList<UpcomingRow> Matches { get; }
    public string Question => Matches.Count == 1 ? "Cancel this?" : $"Cancel these {Matches.Count}?";

    public bool Done
    {
        get => _done;
        set { if (Set(ref _done, value)) Raise(nameof(IsPending)); }
    }

    public bool IsPending => !Done;

    public string? Note
    {
        get => _note;
        set => Set(ref _note, value);
    }

    public ICommand ConfirmCommand { get; set; } = new RelayCommand(() => { });
}

internal static class TimeText
{
    public static string Icon(ReminderKind kind) => kind == ReminderKind.Alarm ? "⏰" : "🔔";

    /// <summary>"Today 07:00", "Tomorrow 07:00", "Yesterday 18:00", "Fri 07:00", "21 Nov 07:00" in the user's clock format.</summary>
    public static string Describe(DateTimeOffset when, DateTimeOffset now)
    {
        var culture = CultureInfo.CurrentCulture;
        var local = when.ToLocalTime();
        var today = now.ToLocalTime().Date;
        string time = local.ToString("t", culture);
        int days = (local.Date - today).Days;
        return days switch
        {
            0 => $"Today {time}",
            1 => $"Tomorrow {time}",
            -1 => $"Yesterday {time}",
            > 1 and < 7 => $"{local.ToString("ddd", culture)} {time}",
            _ => $"{local.ToString("d MMM", culture)} {time}",
        };
    }
}
