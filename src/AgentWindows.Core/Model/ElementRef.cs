namespace AgentWindows.Core.Model;

public static class ElementRef
{
    /// <summary>Normalizes "@e5" or "e5" to the bare id "e5".</summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var candidate = input.StartsWith('@') ? input[1..] : input;
        if (candidate.Length < 2 || candidate[0] != 'e')
        {
            return false;
        }

        for (var i = 1; i < candidate.Length; i++)
        {
            if (!char.IsAsciiDigit(candidate[i]))
            {
                return false;
            }
        }

        normalized = candidate;
        return true;
    }

    public static string Display(string bareRef) => $"@{bareRef}";

    /// <summary>Extracts the numeric index from a bare ref ("e5" -> 5).</summary>
    public static bool TryGetIndex(string bareRef, out int index)
    {
        index = 0;
        return bareRef is { Length: > 1 }
            && bareRef[0] == 'e'
            && int.TryParse(
                bareRef.AsSpan(1),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out index
            );
    }
}
