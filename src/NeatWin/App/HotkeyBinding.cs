using NeatWin.Windows;

namespace NeatWin.App;

internal readonly record struct HotkeyBinding(
    Keys Key,
    bool Control,
    bool Alt,
    bool Shift,
    bool Win)
{
    internal static HotkeyBinding Default => new(Keys.T, Control: false, Alt: true, Shift: false, Win: true);

    internal bool HasModifier => Control || Alt || Shift || Win;

    internal uint NativeModifiers
    {
        get
        {
            uint result = NativeMethods.ModNoRepeat;
            if (Alt)
            {
                result |= NativeMethods.ModAlt;
            }

            if (Control)
            {
                result |= NativeMethods.ModControl;
            }

            if (Shift)
            {
                result |= NativeMethods.ModShift;
            }

            if (Win)
            {
                result |= NativeMethods.ModWin;
            }

            return result;
        }
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Control)
        {
            parts.Add("Ctrl");
        }

        if (Alt)
        {
            parts.Add("Alt");
        }

        if (Shift)
        {
            parts.Add("Shift");
        }

        if (Win)
        {
            parts.Add("Win");
        }

        parts.Add(FormatKey(Key));
        return string.Join(" + ", parts);
    }

    private static string FormatKey(Keys key)
    {
        if (key >= Keys.D0 && key <= Keys.D9)
        {
            return ((char)('0' + ((int)key - (int)Keys.D0))).ToString();
        }

        return key.ToString();
    }
}
