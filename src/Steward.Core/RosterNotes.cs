namespace Steward.Core;

public static class RosterNotes
{
    public static string? Trim(string? notes)
    {
        if (notes is null)
        {
            return null;
        }

        var kept = notes.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Where(line => !IsYOrN(line));

        return string.Join('\n', kept).Trim('\n');
    }

    private static bool IsYOrN(string line) =>
        string.Equals(line.Trim(), "Y", StringComparison.OrdinalIgnoreCase)
        || string.Equals(line.Trim(), "N", StringComparison.OrdinalIgnoreCase);
}
