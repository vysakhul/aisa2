using System.Windows.Threading;
using static Aisa.App.Interop.NativeMethods;

namespace Aisa.App.Platform;

/// <summary>
/// Background global-cursor poller (Spec 1.4 / 4.1): 16 ms near, 50 ms far, parked at 250 ms while folded.
/// Drives the hit-test controller directly on its own thread, so click-through flips even if the UI
/// thread is busy, and marshals coalesced events to the dispatcher.
/// </summary>
internal sealed class GlobalCursorMonitor : IDisposable
{
    public const double ProximityDip = 420;

    private readonly HitTestController _hitTest;
    private readonly Dispatcher _dispatcher;
    private readonly Thread _thread;
    private volatile bool _stop;
    private volatile bool _folded;
    private volatile PlacementBox? _placement;

    private long _latestPacked;
    private int _movePending;

    public event Action? Entered;
    public event Action? Left;
    /// <summary>Cursor position in physical screen pixels; only raised within 420 px of the window.</summary>
    public event Action<int, int>? Moved;
    public event Action? WentFar;

    public GlobalCursorMonitor(HitTestController hitTest, Dispatcher dispatcher)
    {
        _hitTest = hitTest;
        _dispatcher = dispatcher;
        _thread = new Thread(Run) { IsBackground = true, Name = "Aisa cursor monitor", Priority = ThreadPriority.AboveNormal };
    }

    /// <summary>While folded the poll parks at 250 ms and only checks for a wake on the notch.</summary>
    public bool Folded { get => _folded; set => _folded = value; }

    public void UpdatePlacement(Placement placement) => _placement = new PlacementBox(placement);

    public void Start() => _thread.Start();

    public void Dispose() => _stop = true;

    private void Run()
    {
        using var timer = new HighResolutionTimer();
        bool wasInside = false, wasNear = false;

        while (!_stop)
        {
            bool folded = _folded;
            if (GetCursorPos(out POINT p)) // fails on the secure desktop (UAC / lock screen)
            {
                bool leftDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
                bool inside = _hitTest.Evaluate(p, leftDown);
                if (inside != wasInside)
                {
                    wasInside = inside;
                    Post(inside ? Entered : Left);
                }

                if (!folded && _placement is { } box)
                {
                    bool near = DistanceToRect(box.Value.WindowPx, p) <= ProximityDip * box.Value.Scale;
                    if (near) PostMove(p);
                    else if (wasNear) Post(WentFar); // one final "far" event, then silence
                    wasNear = near;
                }
                else
                {
                    wasNear = false;
                }
            }

            timer.Wait(folded ? 250 : wasNear ? 16 : 50);
        }
    }

    private void Post(Action? handler)
    {
        if (handler is not null) _dispatcher.BeginInvoke(DispatcherPriority.Input, handler);
    }

    // Coalesce: at most one pending Moved callback; it always delivers the latest sample.
    private void PostMove(POINT p)
    {
        Interlocked.Exchange(ref _latestPacked, ((long)p.X << 32) | (uint)p.Y);
        if (Interlocked.Exchange(ref _movePending, 1) == 1) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            Interlocked.Exchange(ref _movePending, 0);
            long packed = Interlocked.Read(ref _latestPacked);
            Moved?.Invoke((int)(packed >> 32), (int)(packed & 0xFFFFFFFF));
        });
    }

    private static double DistanceToRect(RECT r, POINT p)
    {
        double dx = Math.Max(Math.Max(r.Left - p.X, 0), p.X - r.Right);
        double dy = Math.Max(Math.Max(r.Top - p.Y, 0), p.Y - r.Bottom);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private sealed class PlacementBox(Placement value) { public readonly Placement Value = value; }
}

/// <summary>
/// Thread.Sleep(16) rounds up to the ~15.6 ms system tick (often ~31 ms). A high-resolution waitable
/// timer (Win10 1803+) gives a real 16 ms cadence without raising the global timer resolution.
/// </summary>
internal sealed class HighResolutionTimer : IDisposable
{
    private readonly IntPtr _handle = CreateWaitableTimerExW(IntPtr.Zero, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);

    public void Wait(int milliseconds)
    {
        if (_handle == IntPtr.Zero)
        {
            Thread.Sleep(milliseconds);
            return;
        }
        long due = -milliseconds * 10_000L; // negative = relative, in 100 ns units
        SetWaitableTimer(_handle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false);
        WaitForSingleObject(_handle, INFINITE);
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero) CloseHandle(_handle);
    }
}
