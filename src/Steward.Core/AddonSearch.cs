namespace Steward.Core;

public static class AddonSearch
{
    public static bool Matches(string name, string query)
    {
        for (var index = name.IndexOf(query, StringComparison.OrdinalIgnoreCase); index >= 0;
             index = name.IndexOf(query, index + 1, StringComparison.OrdinalIgnoreCase))
        {
            if (IsWordStart(name, index))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWordStart(string name, int index)
    {
        if (index == 0)
        {
            return true;
        }

        var previous = name[index - 1];
        var current = name[index];
        return !char.IsLetterOrDigit(previous)
            || (char.IsLower(previous) && char.IsUpper(current))
            || (char.IsLetter(previous) != char.IsLetter(current));
    }
}
