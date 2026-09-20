namespace Steward.Core;

public sealed record StewardGuidesMarks(long? Generation, IReadOnlyCollection<string> Imported);

public static class StewardGuidesSavedVariables
{
    public const string FileName = "StewardGuides.lua";

    private const string Global = "StewardGuidesDB";

    public static IReadOnlyList<StewardGuidesMarks> Read(string flavourPath)
    {
        var accountRoot = Path.Combine(flavourPath, "WTF", "Account");
        if (!Directory.Exists(accountRoot))
        {
            return [];
        }

        var marks = new List<StewardGuidesMarks>();
        foreach (var accountPath in Directory.EnumerateDirectories(accountRoot))
        {
            var path = Path.Combine(accountPath, "SavedVariables", FileName);
            if (File.Exists(path))
            {
                marks.Add(ReadFile(path));
            }
        }

        return marks;
    }

    private static StewardGuidesMarks ReadFile(string path)
    {
        try
        {
            var root = LuaSavedVariables.Parse(File.ReadAllText(path)).GetValueOrDefault(Global);
            var generation = root?.GetNumber("generation");
            return new StewardGuidesMarks(generation is null ? null : (long)generation.Value, Imported(root));
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            return new StewardGuidesMarks(null, []);
        }
    }

    private static IReadOnlyCollection<string> Imported(LuaValue? root) =>
    [
        .. (root?.GetTable("imported")?.Table ?? [])
            .Where(entry => entry.Key is { Kind: LuaKind.Text } && entry.Value is { Kind: LuaKind.Boolean, Boolean: true })
            .Select(entry => entry.Key!.Text!),
    ];
}
