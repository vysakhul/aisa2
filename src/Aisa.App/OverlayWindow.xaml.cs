using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Aisa.App.Alerts;
using Aisa.App.Chat;
using Aisa.App.Interop;
using Aisa.App.Platform;
using Aisa.App.Rendering;
using Aisa.Character;
using Aisa.Character.Rendering;
using Aisa.Core.Animation;
using Aisa.Core.Island;
using Aisa.Core.Settings;
using Microsoft.Win32;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using static Aisa.App.Interop.NativeMethods;

namespace Aisa.App;

/// <summary>
/// Spec 4.1/4.2 overlay: borderless, per-pixel transparent, topmost tool window that never activates on click and
/// is click-through everywhere except the island (+14 px). Renders what the headless IslandStateMachine and
/// CharacterEngine decide; it owns no timers of its own besides the 420 ms window fold.
/// </summary>
public partial class OverlayWindow : Window
{
    private const double HitMarginDip = 14;
    private const double OpenWindowWidthDip = 720, OpenWindowHeightDip = 360; // 720 × 320 + room for the hit margin and spring overshoot
    private const double ShrinkSeconds = 0.34;
    private const double FaceCanvasDip = 160;
    private const double GreetingLandY = 50;

    private static readonly (double W, double H, double R) ClosedShape = (120, 24, 10);
    private static readonly (double W, double H, double R) CompactShape = (288, 32, 14);
    private static readonly TimeSpan WindowFoldLag = TimeSpan.FromMilliseconds(420);
    private static readonly CubicBezier DropEase = new(0.55, 0, 1, 0.45);
    private static readonly IEasingFunction OvershootEase = new BezierEase(new CubicBezier(0.3, 1.2, 0.4, 1));

    private readonly IslandStateMachine _island;
    private readonly CharacterEngine _character;
    private readonly ChatController _chat;
    private readonly SettingsStore _settings;
    private readonly PlacementService _placementService = new();
    private readonly TrackedValue _width, _height, _radius;
    private readonly TrackedValue _faceX = new(-50), _faceY = new(12), _faceSize = new(6), _faceOpacity = new(0);
    private readonly FrameLoop _frames;
    private readonly DispatcherTimer _windowFoldTimer, _placementTimer, _characterWake;

    private IntPtr _hwnd;
    private WindowStyles _styles = null!;
    private HitTestController _hitTest = null!;
    private GlobalCursorMonitor _cursor = null!;
    private FocusController _focus = null!;

    private IslandSnapshot _snapshot;
    private IslandAnchor _anchor;
    private Placement _placement;
    private bool _windowFolded = true;
    private Rect _lastHitRectDip = Rect.Empty;
    private bool _allowClose;
    private FrameworkElement? _activeView;
    private int _chatMessages;
    private (double W, double H) _targetShape;
    private bool _faceHovered;
    private bool _faceMoved = true;

    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _lastCharacterStep;

    private double _greetingT = -1; // seconds into the launch timeline; < 0 when not greeting
    private int _greetingStep;

    public OverlayWindow(IslandStateMachine island, CharacterEngine character, ChatController chat, AlertCoordinator alerts, SettingsStore settings)
    {
        InitializeComponent();

        _island = island;
        _character = character;
        _chat = chat;
        _settings = settings;
        _anchor = settings.Current.Anchor;
        _snapshot = island.Snapshot;

        ChatPane.DataContext = chat;
        AlertPane.DataContext = alerts;
        ChatPane.MessageCountChanged += count => { _chatMessages = count; UpdateShape(); };

        _width = new TrackedValue(ClosedShape.W);
        _height = new TrackedValue(ClosedShape.H);
        _radius = new TrackedValue(ClosedShape.R);
        _frames = new FrameLoop(OnFrame);

        _windowFoldTimer = new DispatcherTimer { Interval = WindowFoldLag };
        _windowFoldTimer.Tick += (_, _) => { _windowFoldTimer.Stop(); if (_snapshot.Mode == IslandMode.Closed) ResizeWindow(folded: true); };

        // Spec 1.1: re-evaluate placement every 0.5 s (only moves the window if something changed).
        _placementTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _placementTimer.Tick += (_, _) => ResizeWindow(_windowFolded);

        // While the character is settled the frame loop sleeps; this wakes it for the next blink.
        _characterWake = new DispatcherTimer();
        _characterWake.Tick += (_, _) => { _characterWake.Stop(); _frames.Wake(); };

        island.Changed += s => ApplySnapshot(s);
        character.Invalidated += () => { _characterWake.Stop(); _frames.Wake(); };
        settings.Changed += OnSettingsChanged;

        SourceInitialized += OnSourceInitialized;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += (_, _) => _island.Activity();
        UpdateSpeakerGlyph(settings.Current.SoundEnabled);
    }

    public event Action? SettingsRequested;

    /// <summary>Launch: plays the greeting timeline, then hands over to the state machine.</summary>
    public void StartGreeting()
    {
        _greetingT = 0;
        _greetingStep = 0;
        GreetingCanvas.Visibility = Visibility.Visible;
        _island.Start();
    }

    // ---- Window setup --------------------------------------------------------------------------------

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _styles = new WindowStyles(_hwnd);

        // TOOLWINDOW hides from Alt-Tab; NOACTIVATE keeps clicks from stealing focus; TRANSPARENT starts
        // the window click-through until the cursor monitor sees the pointer on the island.
        _styles.Update(set: WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT, clear: WS_EX_APPWINDOW, frameChanged: true);
        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);

        _hitTest = new HitTestController(_styles);
        _focus = new FocusController(this, _hwnd, _styles);
        _cursor = new GlobalCursorMonitor(_hitTest, Dispatcher);
        _cursor.Entered += () => _island.PointerEntered();
        _cursor.Left += () => _island.PointerLeft();
        _cursor.Moved += OnCursorMoved;
        _cursor.WentFar += OnCursorFar;

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        ResizeWindow(folded: true);
        _cursor.Start();
        _placementTimer.Start();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_MOUSEACTIVATE when !_focus.AcceptsFocus:
                handled = true;
                return new IntPtr(MA_NOACTIVATE);

            case WM_DPICHANGED:
            case WM_DISPLAYCHANGE:
                // Let WPF apply its own DPI handling first, then re-anchor in physical pixels.
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () => ResizeWindow(_windowFolded));
                break;
        }
        return IntPtr.Zero;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => ResizeWindow(_windowFolded));

    private void OnSettingsChanged(AisaSettings s) => Dispatcher.BeginInvoke(() =>
    {
        UpdateSpeakerGlyph(s.SoundEnabled);
        if (s.Anchor != _anchor)
        {
            _anchor = s.Anchor;
            ResizeWindow(_windowFolded);
        }
    });

    // ---- Island state → visuals ----------------------------------------------------------------------

    private void UpdateShape()
    {
        var s = _snapshot;
        var shape = ShapeFor(s);
        if (shape.W == _targetShape.W && shape.H == _targetShape.H) return;

        bool grow = shape.W > _targetShape.W || shape.H > _targetShape.H;
        _targetShape = (shape.W, shape.H);

        // Spec 1.3: on wake the OS window grows back before the island starts animating.
        if (s.Mode != IslandMode.Closed && _windowFolded) ResizeWindow(folded: false);
        _windowFoldTimer.Stop();
        if (s.Mode == IslandMode.Closed) _windowFoldTimer.Start();

        Animate(_width, shape.W, grow);
        Animate(_height, shape.H, grow);
        Animate(_radius, shape.R, grow);
        ContentLayer.Height = Math.Max(shape.H, 1);
        _frames.Wake();
    }

    private void ApplySnapshot(IslandSnapshot s)
    {
        var previous = _snapshot;
        _snapshot = s;

        UpdateShape();
        MoveFaceToSlot(s);

        if (s.Mode != previous.Mode || s.View != previous.View)
        {
            bool expanded = s.Mode == IslandMode.Expanded;
            FadeContent(expanded);
            ShowView(expanded ? s.View : (IslandView?)null);
            Header.Visibility = s.View == IslandView.Greeting ? Visibility.Hidden : Visibility.Visible;
            TabLabel.Text = s.View == IslandView.Alert ? "Alert" : "Chat";
            CloseButton.ToolTip = s.View == IslandView.Alert ? "Hide (stays pinned)" : "Close (Esc)";
            _character.SetExpanded(expanded);

            if (expanded && s.View == IslandView.Chat) _focus.EnterTextInput(ChatPane.InputBox);
            else _focus.ExitTextInput();

            if (s.View != IslandView.Chat) _chat.Reset();
            if (previous.Mode == IslandMode.Closed && s.Mode == IslandMode.Compact) _character.Appear();
        }

        _frames.Wake();
    }

    private (double W, double H, double R) ShapeFor(IslandSnapshot s) => s.Mode switch
    {
        IslandMode.Closed => ClosedShape,
        IslandMode.Compact => CompactShape,
        _ => s.View switch
        {
            IslandView.Greeting => (640, 150, 22),
            IslandView.Alert => (640, 160, 22),
            _ when _chatMessages > 0 => (640, Math.Min(300, 240 + 40 * _chatMessages), 22),
            _ => (640, 160, 22),
        },
    };

    private static void Animate(TrackedValue value, double target, bool grow)
    {
        if (value.Target == target) return;
        if (grow) value.AnimateSpring(target);
        else value.AnimateCurve(target, ShrinkSeconds, CubicBezier.Shrink);
    }

    /// <summary>Spec 2.5 "gliding": the face springs to a per-mode slot (island-relative DIPs).</summary>
    private void MoveFaceToSlot(IslandSnapshot s)
    {
        if (_greetingT >= 0 && s.View == IslandView.Greeting) return; // the greeting timeline drives the face

        (double x, double y, double d, double opacity) slot = s.Mode switch
        {
            IslandMode.Closed => (-50, 12, 6, 0),
            IslandMode.Compact => (-104, 16, 20, 1),
            _ when s.View == IslandView.Greeting => (0, GreetingLandY, 44, 1),
            _ => (-264, 84, 44, 1),
        };

        _faceX.AnimateSpring(slot.x);
        _faceY.AnimateSpring(slot.y);
        _faceSize.AnimateSpring(slot.d);
        if (slot.opacity > _faceOpacity.Target) _faceOpacity.AnimateSpring(slot.opacity);
        else if (slot.opacity < _faceOpacity.Target) _faceOpacity.AnimateCurve(slot.opacity, ShrinkSeconds, CubicBezier.Shrink);
    }

    private void FadeContent(bool show)
    {
        ContentLayer.IsHitTestVisible = show;
        ContentLayer.BeginAnimation(OpacityProperty, show
            ? new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(160), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }
            : new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
    }

    /// <summary>Asymmetric cross-fade: out 160 ms (fade + 0.97 scale), in after 160 ms over 300 ms with slight overshoot.</summary>
    private void ShowView(IslandView? view)
    {
        FrameworkElement? next = view switch
        {
            IslandView.Chat => ChatPane,
            IslandView.Alert => AlertPane,
            IslandView.Greeting => GreetingPane,
            _ => _activeView, // collapsing: keep the current view; the content layer fades as a whole
        };
        if (next == _activeView) return;

        if (_activeView is { } outgoing)
        {
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fadeOut.Completed += (_, _) => { if (_activeView != outgoing) outgoing.Visibility = Visibility.Collapsed; };
            outgoing.BeginAnimation(OpacityProperty, fadeOut);
            outgoing.RenderTransform = Scale(1, 0.97, TimeSpan.Zero, TimeSpan.FromMilliseconds(160), null);
        }

        _activeView = next;
        if (next is null) return;
        next.Visibility = Visibility.Visible;
        next.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            BeginTime = TimeSpan.FromMilliseconds(160),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        next.RenderTransform = Scale(0.97, 1, TimeSpan.FromMilliseconds(160), TimeSpan.FromMilliseconds(300), OvershootEase);
    }

    private static ScaleTransform Scale(double from, double to, TimeSpan begin, TimeSpan duration, IEasingFunction? ease)
    {
        var t = new ScaleTransform(from, from);
        var a = new DoubleAnimation(from, to, duration) { BeginTime = begin, EasingFunction = ease };
        t.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        t.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        return t;
    }

    // ---- Frame loop ----------------------------------------------------------------------------------

    private bool OnFrame(double dt)
    {
        bool islandMoving = _width.Step(dt) | _height.Step(dt) | _radius.Step(dt);
        bool faceMoving = _faceX.Step(dt) | _faceY.Step(dt) | _faceSize.Step(dt) | _faceOpacity.Step(dt);
        bool greeting = StepGreeting(dt);

        // Real elapsed time, not the capped frame dt: after the loop sleeps until NextWakeIn the engine must see the full gap.
        double now = _clock.Elapsed.TotalSeconds;
        _character.Step(Math.Max(0, now - _lastCharacterStep));
        _lastCharacterStep = now;
        bool characterBusy = _character.IsBusy;

        Island.Width = Math.Max(0, _width.Value);
        Island.Height = Math.Max(0, _height.Value);
        double r = Math.Max(0, _radius.Value);
        Island.CornerRadius = new CornerRadius(0, 0, r, r);

        PlaceFaceCanvas();
        if (characterBusy || faceMoving || islandMoving || greeting || _faceMoved) FaceCanvas.InvalidateVisual();
        _faceMoved = false;
        if (greeting) GreetingCanvas.InvalidateVisual();

        bool countdown = UpdateCountdown();
        PushHitRegion();

        bool busy = islandMoving || faceMoving || greeting || characterBusy || countdown;
        if (!busy && _character.NextWakeIn is { } wake)
        {
            _characterWake.Interval = wake < TimeSpan.FromMilliseconds(16) ? TimeSpan.FromMilliseconds(16) : wake;
            _characterWake.Start();
        }
        return busy;
    }

    private double IslandLeftDip()
    {
        double scale = _placement.Scale <= 0 ? 1 : _placement.Scale;
        double windowWidthDip = _placement.WindowPx.Width / scale;
        double hostW = Math.Max(0, _width.Value) + 2 * HitMarginDip;
        double hostX = _anchor switch
        {
            IslandAnchor.Left => 0,
            IslandAnchor.Right => windowWidthDip - hostW,
            _ => (windowWidthDip - hostW) / 2,
        };
        return hostX + HitMarginDip;
    }

    /// <summary>
    /// Face slot X is measured from the island's horizontal centre so the glide stays continuous while the
    /// island's width animates (compact face at 40 px of 288 → −104; expanded at 56 px of 640 → −264).
    /// </summary>
    private Point FaceCenterDip()
    {
        double centre = IslandLeftDip() + Math.Max(0, _width.Value) / 2;
        return new Point(centre + _faceX.Value, _faceY.Value);
    }

    private void PlaceFaceCanvas()
    {
        var c = FaceCenterDip();
        Canvas.SetLeft(FaceCanvas, c.X - FaceCanvasDip / 2);
        Canvas.SetTop(FaceCanvas, c.Y - FaceCanvasDip / 2);
        FaceCanvas.Opacity = Math.Clamp(_faceOpacity.Value, 0, 1);
    }

    private void OnFacePaint(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (_faceOpacity.Value <= 0.01) return;

        float scale = (float)(e.Info.Width / FaceCanvasDip);
        var center = new SKPoint(e.Info.Width / 2f, e.Info.Height / 2f);
        float diameter = (float)Math.Max(1, _faceSize.Value) * scale;

        if (_snapshot.Mode == IslandMode.Expanded) CharacterRenderer.DrawHalo(canvas, _character.Pose, center, diameter);
        CharacterRenderer.Draw(canvas, _character.Pose, center, diameter);
    }

    /// <summary>Spec 1.3: 2 px bar, last min(10 s, 0.6 × delay), shrinking 160 → 0 px. Returns true while it should keep animating.</summary>
    private bool UpdateCountdown()
    {
        if (_snapshot.CollapseDueAt is not { } due || _snapshot.Mode != IslandMode.Expanded)
        {
            CountdownBar.Visibility = Visibility.Collapsed;
            return false;
        }

        double window = Math.Min(10, 0.6 * _snapshot.CollapseTotal.TotalSeconds);
        double remaining = (due - DateTimeOffset.UtcNow).TotalSeconds;
        if (remaining > window || remaining <= 0)
        {
            CountdownBar.Visibility = Visibility.Collapsed;
            return remaining > 0;
        }

        double w = 160 * remaining / window;
        CountdownBar.Visibility = Visibility.Visible;
        CountdownBar.Width = w;
        Canvas.SetLeft(CountdownBar, IslandLeftDip() + _width.Value / 2 - w / 2);
        Canvas.SetTop(CountdownBar, Math.Max(0, _height.Value - 4));
        return true;
    }

    // ---- Greeting (Spec 3.2 A, 4.3) ------------------------------------------------------------------

    private bool StepGreeting(double dt)
    {
        if (_greetingT < 0) return false;
        _greetingT += dt;
        double t = _greetingT;

        // Drop: from the notch's top centre into the panel, accelerating, growing to full size.
        const double dropTime = 0.45;
        if (_greetingStep == 0)
        {
            _faceX.Snap(0); _faceY.Snap(0); _faceSize.Snap(20); _faceOpacity.Snap(1);
            _greetingStep = 1;
        }
        if (_greetingStep == 1)
        {
            double p = Math.Min(1, t / dropTime);
            _faceY.Snap(GreetingLandY * DropEase.Evaluate(p));
            _faceSize.Snap(20 + 24 * p);
            if (p >= 1) { _character.Appear(); _greetingStep = 2; } // landing squash + wobble
        }
        if (_greetingStep == 2 && t >= 0.7) { _character.PlayEmote(Emote.Happy); _greetingStep = 3; }
        if (_greetingStep == 3 && t >= 1.4) { _character.PlayEmote(Emote.Wink); _greetingStep = 4; }
        if (_greetingStep == 4 && t >= 2.9) { _character.PlayEmote(Emote.Happy); _greetingStep = 5; }
        if (_greetingStep == 5 && t >= 4.6)
        {
            _greetingT = -1;
            GreetingCanvas.Visibility = Visibility.Collapsed;
            _island.GreetingFinished();
            return false;
        }
        return true;
    }

    private void OnGreetingPaint(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (_greetingT < 0) return;

        // Warp streaks radiating from the face during the drop, fading out by ~1.2 s.
        float scale = e.Info.Width / 640f;
        float fade = (float)Math.Clamp(1 - _greetingT / 1.2, 0, 1);
        if (fade <= 0) return;
        var c = new SKPoint(320 * scale, (float)_faceY.Value * scale);
        using var paint = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeWidth = 1.5f * scale, BlendMode = SKBlendMode.Plus };
        var rng = new Random(7);
        for (int i = 0; i < 48; i++)
        {
            double angle = rng.NextDouble() * Math.PI * 2;
            double speed = 120 + rng.NextDouble() * 260;
            double start = 18 + (_greetingT * speed) % 300;
            double len = 6 + rng.NextDouble() * 22 * fade;
            paint.Color = new SKColor(255, 255, 255, (byte)(fade * (90 + rng.Next(120))));
            var p0 = new SKPoint(c.X + (float)(Math.Cos(angle) * start * scale), c.Y + (float)(Math.Sin(angle) * start * scale));
            var p1 = new SKPoint(c.X + (float)(Math.Cos(angle) * (start + len) * scale), c.Y + (float)(Math.Sin(angle) * (start + len) * scale));
            canvas.DrawLine(p0, p1, paint);
        }
    }

    // ---- Pointer & input -----------------------------------------------------------------------------

    private void OnCursorMoved(int xPx, int yPx)
    {
        double scale = _placement.Scale <= 0 ? 1 : _placement.Scale;
        double x = (xPx - _placement.WindowPx.Left) / scale;
        double y = (yPx - _placement.WindowPx.Top) / scale;

        var face = FaceCenterDip();
        _character.SetGaze(x - face.X, y - face.Y);

        bool onFace = _faceOpacity.Value > 0.5 && Distance(x, y, face) <= _faceSize.Value / 2;
        _character.UpdateFacePointer(onFace, x, y);
        if (onFace != _faceHovered)
        {
            _faceHovered = onFace;
            if (onFace) _island.FaceEntered(); else _island.FaceLeft();
        }
        _frames.Wake();
    }

    private void OnCursorFar()
    {
        _character.ClearGaze();
        if (_faceHovered)
        {
            _faceHovered = false;
            _character.UpdateFacePointer(false, 0, 0);
            _island.FaceLeft();
        }
        _frames.Wake();
    }

    private static double Distance(double x, double y, Point p) => Math.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y));

    private void OnIslandMouseDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        var face = FaceCenterDip();
        bool onFace = Distance(p.X, p.Y, face) <= _faceSize.Value / 2;

        if (_snapshot.Mode == IslandMode.Expanded && onFace)
        {
            _character.Slap();   // the engine handles dizzy and requests its own sounds
            _island.Activity();  // slaps count as activity
            _frames.Wake();
        }
        else
        {
            _island.Click();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        _island.Activity();
        if (e.Key == Key.Escape)
        {
            _island.CloseRequested();
            e.Handled = true;
        }
        // Spec 1.4: suppress Ctrl +/–/0 zoom-style shortcuts.
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.OemPlus or Key.OemMinus or Key.Add or Key.Subtract or Key.D0 or Key.NumPad0)
            e.Handled = true;
    }

    private void OnGear(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void OnSpeaker(object sender, RoutedEventArgs e) => _settings.Update(s => s with { SoundEnabled = !s.SoundEnabled });

    private void OnClose(object sender, RoutedEventArgs e) => _island.CloseRequested();

    private void UpdateSpeakerGlyph(bool soundOn) => SpeakerButton.Content = soundOn ? "\uE767" : "\uE74F";

    // ---- Hit region & window placement ---------------------------------------------------------------

    /// <summary>
    /// Computes the host rect analytically (layout for this frame hasn't run yet when Rendering fires) and
    /// publishes it in physical screen pixels when it moved by more than 0.5 px.
    /// </summary>
    private void PushHitRegion(bool force = false)
    {
        if (_hitTest is null) return;
        double scale = _placement.Scale <= 0 ? 1 : _placement.Scale;
        double hostX = IslandLeftDip() - HitMarginDip;
        var rectDip = new Rect(hostX, 0, Math.Max(0, _width.Value) + 2 * HitMarginDip, Math.Max(0, _height.Value) + HitMarginDip);

        if (!force && !_lastHitRectDip.IsEmpty &&
            Math.Abs(rectDip.X - _lastHitRectDip.X) < 0.5 && Math.Abs(rectDip.Width - _lastHitRectDip.Width) < 0.5 &&
            Math.Abs(rectDip.Height - _lastHitRectDip.Height) < 0.5)
            return;
        _lastHitRectDip = rectDip;

        var origin = _placement.WindowPx;
        _hitTest.UpdateRegion(new RECT(
            origin.Left + (int)Math.Floor(rectDip.Left * scale),
            origin.Top + (int)Math.Floor(rectDip.Top * scale),
            origin.Left + (int)Math.Ceiling(rectDip.Right * scale),
            origin.Top + (int)Math.Ceiling(rectDip.Bottom * scale)));
    }

    private void ResizeWindow(bool folded)
    {
        if (_hwnd == IntPtr.Zero) return;

        double w = folded ? ClosedShape.W + 2 * HitMarginDip : OpenWindowWidthDip;
        double h = folded ? ClosedShape.H + HitMarginDip : OpenWindowHeightDip;

        var placement = _placementService.Compute(_anchor, _settings.Current.EdgeMargin, HitMarginDip, w, h);
        bool changed = placement != _placement || folded != _windowFolded;
        _windowFolded = folded;
        _cursor.Folded = folded;

        IslandHost.HorizontalAlignment = _anchor switch
        {
            IslandAnchor.Left => HorizontalAlignment.Left,
            IslandAnchor.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };

        // Re-assert TOPMOST every pass: other topmost windows (and some full-screen transitions) can bury it.
        var r = placement.WindowPx;
        SetWindowPos(_hwnd, HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, SWP_NOACTIVATE);
        if (!changed) return;

        _placement = placement;
        _cursor.UpdatePlacement(placement);
        PushHitRegion(force: true);
        _faceMoved = true;
        _frames.Wake();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Spec 1.4: not closable by Alt+F4; only Quit sets _allowClose.
        if (!_allowClose) e.Cancel = true;
        base.OnClosing(e);
    }

    public void Shutdown()
    {
        _allowClose = true;
        _cursor?.Dispose();
        _placementTimer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Close();
    }

    private sealed class BezierEase(CubicBezier curve) : IEasingFunction
    {
        public double Ease(double normalizedTime) => curve.Evaluate(normalizedTime);
    }
}
