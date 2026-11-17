namespace Aisa.Core.Island;

public enum IslandMode { Closed = 0, Compact = 1, Expanded = 2 }

public enum IslandView { Greeting, Chat, Alert }

public sealed record IslandOptions
{
    public TimeSpan AutoClose { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan CompactHide { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan HoverOpenGrace { get; init; } = TimeSpan.FromSeconds(0.6);
    public TimeSpan FaceHoverOpen { get; init; } = TimeSpan.FromSeconds(0.5);
    public TimeSpan GreetingCollapse { get; init; } = TimeSpan.FromSeconds(0.6);
    public TimeSpan GreetingCollapseHovered { get; init; } = TimeSpan.FromSeconds(10);
    public bool OpenOnFaceHover { get; init; } = true;
}

/// <param name="CollapseDueAt">When the running Expanded→Compact countdown fires (drives the countdown bar).</param>
/// <param name="CollapseTotal">Full length of that countdown.</param>
public sealed record IslandSnapshot(
    IslandMode Mode,
    IslandView View,
    bool Pinned,
    bool HoverOpened,
    bool PointerInside,
    DateTimeOffset? CollapseDueAt,
    TimeSpan CollapseTotal);

/// <summary>
/// Headless island machine (Spec 2.1, 3.2, 3.3). Owns geometry-level mode, the visible view, the pinned flag and
/// the idle timers. Single-threaded: call it from one thread. Timer callbacks are posted to the
/// SynchronizationContext captured at construction (the WPF dispatcher in the app, inline in tests).
/// </summary>
public sealed class IslandStateMachine : IDisposable
{
    private readonly TimeProvider _time;
    private readonly SynchronizationContext? _context;

    private readonly IslandTimer _faceOpen, _compactHide, _expandedCollapse, _greetingCollapse;

    private IslandOptions _options;
    private IslandMode _mode = IslandMode.Closed;
    private IslandView _view = IslandView.Chat;
    private bool _pinned, _hoverOpened, _pointerInside, _greetingActive, _alertHeld, _hasAlerts;
    private TimeSpan _collapseTotal;

    public IslandStateMachine(TimeProvider? time = null, IslandOptions? options = null)
    {
        _time = time ?? TimeProvider.System;
        _options = options ?? new IslandOptions();
        _context = SynchronizationContext.Current;

        _faceOpen = new IslandTimer(this, OnFaceOpenElapsed);
        _compactHide = new IslandTimer(this, OnCompactHideElapsed);
        _expandedCollapse = new IslandTimer(this, OnExpandedCollapseElapsed);
        _greetingCollapse = new IslandTimer(this, OnGreetingCollapseElapsed);
    }

    public event Action<IslandSnapshot>? Changed;

    public IslandOptions Options => _options;

    public IslandSnapshot Snapshot => new(
        _mode, _view, _pinned, _hoverOpened, _pointerInside,
        _expandedCollapse.DueAt, _expandedCollapse.IsRunning ? _collapseTotal : TimeSpan.Zero);

    // ---- Lifecycle -----------------------------------------------------------------------------------

    /// <summary>App launch: Expanded on the greeting scene.</summary>
    public void Start()
    {
        _greetingActive = true;
        _view = IslandView.Greeting;
        _mode = IslandMode.Expanded;
        Publish();
    }

    /// <summary>The greeting timeline ended. Folds after 0.6 s (10 s while hovered); held alerts are raised now.</summary>
    public void GreetingFinished()
    {
        if (!_greetingActive) return;
        _greetingActive = false;

        if (_alertHeld)
        {
            _alertHeld = false;
            ShowAlert();
            return;
        }

        _greetingCollapse.Start(_pointerInside ? _options.GreetingCollapseHovered : _options.GreetingCollapse);
        Publish();
    }

    public void UpdateOptions(IslandOptions options)
    {
        _options = options;
        // A delay change restarts any running countdown with the new value (Spec 2.1 / 3.2 G).
        if (_expandedCollapse.IsRunning) StartExpandedCollapse();
        if (_compactHide.IsRunning) _compactHide.Start(_options.CompactHide);
        Publish();
    }

    // ---- Pointer -------------------------------------------------------------------------------------

    public void PointerEntered()
    {
        if (_pointerInside) return;
        _pointerInside = true;
        _expandedCollapse.Stop();
        _compactHide.Stop();

        if (_mode == IslandMode.Closed) SetMode(IslandMode.Compact);
        Publish();
    }

    public void PointerLeft()
    {
        if (!_pointerInside) return;
        _pointerInside = false;
        _faceOpen.Stop();

        if (_greetingActive || _greetingCollapse.IsRunning)
        {
            // Pointer leaving the greeting folds it immediately.
            if (!_greetingActive) { _greetingCollapse.Stop(); CollapseToCompact(); }
            Publish();
            return;
        }

        if (_mode == IslandMode.Expanded) StartExpandedCollapse();
        else if (_mode == IslandMode.Compact) StartCompactHide();
        Publish();
    }

    public void FaceEntered()
    {
        if (_mode != IslandMode.Compact || !_options.OpenOnFaceHover) return;
        _faceOpen.Start(_options.FaceHoverOpen);
    }

    public void FaceLeft() => _faceOpen.Stop();

    // ---- User actions --------------------------------------------------------------------------------

    /// <summary>Click anywhere on the island.</summary>
    public void Click()
    {
        _faceOpen.Stop();
        if (_mode == IslandMode.Compact)
        {
            Open(_hasAlerts ? IslandView.Alert : IslandView.Chat, hoverOpened: false);
            return;
        }
        Activity();
    }

    /// <summary>Key, click, typing or slap inside the expanded island: restarts the auto-close countdown.</summary>
    public void Activity()
    {
        if (_mode != IslandMode.Expanded) return;
        bool changed = _hoverOpened;
        _hoverOpened = false; // any interaction converts a hover-open into a normal open
        if (_expandedCollapse.IsRunning) { StartExpandedCollapse(); changed = true; }
        if (changed) Publish();
    }

    /// <summary>Tray "Open"/"Upcoming", or programmatic open.</summary>
    public void Open(IslandView view = IslandView.Chat) => Open(view, hoverOpened: false);

    /// <summary>✕ or Esc. On an alert the island folds but stays pinned.</summary>
    public void CloseRequested()
    {
        if (_mode != IslandMode.Expanded || _greetingActive) return;
        CollapseToCompact();
        Publish();
    }

    /// <summary>Explicit hide (e.g. tray). Ignored while pinned.</summary>
    public void Hide()
    {
        if (_pinned || _greetingActive) return;
        StopAllTimers();
        SetMode(IslandMode.Closed);
        Publish();
    }

    // ---- Alerts --------------------------------------------------------------------------------------

    /// <summary>An item is due: force Expanded(Alert) and pin. Held while the greeting plays.</summary>
    public void RaiseAlert()
    {
        _hasAlerts = true;
        if (_greetingActive)
        {
            _alertHeld = true;
            return;
        }
        ShowAlert();
    }

    /// <summary>Reminder unanswered for 5 min: fold to Compact but stay pinned.</summary>
    public void FoldAlert()
    {
        if (_mode == IslandMode.Expanded && _view == IslandView.Alert) CloseRequested();
    }

    /// <summary>Every queued alert was answered: unpin and fold to Compact; the view returns to chat.</summary>
    public void AlertsCleared()
    {
        _hasAlerts = false;
        _alertHeld = false;
        if (!_pinned && _view != IslandView.Alert) return;
        _pinned = false;
        _view = IslandView.Chat;
        CollapseToCompact();
        Publish();
    }

    public void Dispose()
    {
        StopAllTimers();
    }

    // ---- Internals -----------------------------------------------------------------------------------

    private void Open(IslandView view, bool hoverOpened)
    {
        _faceOpen.Stop();
        _compactHide.Stop();
        _greetingCollapse.Stop();
        _view = view;
        _hoverOpened = hoverOpened;
        SetMode(IslandMode.Expanded);
        if (!_pointerInside) StartExpandedCollapse();
        else _expandedCollapse.Stop();
        Publish();
    }

    private void ShowAlert()
    {
        StopAllTimers();
        _pinned = true;
        _hoverOpened = false;
        _view = IslandView.Alert;
        SetMode(IslandMode.Expanded);
        Publish();
    }

    private void CollapseToCompact()
    {
        _expandedCollapse.Stop();
        _hoverOpened = false;
        SetMode(IslandMode.Compact);
        if (_view == IslandView.Greeting) _view = IslandView.Chat;
        if (!_pointerInside) StartCompactHide();
    }

    private void StartExpandedCollapse()
    {
        if (_pinned || _greetingActive) return;
        _collapseTotal = _hoverOpened ? _options.HoverOpenGrace : _options.AutoClose;
        _expandedCollapse.Start(_collapseTotal);
    }

    private void StartCompactHide()
    {
        if (_pinned) return;
        _compactHide.Start(_options.CompactHide);
    }

    private void SetMode(IslandMode mode) => _mode = mode;

    private void StopAllTimers()
    {
        _faceOpen.Stop();
        _compactHide.Stop();
        _expandedCollapse.Stop();
        _greetingCollapse.Stop();
    }

    private void OnFaceOpenElapsed()
    {
        if (_mode != IslandMode.Compact) return;
        Open(_hasAlerts ? IslandView.Alert : IslandView.Chat, hoverOpened: !_hasAlerts);
    }

    private void OnCompactHideElapsed()
    {
        if (_mode != IslandMode.Compact || _pinned || _pointerInside) return;
        SetMode(IslandMode.Closed);
        Publish();
    }

    private void OnExpandedCollapseElapsed()
    {
        if (_mode != IslandMode.Expanded || _pinned) return;
        CollapseToCompact();
        Publish();
    }

    private void OnGreetingCollapseElapsed()
    {
        if (_mode != IslandMode.Expanded || _view != IslandView.Greeting) return;
        CollapseToCompact();
        Publish();
    }

    private void Publish() => Changed?.Invoke(Snapshot);

    private void Dispatch(Action action)
    {
        if (_context is null) action();
        else _context.Post(static s => ((Action)s!)(), action);
    }

    /// <summary>One-shot cancellable timer; a generation counter drops callbacks from stopped/restarted runs.</summary>
    private sealed class IslandTimer(IslandStateMachine owner, Action onElapsed)
    {
        private ITimer? _timer;
        private int _generation;

        public DateTimeOffset? DueAt { get; private set; }
        public bool IsRunning => _timer is not null;

        public void Start(TimeSpan delay)
        {
            Stop();
            int generation = ++_generation;
            DueAt = owner._time.GetUtcNow() + delay;
            _timer = owner._time.CreateTimer(_ => owner.Dispatch(() =>
            {
                if (generation != _generation) return;
                Stop();
                onElapsed();
            }), null, delay, Timeout.InfiniteTimeSpan);
        }

        public void Stop()
        {
            _generation++;
            _timer?.Dispose();
            _timer = null;
            DueAt = null;
        }
    }
}
