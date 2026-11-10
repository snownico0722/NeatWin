using NeatWin.Windows;

namespace NeatWin.App;

internal readonly record struct HotkeyBinding(
    int Key,
    bool Control,
    bool Alt,
    bool Shift,
    bool Win)
{
    internal static HotkeyBinding Default => new(0x54, Control: false, Alt: true, Shift: false, Win: true);

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

    internal static bool IsValidKey(int key) => key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A
        or >= 0x70 and <= 0x87 or 0x20 or 0x2D or 0x2E or 0x24 or 0x23 or 0x21 or 0x22;

    internal static string FormatKey(int key) => key switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x70 and <= 0x87 => "F" + (key - 0x6F),
        0x20 => "Space", 0x2D => "Insert", 0x2E => "Delete", 0x24 => "Home", 0x23 => "End",
        0x21 => "PageUp", 0x22 => "PageDown", _ => "0x" + key.ToString("X2"),
    };
}
