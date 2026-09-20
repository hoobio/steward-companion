using System.Text.RegularExpressions;

namespace Steward.Core;

public static partial class RxpGuideString
{
    public const string AddonName = "RXPGuides";
    public const string GlobalName = "RXPString";

    [GeneratedRegex(@"^RXPString = [^\r\n]*", RegexOptions.Multiline)]
    private static partial Regex Assignment();

    public static string Apply(string existingFile, string guideString)
    {
        ArgumentNullException.ThrowIfNull(existingFile);

        var statement = $"{GlobalName} = {LuaWriter.SerializeString(guideString)}";
        var match = Assignment().Match(existingFile);
        if (match.Success)
        {
            return string.Concat(existingFile.AsSpan(0, match.Index), statement, existingFile.AsSpan(match.Index + match.Length));
        }

        var separator = existingFile.Length == 0 || existingFile.EndsWith('\n') ? string.Empty : "\n";
        return existingFile + separator + statement + "\n";
    }

    public static IReadOnlyList<string> FindFiles(string flavourPath)
    {
        var accountRoot = Path.Combine(flavourPath, "WTF", "Account");
        if (!Directory.Exists(accountRoot))
        {
            return [];
        }

        var files = new List<string>();
        foreach (var accountPath in Directory.EnumerateDirectories(accountRoot))
        {
            var path = Path.Combine(accountPath, "SavedVariables", $"{AddonName}.lua");
            if (File.Exists(path))
            {
                files.Add(path);
            }
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    public static int Write(string flavourPath, string guideString)
    {
        var files = FindFiles(flavourPath);
        foreach (var path in files)
        {
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, Apply(File.ReadAllText(path), guideString));
            File.Move(temporaryPath, path, overwrite: true);
        }

        return files.Count;
    }
}
