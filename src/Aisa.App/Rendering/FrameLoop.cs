using System.Windows.Media;

namespace Aisa.App.Rendering;

/// <summary>
/// Single vsync-locked frame driver (CompositionTarget.Rendering). Unsubscribes itself when the tick
/// reports nothing is moving, so an idle island costs ~0% CPU.
/// </summary>
public sealed class FrameLoop(Func<double, bool> tick)
{
    private const double MaxDelta = 0.05;
    private TimeSpan? _last;
    private bool _running;

    public bool IsRunning => _running;

    public void Wake()
    {
        if (_running) return;
        _running = true;
        _last = null;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_last == now) return; // Rendering can fire more than once per frame
        double dt = _last is { } last ? Math.Min((now - last).TotalSeconds, MaxDelta) : 1.0 / 60;
        _last = now;

        if (!tick(dt))
        {
            CompositionTarget.Rendering -= OnRendering;
            _running = false;
        }
    }
}
