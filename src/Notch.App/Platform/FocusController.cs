using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Notch.App.Interop;
using static Notch.App.Interop.NativeMethods;

namespace Notch.App.Platform;

/// <summary>
/// The window is non-activating by default. When a text-entry view appears it becomes focusable, takes
/// foreground, focuses the input 120 ms later, and hands foreground back to the previous app on exit.
/// </summary>
internal sealed class FocusController(Window window, IntPtr hwnd, WindowStyles styles)
{
    private static readonly TimeSpan InputFocusDelay = TimeSpan.FromMilliseconds(120);
    private IntPtr _previousForeground;
    private DispatcherTimer? _pending;

    public bool AcceptsFocus { get; private set; }

    public void EnterTextInput(IInputElement input)
    {
        if (AcceptsFocus) return;
        AcceptsFocus = true;

        IntPtr fg = GetForegroundWindow();
        if (fg != hwnd) _previousForeground = fg;

        styles.Update(set: 0, clear: WS_EX_NOACTIVATE);
        // Windows only grants foreground to a process that received the last input event; a click on the
        // island satisfies that, a hover-only open may not (then the caret simply appears on first click).
        window.Activate();

        _pending?.Stop();
        _pending = new DispatcherTimer(InputFocusDelay, DispatcherPriority.Input, (_, _) =>
        {
            _pending?.Stop();
            Keyboard.Focus(input);
        }, window.Dispatcher);
        _pending.Start();
    }

    public void ExitTextInput()
    {
        if (!AcceptsFocus) return;
        AcceptsFocus = false;
        _pending?.Stop();

        Keyboard.ClearFocus();
        styles.Update(set: WS_EX_NOACTIVATE, clear: 0);

        if (_previousForeground != IntPtr.Zero && IsWindow(_previousForeground))
            SetForegroundWindow(_previousForeground);
        _previousForeground = IntPtr.Zero;
    }
}
