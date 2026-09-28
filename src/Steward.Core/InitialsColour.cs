namespace Steward.Core;

public static class InitialsColour
{
    public static int IndexFor(string folderName, int paletteSize)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(paletteSize);

        var hash = 2166136261u;
        foreach (var c in folderName.ToLowerInvariant())
        {
            hash = unchecked((hash ^ c) * 16777619u);
        }

        return (int)(hash % (uint)paletteSize);
    }
}
