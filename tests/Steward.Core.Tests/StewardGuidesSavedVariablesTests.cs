namespace Steward.Core.Tests;

public sealed class StewardGuidesSavedVariablesTests : IDisposable
{
    private const string Marked = """
        StewardGuidesDB = {
        ["generation"] = 1758380000000,
        ["imported"] = {
        ["1084041902"] = true,
        ["2792083552"] = false,
        },
        }
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("steward-guides-sv-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string WriteAccountFile(string account, string fileName, string content)
    {
        var folder = Path.Combine(_root, "WTF", "Account", account, "SavedVariables");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, fileName), content);
        return folder;
    }

    [Fact]
    public void Read_TakesTheGenerationAndTheHashesMarkedTrue()
    {
        WriteAccountFile("12345#1", StewardGuidesSavedVariables.FileName, Marked);

        var marks = Assert.Single(StewardGuidesSavedVariables.Read(_root));

        Assert.Equal(1758380000000, marks.Generation);
        Assert.Equal(["1084041902"], marks.Imported);
    }

    [Fact]
    public void Read_IsEmpty_WhenTheFileIsMissing()
    {
        WriteAccountFile("12345#1", "Other.lua", Marked);

        Assert.Empty(StewardGuidesSavedVariables.Read(_root));
        Assert.Empty(StewardGuidesSavedVariables.Read(Path.Combine(_root, "nowhere")));
    }

    [Fact]
    public void Read_GivesNoMarks_ForAMalformedFile()
    {
        WriteAccountFile("12345#1", StewardGuidesSavedVariables.FileName, "StewardGuidesDB = { [\"imported\"] = {");

        var marks = Assert.Single(StewardGuidesSavedVariables.Read(_root));

        Assert.Null(marks.Generation);
        Assert.Empty(marks.Imported);
    }

    [Fact]
    public void Read_IgnoresTheBackupFile()
    {
        WriteAccountFile("12345#1", StewardGuidesSavedVariables.FileName + ".bak", Marked);

        Assert.Empty(StewardGuidesSavedVariables.Read(_root));
    }
}
