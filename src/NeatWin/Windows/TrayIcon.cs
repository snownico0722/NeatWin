using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace NeatWin.Windows;

/// <summary>Shell tray integration with a WPF menu. No WinForms runtime or hidden Form.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 105;
    private readonly MessageWindow _messages;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly ContextMenu _menu;
    private readonly Action _activate;
    private NotifyIconData _data;
    private bool _disposed;
    internal TrayIcon(Window owner, ContextMenu menu, Action activate)
    {
        _menu = menu; _activate = activate;
        _menu.Resources = owner.Resources;
        _menu.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        _menu.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        _messages = new MessageWindow("NeatWin.Tray", OnMessage, topLevel: true);
        _data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _messages.Handle, Id = 1,
            Flags = 1 | 2 | 4 | 0x80, Callback = CallbackMessage,
            Icon = LoadIcon(0, (nint)32512), Tip = "NeatWin", Info = string.Empty, InfoTitle = string.Empty,
        };
        Add();
        _menu.Closed += (_, _) => NativeMethods.PostMessage(_messages.Handle, 0, 0, 0);
    }
    private void Add()
    {
        _data.Flags = 1 | 2 | 4 | 0x80;
        if (!ShellNotifyIcon(0, ref _data))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法创建 NeatWin 托盘图标。");
        _data.Version = 4;
        _ = ShellNotifyIcon(4, ref _data);
    }
    internal string Text
    {
        set { _data.Tip = value.Length > 127 ? value[..127] : value; _data.Flags = 4; _ = ShellNotifyIcon(1, ref _data); }
    }
    internal void ShowError(string text)
    {
        _data.InfoTitle = "NeatWin"; _data.Info = text.Length > 255 ? text[..255] : text;
        _data.InfoFlags = 3; _data.Flags = 0x10;
        _ = ShellNotifyIcon(1, ref _data);
    }
    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (_disposed) return 0;
        if ((uint)message == _taskbarCreated) { Add(); handled = true; }
        if ((uint)message == CallbackMessage)
        {
            var notification = (uint)((long)lParam & 0xffff);
            if (notification is 0x400 or 0x401) _activate();
            else if (notification == 0x7B)
            {
                _ = SetForegroundWindow(_messages.Handle);
                _menu.Placement = PlacementMode.MousePoint;
                _menu.IsOpen = true;
            }
            handled = true;
        }
        return 0;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _menu.IsOpen = false;
        _ = ShellNotifyIcon(2, ref _data); _messages.Dispose();
        // LoadIcon returns a shared icon; it must not be destroyed.
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size; public nint Window; public uint Id, Flags, Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint action, ref NotifyIconData data);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")] private static extern nint LoadIcon(nint instance, nint name);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
}
