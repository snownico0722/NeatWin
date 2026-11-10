using System.Windows.Interop;

namespace NeatWin.Windows;

/// <summary>A native message endpoint owned by the current WPF dispatcher.</summary>
internal sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;
    internal nint Handle => _source.Handle;
    internal MessageWindow(string name, HwndSourceHook hook, bool topLevel = false)
    {
        _source = new HwndSource(new HwndSourceParameters(name)
        {
            ParentWindow = topLevel ? 0 : NativeMethods.HwndMessage,
            WindowStyle = 0, ExtendedWindowStyle = 0x80,
            Width = 0, Height = 0,
        });
        _source.AddHook(hook);
        AutomationGuard.MarkUtility(_source.Handle);
    }
    public void Dispose() => _source.Dispose();
}
