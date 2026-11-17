using static Aisa.App.Interop.NativeMethods;

namespace Aisa.App.Interop;

/// <summary>
/// Serialises read-modify-write of WS_EX_* bits. The cursor thread flips WS_EX_TRANSPARENT while the
/// UI thread flips WS_EX_NOACTIVATE, so without the lock one update could silently undo the other.
/// </summary>
internal sealed class WindowStyles(IntPtr hwnd)
{
    private readonly object _gate = new();

    public void Update(long set, long clear, bool frameChanged = false)
    {
        lock (_gate)
        {
            long current = GetWindowLong(hwnd, GWL_EXSTYLE);
            long next = (current | set) & ~clear;
            if (next == current) return;
            SetWindowLong(hwnd, GWL_EXSTYLE, next);
        }

        if (frameChanged)
        {
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
    }

    public bool Has(long flag)
    {
        lock (_gate) return (GetWindowLong(hwnd, GWL_EXSTYLE) & flag) != 0;
    }
}
