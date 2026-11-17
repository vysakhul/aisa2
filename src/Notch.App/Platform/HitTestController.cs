using Notch.App.Interop;
using static Notch.App.Interop.NativeMethods;

namespace Notch.App.Platform;

/// <summary>
/// Owns the window's click-through state. The UI thread publishes the island rect (already inflated by
/// the 14 px hit margin, in physical screen pixels); the cursor thread calls <see cref="Evaluate"/> and the
/// WS_EX_TRANSPARENT bit is only touched when the inside/outside state actually flips.
/// </summary>
internal sealed class HitTestController(WindowStyles styles)
{
    private bool _interactive; // cursor thread only

    public void UpdateRegion(RECT screenRectPx) => Volatile.Write(ref _boxed, new StrongBox(screenRectPx));

    public void ClearRegion() => Volatile.Write(ref _boxed, null);

    /// <returns>True when the cursor is on the island (including the margin).</returns>
    public bool Evaluate(POINT cursor, bool leftButtonDown)
    {
        var region = Volatile.Read(ref _boxed);
        bool inside = region is not null && region.Value.Contains(cursor);

        // A press that started on the island keeps input captured, so drags never fall through mid-gesture.
        bool wantInput = inside || (leftButtonDown && _interactive);
        if (wantInput != _interactive)
        {
            _interactive = wantInput;
            if (wantInput) styles.Update(set: 0, clear: WS_EX_TRANSPARENT);
            else styles.Update(set: WS_EX_TRANSPARENT, clear: 0);
        }
        return inside;
    }

    // RECT is a struct; box it so the cross-thread publish is a single atomic reference write.
    private sealed class StrongBox(RECT value) { public readonly RECT Value = value; }
    private StrongBox? _boxed;
}
