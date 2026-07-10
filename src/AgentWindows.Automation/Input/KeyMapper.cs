using AgentWindows.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace AgentWindows.Automation.Input;

public static class KeyMapper
{
    private static readonly Dictionary<string, VirtualKeyShort> _namedKeys = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["Enter"] = VirtualKeyShort.RETURN,
        ["Return"] = VirtualKeyShort.RETURN,
        ["Esc"] = VirtualKeyShort.ESCAPE,
        ["Del"] = VirtualKeyShort.DELETE,
        ["PgUp"] = VirtualKeyShort.PRIOR,
        ["PgDn"] = VirtualKeyShort.NEXT,
        ["Tab"] = VirtualKeyShort.TAB,
        ["Escape"] = VirtualKeyShort.ESCAPE,
        ["Space"] = VirtualKeyShort.SPACE,
        ["Backspace"] = VirtualKeyShort.BACK,
        ["Delete"] = VirtualKeyShort.DELETE,
        ["Insert"] = VirtualKeyShort.INSERT,
        ["Home"] = VirtualKeyShort.HOME,
        ["End"] = VirtualKeyShort.END,
        ["PageUp"] = VirtualKeyShort.PRIOR,
        ["PageDown"] = VirtualKeyShort.NEXT,
        ["Up"] = VirtualKeyShort.UP,
        ["Down"] = VirtualKeyShort.DOWN,
        ["Left"] = VirtualKeyShort.LEFT,
        ["Right"] = VirtualKeyShort.RIGHT,
        ["F1"] = VirtualKeyShort.F1,
        ["F2"] = VirtualKeyShort.F2,
        ["F3"] = VirtualKeyShort.F3,
        ["F4"] = VirtualKeyShort.F4,
        ["F5"] = VirtualKeyShort.F5,
        ["F6"] = VirtualKeyShort.F6,
        ["F7"] = VirtualKeyShort.F7,
        ["F8"] = VirtualKeyShort.F8,
        ["F9"] = VirtualKeyShort.F9,
        ["F10"] = VirtualKeyShort.F10,
        ["F11"] = VirtualKeyShort.F11,
        ["F12"] = VirtualKeyShort.F12,
    };

    public static VirtualKeyShort ToVirtualKey(KeyModifier modifier) =>
        modifier switch
        {
            KeyModifier.Ctrl => VirtualKeyShort.CONTROL,
            KeyModifier.Shift => VirtualKeyShort.SHIFT,
            KeyModifier.Alt => VirtualKeyShort.ALT,
            KeyModifier.Win => VirtualKeyShort.LWIN,
            _ => throw new ArgumentOutOfRangeException(nameof(modifier)),
        };

    /// <summary>Maps a canonical gesture key (named key or single character) to a virtual key.</summary>
    public static bool TryMapKey(string key, out VirtualKeyShort virtualKey)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_namedKeys.TryGetValue(key, out virtualKey))
        {
            return true;
        }

        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z')
            {
                virtualKey = (VirtualKeyShort)((int)VirtualKeyShort.KEY_A + (c - 'A'));
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                virtualKey = (VirtualKeyShort)((int)VirtualKeyShort.KEY_0 + (c - '0'));
                return true;
            }
        }

        virtualKey = default;
        return false;
    }
}
