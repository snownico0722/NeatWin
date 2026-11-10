using System.Runtime.InteropServices;
using NeatWin.Core;

namespace NeatWin.Windows;

internal static class DesktopDisplays
{
    internal static TaskDisplay[] Capture()
    {
        var displays = new List<TaskDisplay>();
        EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref NativeMethods.Rect bounds, nint data) =>
        {
            var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
            if (NativeMethods.GetMonitorInfo(monitor, ref info))
                displays.Add(new(monitor, Rect(info.Work), Rect(info.Monitor)));
            return true;
        }, 0);
        return displays.ToArray();
    }
    internal static RectI WorkArea(nint window)
    {
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        var monitor = NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest);
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return Rect(info.Work);
    }
    private static RectI Rect(NativeMethods.Rect r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    private delegate bool MonitorCallback(nint monitor, nint dc, ref NativeMethods.Rect rect, nint data);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
}
