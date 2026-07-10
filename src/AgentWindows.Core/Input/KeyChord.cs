using System.Diagnostics.CodeAnalysis;
using AgentWindows.Core.Session;

namespace AgentWindows.Core.Input;

/// <summary>
/// A parsed key chord. Parsing validates syntax only (modifiers plus a non-empty
/// key token); which named keys exist is decided by the automation layer, the
/// single authority for the key vocabulary.
/// </summary>
public sealed record KeyChord
{
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

    private KeyChord(IReadOnlyList<KeyModifier> modifiers, string key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    public IReadOnlyList<KeyModifier> Modifiers { get; }

    /// <summary>The key token as written: a named key (e.g. "Enter") or a character.</summary>
    public string Key { get; }

    public static KeyChord Parse(string input) =>
        TryParse(input, out var chord)
            ? chord
            : throw new AutomationException(
                ErrorCodes.BadRequest,
                $"Cannot parse key chord '{input}'. Expected e.g. 'Enter', 'Ctrl+S', 'Ctrl+Shift+Tab'."
            );

    public static bool TryParse(string? input, [NotNullWhen(true)] out KeyChord? chord)
    {
        chord = null;
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

        var key = parts[^1];
        if (key.Length == 0)
        {
            return false;
        }

        chord = new KeyChord(modifiers, key);
        return true;
    }
}
