using System.Diagnostics.CodeAnalysis;
using AgentWindows.Core.Session;

namespace AgentWindows.Core.Input;

public sealed record KeyGesture
{
    private static readonly HashSet<string> _namedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Enter",
        "Tab",
        "Escape",
        "Space",
        "Backspace",
        "Delete",
        "Insert",
        "Home",
        "End",
        "PageUp",
        "PageDown",
        "Up",
        "Down",
        "Left",
        "Right",
        "F1",
        "F2",
        "F3",
        "F4",
        "F5",
        "F6",
        "F7",
        "F8",
        "F9",
        "F10",
        "F11",
        "F12",
    };

    private static readonly Dictionary<string, string> _keyAliases = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["Esc"] = "Escape",
        ["Return"] = "Enter",
        ["Del"] = "Delete",
        ["PgUp"] = "PageUp",
        ["PgDn"] = "PageDown",
    };

    private static readonly Dictionary<string, KeyModifier> _modifierNames = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["Ctrl"] = KeyModifier.Ctrl,
        ["Control"] = KeyModifier.Ctrl,
        ["Shift"] = KeyModifier.Shift,
        ["Alt"] = KeyModifier.Alt,
        ["Win"] = KeyModifier.Win,
        ["Windows"] = KeyModifier.Win,
        ["Meta"] = KeyModifier.Win,
    };

    private KeyGesture(IReadOnlyList<KeyModifier> modifiers, string key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    public IReadOnlyList<KeyModifier> Modifiers { get; }

    /// <summary>Canonical key: a named key (e.g. "Enter", "F5") or a single character.</summary>
    public string Key { get; }

    public static KeyGesture Parse(string input) =>
        TryParse(input, out var gesture)
            ? gesture
            : throw new AutomationException(
                ErrorCodes.BadRequest,
                $"Cannot parse key gesture '{input}'. Expected e.g. 'Enter', 'Ctrl+S', 'Ctrl+Shift+Tab'."
            );

    public static bool TryParse(string? input, [NotNullWhen(true)] out KeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var parts = input.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = new List<KeyModifier>();
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!_modifierNames.TryGetValue(parts[i], out var modifier))
            {
                return false;
            }

            if (!modifiers.Contains(modifier))
            {
                modifiers.Add(modifier);
            }
        }

        var keyPart = parts[^1];
        if (keyPart.Length == 0)
        {
            return false;
        }

        string key;
        if (keyPart.Length == 1)
        {
            key = keyPart;
        }
        else
        {
            if (_keyAliases.TryGetValue(keyPart, out var alias))
            {
                keyPart = alias;
            }

            if (!_namedKeys.TryGetValue(keyPart, out var canonical))
            {
                return false;
            }

            key = canonical;
        }

        gesture = new KeyGesture(modifiers, key);
        return true;
    }
}
