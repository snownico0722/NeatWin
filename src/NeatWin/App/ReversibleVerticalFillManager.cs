using System.Runtime.InteropServices;
using NeatWin.Core;
using NeatWin.Windows;

namespace NeatWin.App;

/// <summary>
/// Windows does not expose a general public API that lets one process mark another process's
/// window as natively arranged/snapped. This helper preserves the important user-facing part of
/// vertical snap: after NeatWin fills a window from work-area top to bottom, dragging its title
/// bar restores the pre-tidy rectangle under the pointer.
/// </summary>
internal sealed class ReversibleVerticalFillManager : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<nint, RestoreEntry> _entries = new();
    private readonly NativeMethods.WinEventProc _winEventProc;
    private readonly nint _hook;
    private bool _disposed;

    internal ReversibleVerticalFillManager()
    {
        _winEventProc = OnWinEvent;
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemMoveSizeStart,
            NativeMethods.EventSystemMoveSizeStart,
            nint.Zero,
            _winEventProc,
            0,
            0,
            NativeMethods.WinEventOutOfContext | NativeMethods.WinEventSkipOwnProcess);
    }

    internal bool IsAvailable => _hook != nint.Zero;

    internal void Track(IReadOnlyList<TidyMove> plan, bool enabled)
    {
        lock (_gate)
        {
            if (!enabled || !IsAvailable)
            {
                _entries.Clear();
                return;
            }

            foreach (var move in plan)
            {
                var window = move.Window;
                var target = move.TargetVisualRect;
                var workArea = window.WorkArea;
                var targetFillsVertically =
                    Math.Abs(target.Top - workArea.Top) <= 1 &&
                    Math.Abs(target.Bottom - workArea.Bottom) <= 1;
                var originalAlreadyFilled =
                    Math.Abs(window.VisualRect.Top - workArea.Top) <= 1 &&
                    Math.Abs(window.VisualRect.Bottom - workArea.Bottom) <= 1;

                if (window.IsResizable && targetFillsVertically && !originalAlreadyFilled)
                {
                    _entries[window.Handle] = new RestoreEntry(
                        window.VisualRect,
                        target,
                        workArea,
                        window.FrameInsets);
                }
                else
                {
                    _entries.Remove(window.Handle);
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hook != nint.Zero)
        {
            _ = NativeMethods.UnhookWinEvent(_hook);
        }

        lock (_gate)
        {
            _entries.Clear();
        }

        GC.SuppressFinalize(this);
    }

    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        if (_disposed || hwnd == nint.Zero)
        {
            return;
        }

        RestoreEntry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(hwnd, out entry))
            {
                return;
            }
        }

        if (!NativeMethods.GetCursorPos(out var cursor) ||
            !TryGetVisualRect(hwnd, out var currentVisual) ||
            !LooksLikeTitleBarDrag(hwnd, currentVisual, cursor))
        {
            return;
        }

        // If some other tool or the application itself changed the window after NeatWin's tidy,
        // treat that as new user intent and do not restore stale geometry.
        if (!ApproximatelyMatches(currentVisual, entry.TargetVisualRect, tolerance: 12))
        {
            lock (_gate)
            {
                _entries.Remove(hwnd);
            }
            return;
        }

        var restoredVisual = PlaceRestoreRectUnderCursor(entry, currentVisual, cursor);
        var outer = ToOuterRect(restoredVisual, entry.FrameInsets);
        var success = NativeMethods.SetWindowPos(
            hwnd,
            nint.Zero,
            outer.X,
            outer.Y,
            outer.Width,
            outer.Height,
            NativeMethods.SwpNoZOrder |
            NativeMethods.SwpNoActivate |
            NativeMethods.SwpNoOwnerZOrder);

        if (success)
        {
            lock (_gate)
            {
                _entries.Remove(hwnd);
            }
        }
    }

    private static bool LooksLikeTitleBarDrag(
        nint hwnd,
        RectI visualRect,
        NativeMethods.Point cursor)
    {
        if (visualRect.IsEmpty ||
            cursor.X < visualRect.Left || cursor.X > visualRect.Right ||
            cursor.Y < visualRect.Top || cursor.Y > visualRect.Bottom)
        {
            return false;
        }

        var dpi = Math.Max(96u, NativeMethods.GetDpiForWindow(hwnd));
        var scale = dpi / 96.0;
        var resizeBorder = Math.Max(7, (int)Math.Round(9 * scale));
        var titleBand = Math.Max(36, (int)Math.Round(56 * scale));
        var topOffset = cursor.Y - visualRect.Top;

        // Exclude resize borders/corners. Custom title bars vary, so the accepted band is a little
        // wider than the classic caption metric but still confined to the top of the window.
        return topOffset >= resizeBorder &&
               topOffset <= titleBand &&
               cursor.X - visualRect.Left >= resizeBorder * 2 &&
               visualRect.Right - cursor.X >= resizeBorder * 2;
    }

    private static RectI PlaceRestoreRectUnderCursor(
        RestoreEntry entry,
        RectI currentVisual,
        NativeMethods.Point cursor)
    {
        var original = entry.OriginalVisualRect;
        var workArea = entry.WorkArea;
        var horizontalFraction = currentVisual.Width <= 0
            ? 0.5
            : Math.Clamp((double)(cursor.X - currentVisual.Left) / currentVisual.Width, 0.08, 0.92);
        var currentTopOffset = Math.Max(8, cursor.Y - currentVisual.Top);
        var restoredTopOffset = Math.Min(currentTopOffset, Math.Max(12, original.Height / 5));

        var left = (int)Math.Round(cursor.X - (original.Width * horizontalFraction));
        var top = cursor.Y - restoredTopOffset;

        if (original.Width <= workArea.Width)
        {
            left = Math.Clamp(left, workArea.Left, workArea.Right - original.Width);
        }
        if (original.Height <= workArea.Height)
        {
            top = Math.Clamp(top, workArea.Top, workArea.Bottom - original.Height);
        }

        return new RectI(left, top, original.Width, original.Height);
    }

    private static bool TryGetVisualRect(nint hwnd, out RectI rect)
    {
        var status = NativeMethods.DwmGetWindowAttribute(
            hwnd,
            NativeMethods.DwmwaExtendedFrameBounds,
            out NativeMethods.Rect nativeRect,
            Marshal.SizeOf<NativeMethods.Rect>());

        if (status == 0)
        {
            rect = RectI.FromEdges(nativeRect.Left, nativeRect.Top, nativeRect.Right, nativeRect.Bottom);
            return !rect.IsEmpty;
        }

        rect = default;
        return false;
    }

    private static bool ApproximatelyMatches(RectI a, RectI b, int tolerance) =>
        Math.Abs(a.Left - b.Left) <= tolerance &&
        Math.Abs(a.Top - b.Top) <= tolerance &&
        Math.Abs(a.Right - b.Right) <= tolerance &&
        Math.Abs(a.Bottom - b.Bottom) <= tolerance;

    private static RectI ToOuterRect(RectI visualRect, FrameInsets insets) =>
        RectI.FromEdges(
            visualRect.Left - insets.Left,
            visualRect.Top - insets.Top,
            visualRect.Right + insets.Right,
            visualRect.Bottom + insets.Bottom);

    private readonly record struct RestoreEntry(
        RectI OriginalVisualRect,
        RectI TargetVisualRect,
        RectI WorkArea,
        FrameInsets FrameInsets);
}
