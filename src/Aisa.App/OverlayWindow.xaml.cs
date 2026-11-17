using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Aisa.App.Animation;
using Aisa.App.Interop;
using Aisa.App.Platform;
using static Aisa.App.Interop.NativeMethods;

namespace Aisa.App;

/// <summary>
/// Spec 4.1 top-level overlay: borderless, per-pixel transparent, topmost tool window that never
/// activates on click and is click-through everywhere except the island (+14 px). This class also hosts
/// a throwaway Closed/Compact/Expanded ladder so window physics and hit-testing can be validated
/// before the headless IslandStateMachine replaces it.
/// </summary>
public partial class OverlayWindow : Window
{
    private enum IslandMode { Closed = 0, Compact = 1, Expanded = 2 }

    private static readonly Dictionary<IslandMode, (double W, double H, double R)> Shapes = new()
    {
        [IslandMode.Closed] = (120, 24, 10),
        [IslandMode.Compact] = (288, 32, 14),
        [IslandMode.Expanded] = (640, 160, 22),
    };

    private const double HitMarginDip = 14;
    private const double EdgeMarginDip = 12;
    // Spec says 720 × 320; +40 leaves room for the 14 px hit margin and spring overshoot on the 320 px view.
    private const double OpenWindowWidthDip = 720, OpenWindowHeightDip = 360;
    private const double ShrinkSeconds = 0.34;

    private static readonly TimeSpan ExpandedIdle = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CompactIdle = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WindowFoldLag = TimeSpan.FromMilliseconds(420);

    private readonly PlacementService _placementService = new();
    private readonly TrackedValue _width, _height, _radius;
    private readonly FrameLoop _frames;
    private readonly DispatcherTimer _idleTimer, _windowFoldTimer, _placementTimer;

    private IntPtr _hwnd;
    private WindowStyles _styles = null!;
    private HitTestController _hitTest = null!;
    private GlobalCursorMonitor _cursor = null!;
    private FocusController _focus = null!;

    private IslandMode _mode = IslandMode.Closed;
    private NotchAnchor _anchor = NotchAnchor.Center;
    private Placement _placement;
    private bool _windowFolded = true;
    private Rect _lastHitRectDip = Rect.Empty;
    private bool _allowClose;

    public OverlayWindow()
    {
        InitializeComponent();

        var closed = Shapes[IslandMode.Closed];
        _width = new TrackedValue(closed.W);
        _height = new TrackedValue(closed.H);
        _radius = new TrackedValue(closed.R);
        _frames = new FrameLoop(OnFrame);

        _idleTimer = new DispatcherTimer { Interval = CompactIdle };
        _idleTimer.Tick += (_, _) => { _idleTimer.Stop(); SetMode(_mode == IslandMode.Expanded ? IslandMode.Compact : IslandMode.Closed); };

        _windowFoldTimer = new DispatcherTimer { Interval = WindowFoldLag };
        _windowFoldTimer.Tick += (_, _) => { _windowFoldTimer.Stop(); ResizeWindow(folded: true); };

        // Spec 1.1: re-evaluate placement every 0.5 s (cheap; only moves the window if something changed).
        _placementTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _placementTimer.Tick += (_, _) => ResizeWindow(_windowFolded);

        SourceInitialized += OnSourceInitialized;
        KeyDown += OnKeyDown;
        PreviewMouseDown += (_, _) => RestartIdleIfExpanded();
        PreviewKeyDown += (_, _) => RestartIdleIfExpanded();
    }

    public NotchAnchor Anchor
    {
        get => _anchor;
        set { _anchor = value; ResizeWindow(_windowFolded); }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _styles = new WindowStyles(_hwnd);

        // TOOLWINDOW hides from Alt-Tab; NOACTIVATE keeps clicks from stealing focus; TRANSPARENT starts
        // the window click-through until the cursor monitor sees the pointer on the island.
        _styles.Update(
            set: WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT,
            clear: WS_EX_APPWINDOW,
            frameChanged: true);

        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);

        _hitTest = new HitTestController(_styles);
        _focus = new FocusController(this, _hwnd, _styles);
        _cursor = new GlobalCursorMonitor(_hitTest, Dispatcher);
        _cursor.Entered += OnPointerEntered;
        _cursor.Left += OnPointerLeft;
        // _cursor.Moved += (x, y) => characterEngine.SetGaze(...)   // Phase 3

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        ResizeWindow(folded: true);
        _cursor.Folded = true;
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

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() => ResizeWindow(_windowFolded));

    // ---- Island ladder (temporary; replaced by IslandStateMachine in Phase 2) -------------------------

    private void SetMode(IslandMode next)
    {
        if (next == _mode) return;
        bool grow = next > _mode;
        _mode = next;
        _windowFoldTimer.Stop();

        // Spec 1.3: on wake the OS window grows back *before* the island starts animating.
        if (grow && _windowFolded) ResizeWindow(folded: false);

        var (w, h, r) = Shapes[next];
        if (grow)
        {
            _width.AnimateSpring(w);
            _height.AnimateSpring(h);
            _radius.AnimateSpring(r);
        }
        else
        {
            _width.AnimateCurve(w, ShrinkSeconds, CubicBezier.Shrink);
            _height.AnimateCurve(h, ShrinkSeconds, CubicBezier.Shrink);
            _radius.AnimateCurve(r, ShrinkSeconds, CubicBezier.Shrink);
        }

        FadeContent(next == IslandMode.Expanded);
        if (next == IslandMode.Expanded) _focus.EnterTextInput(InputBox);
        else _focus.ExitTextInput();

        if (next == IslandMode.Closed) _windowFoldTimer.Start();
        _frames.Wake();
    }

    private void FadeContent(bool show)
    {
        ContentLayer.IsHitTestVisible = show;
        var anim = show
            ? new DoubleAnimation(1, TimeSpan.FromMilliseconds(300))
            {
                BeginTime = TimeSpan.FromMilliseconds(160), // the island leads, content follows
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            }
            : new DoubleAnimation(0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            };
        ContentLayer.BeginAnimation(OpacityProperty, anim);
    }

    private void OnPointerEntered()
    {
        _idleTimer.Stop();
        if (_mode == IslandMode.Closed) SetMode(IslandMode.Compact); // folded wake
    }

    private void OnPointerLeft()
    {
        if (_mode == IslandMode.Closed) return;
        _idleTimer.Interval = _mode == IslandMode.Expanded ? ExpandedIdle : CompactIdle;
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    private void RestartIdleIfExpanded()
    {
        if (_mode == IslandMode.Expanded && _idleTimer.IsEnabled)
        {
            _idleTimer.Stop();
            _idleTimer.Start();
        }
    }

    private void OnIslandMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_mode == IslandMode.Compact) SetMode(IslandMode.Expanded);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _mode == IslandMode.Expanded)
        {
            SetMode(IslandMode.Compact);
            e.Handled = true;
        }
    }

    // ---- Frame loop, geometry, hit region --------------------------------------------------------

    private bool OnFrame(double dt)
    {
        // Non-short-circuit | so every value steps each frame.
        bool moving = _width.Step(dt) | _height.Step(dt) | _radius.Step(dt);

        Island.Width = Math.Max(0, _width.Value);
        Island.Height = Math.Max(0, _height.Value);
        double r = Math.Max(0, _radius.Value);
        Island.CornerRadius = new CornerRadius(0, 0, r, r);

        PushHitRegion();
        return moving;
    }

    /// <summary>
    /// Computes the host rect analytically (layout for this frame hasn't run yet when Rendering fires)
    /// and publishes it in physical screen pixels when it moved by more than 0.5 px.
    /// </summary>
    private void PushHitRegion(bool force = false)
    {
        double scale = _placement.Scale <= 0 ? 1 : _placement.Scale;
        double windowWidthDip = _placement.WindowPx.Width / scale;

        double hostW = Math.Max(0, _width.Value) + 2 * HitMarginDip;
        double hostH = Math.Max(0, _height.Value) + HitMarginDip;
        double hostX = _anchor switch
        {
            NotchAnchor.Left => 0,
            NotchAnchor.Right => windowWidthDip - hostW,
            _ => (windowWidthDip - hostW) / 2,
        };
        var rectDip = new Rect(hostX, 0, hostW, hostH);

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

        var closed = Shapes[IslandMode.Closed];
        double w = folded ? closed.W + 2 * HitMarginDip : OpenWindowWidthDip;
        double h = folded ? closed.H + HitMarginDip : OpenWindowHeightDip;

        var placement = _placementService.Compute(_anchor, EdgeMarginDip, HitMarginDip, w, h);
        bool changed = placement != _placement || folded != _windowFolded;
        _windowFolded = folded;
        _cursor.Folded = folded;

        IslandHost.HorizontalAlignment = _anchor switch
        {
            NotchAnchor.Left => HorizontalAlignment.Left,
            NotchAnchor.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };

        // Re-assert TOPMOST every pass: other topmost windows (and some full-screen transitions) can bury it.
        var r = placement.WindowPx;
        SetWindowPos(_hwnd, HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, SWP_NOACTIVATE);
        if (!changed) return;

        _placement = placement;
        _cursor.UpdatePlacement(placement);
        PushHitRegion(force: true);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Spec 1.4: not closable by Alt+F4; only the tray's Quit sets _allowClose.
        if (!_allowClose) e.Cancel = true;
        base.OnClosing(e);
    }

    public void Quit()
    {
        _allowClose = true;
        _cursor.Dispose();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Close();
        Application.Current.Shutdown();
    }
}
