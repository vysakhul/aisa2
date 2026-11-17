using static Notch.App.Interop.NativeMethods;

namespace Notch.App.Platform;

public enum NotchAnchor { Left, Center, Right }

/// <summary>Window bounds in physical pixels plus the monitor scale they were computed with.</summary>
internal readonly record struct Placement(RECT WindowPx, double Scale);

/// <summary>
/// Computes where the overlay window sits on the primary monitor. Everything is done in physical
/// pixels so the result can go straight to SetWindowPos without WPF's DIP rounding in between.
/// </summary>
internal sealed class PlacementService
{
    public const double MaxTopInsetDip = 200;

    public Placement Compute(NotchAnchor anchor, double edgeMarginDip, double hitMarginDip, double windowWidthDip, double windowHeightDip)
    {
        IntPtr monitor = MonitorFromPoint(new POINT(0, 0), MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);

        double scale = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;

        RECT mon = info.rcMonitor;
        RECT work = info.rcWork;

        int width = (int)Math.Round(windowWidthDip * scale);
        int height = (int)Math.Round(windowHeightDip * scale);

        // A top-docked taskbar pushes the work area down; sit just below it.
        int topInset = Math.Clamp(work.Top - mon.Top, 0, (int)Math.Round(MaxTopInsetDip * scale));
        int y = mon.Top + topInset;

        // The island sits inside a transparent hit-margin, so pull the window out by that margin to keep
        // the visible black edge exactly edgeMargin from the corner.
        int edge = (int)Math.Round((edgeMarginDip - hitMarginDip) * scale);
        int x = anchor switch
        {
            NotchAnchor.Left => work.Left + edge,
            NotchAnchor.Right => work.Right - edge - width,
            _ => mon.Left + (mon.Width - width) / 2,
        };

        return new Placement(new RECT(x, y, x + width, y + height), scale);
    }
}
