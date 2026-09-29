namespace Steward.Core;

public static class CurseForgeDefaultHandler
{
    public static bool IsOurs(string? progId, string ourAumid, Func<string, string?> lookupAumid) =>
        !string.IsNullOrEmpty(progId)
        && string.Equals(lookupAumid(progId), ourAumid, StringComparison.OrdinalIgnoreCase);
}
