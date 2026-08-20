using NeatWin.Windows;

namespace NeatWin.App;

/// <summary>
/// Requests a tidy shortly after the user finishes an interactive window move/resize. WinEvent
/// callbacks may arrive off the UI thread, so they are marshalled through a message-only window
/// and debounced on the WinForms message loop. Programmatic moves performed by NeatWin can be
/// temporarily suppressed to avoid feedback loops.
/// </summary>
internal sealed class AutoTidyManager : NativeWindow, IDisposable
{
    private const int AutoTidyMessage = 0x8000 + 73;
    private const int DebounceMilliseconds = 250;

    private readonly NativeMethods.WinEventProc _winEventProc;
    private readonly System.Windows.Forms.Timer _debounceTimer;
    private readonly nint _hook;
    private long _suppressUntilTick;
    private bool _enabled;
    private bool _disposed;

    internal AutoTidyManager()
    {
        CreateHandle(new CreateParams
        {
            Caption = "NeatWin.AutoTidyWindow",
            Parent = NativeMethods.HwndMessage,
        });

        _debounceTimer = new System.Windows.Forms.Timer
        {
            Interval = DebounceMilliseconds,
        };
        _debounceTimer.Tick += OnDebounceTick;

        _winEventProc = OnWinEvent;
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemMoveSizeEnd,
            NativeMethods.EventSystemMoveSizeEnd,
            nint.Zero,
            _winEventProc,
            0,
            0,
            NativeMethods.WinEventOutOfContext | NativeMethods.WinEventSkipOwnProcess);
    }

    internal event Action? TidyRequested;

    internal bool IsAvailable => _hook != nint.Zero;

    internal bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value && IsAvailable;
            if (!_enabled)
            {
                _debounceTimer.Stop();
            }
        }
    }

    internal void SuppressFor(int milliseconds)
    {
        var until = Environment.TickCount64 + Math.Max(0, milliseconds);
        Interlocked.Exchange(ref _suppressUntilTick, until);
        _debounceTimer.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _enabled = false;
        _debounceTimer.Stop();
        _debounceTimer.Tick -= OnDebounceTick;
        _debounceTimer.Dispose();

        if (_hook != nint.Zero)
        {
            _ = NativeMethods.UnhookWinEvent(_hook);
        }

        DestroyHandle();
        GC.SuppressFinalize(this);
    }

    private bool IsSuppressed => Environment.TickCount64 < Interlocked.Read(ref _suppressUntilTick);

    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        if (_disposed || !_enabled || hwnd == nint.Zero || IsSuppressed)
        {
            return;
        }

        _ = NativeMethods.PostMessage(Handle, AutoTidyMessage, hwnd, nint.Zero);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == AutoTidyMessage)
        {
            if (_enabled && !IsSuppressed)
            {
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
            return;
        }

        base.WndProc(ref message);
    }

    private void OnDebounceTick(object? sender, EventArgs eventArgs)
    {
        _debounceTimer.Stop();
        if (_disposed || !_enabled || IsSuppressed)
        {
            return;
        }

        TidyRequested?.Invoke();
    }
}
