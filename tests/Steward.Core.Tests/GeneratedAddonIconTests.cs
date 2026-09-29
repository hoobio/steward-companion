using System.Text;

namespace Steward.Core.Tests;

public sealed class GeneratedAddonIconTests : IDisposable
{
    private const string Directive = @"## IconTexture: Interface\AddOns\Foo\StewardIcon";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"steward-icon-{Guid.NewGuid():N}");

    private readonly string _addOns;

    public GeneratedAddonIconTests() => _addOns = Path.Combine(_root, "Interface", "AddOns");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Folder(string name, params (string File, string Content)[] files)
    {
        var path = Path.Combine(_addOns, name);
        Directory.CreateDirectory(path);
        foreach (var (file, content) in files)
        {
            File.WriteAllText(Path.Combine(path, file), content);
        }

        return path;
    }

    private static string Insert(string toc) =>
        Encoding.UTF8.GetString(GeneratedAddonIcon.InsertDirective(Encoding.UTF8.GetBytes(toc), Directive));

    [Fact]
    public void InsertDirective_GoesAfterTheLastHeaderLineWithLf()
    {
        Assert.Equal(
            $"## Interface: 11508\n## Title: Foo\n{Directive}\n\nFoo.lua\n",
            Insert("## Interface: 11508\n## Title: Foo\n\nFoo.lua\n"));
    }

    [Fact]
    public void InsertDirective_KeepsCrlf()
    {
        Assert.Equal(
            $"## Title: Foo\r\n{Directive}\r\nFoo.lua\r\n",
            Insert("## Title: Foo\r\nFoo.lua\r\n"));
    }

    [Fact]
    public void InsertDirective_TerminatesAnUnterminatedLastHeader()
    {
        Assert.Equal($"## Title: Foo\n{Directive}", Insert("## Title: Foo"));
    }

    [Fact]
    public void InsertDirective_KeepsTheBomAndNonUtf8Bytes()
    {
        byte[] toc = [0xEF, 0xBB, 0xBF, .. "## Title: F"u8, 0xE9, .. "\r\nFoo.lua\r\n"u8];

        var result = GeneratedAddonIcon.InsertDirective(toc, Directive);

        Assert.Equal([0xEF, 0xBB, 0xBF, .. "## Title: F"u8, 0xE9, .. "\r\n"u8, .. Encoding.UTF8.GetBytes(Directive), .. "\r\nFoo.lua\r\n"u8], result);
    }

    [Fact]
    public void Plan_EditsEveryTopLevelTocIncludingFlavourSuffixed()
    {
        Folder("Foo", ("Foo.toc", "## Title: Foo\n"), ("Foo_Vanilla.toc", "## Title: Foo\n"), ("Other.toc", "## Title: Other\n"));

        var plan = GeneratedAddonIcon.Plan(_addOns, "Foo")!;

        Assert.True(plan.WriteTexture);
        Assert.Equal(["Foo.toc", "Foo_Vanilla.toc"], plan.TocsToEdit.Select(Path.GetFileName));
    }

    [Fact]
    public void Plan_TouchesNothingWhenAnyTocCarriesItsOwnIcon()
    {
        Folder("Foo", ("Foo.toc", "## Title: Foo\n"), ("Foo_Vanilla.toc", "## IconTexture: Interface\\AddOns\\Foo\\Icon\n"));

        Assert.Null(GeneratedAddonIcon.Plan(_addOns, "Foo"));
    }

    [Fact]
    public void Plan_IsNullForAFolderWithoutATopLevelToc()
    {
        Folder("Foo", ("Other.toc", "## Title: Other\n"));

        Assert.Null(GeneratedAddonIcon.Plan(_addOns, "Foo"));
        Assert.Null(GeneratedAddonIcon.Plan(_addOns, "Missing"));
    }

    [Fact]
    public void Apply_WritesTextureAndTocsAndIsIdempotent()
    {
        var folder = Folder("Foo", ("Foo.toc", "## Title: Foo\r\nFoo.lua\r\n"), ("Foo_Vanilla.toc", "## Title: Foo\nFoo.lua\n"));

        GeneratedAddonIcon.Apply(_addOns, "Foo", GeneratedAddonIcon.Plan(_addOns, "Foo")!, [1, 2, 3]);

        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(folder, GeneratedAddonIcon.FileName)));
        Assert.Equal($"## Title: Foo\r\n{Directive}\r\nFoo.lua\r\n", File.ReadAllText(Path.Combine(folder, "Foo.toc")));
        Assert.Equal($"## Title: Foo\n{Directive}\nFoo.lua\n", File.ReadAllText(Path.Combine(folder, "Foo_Vanilla.toc")));
        Assert.Null(GeneratedAddonIcon.Plan(_addOns, "Foo"));
    }

    [Fact]
    public void Plan_RegeneratesOnlyAMissingTexture()
    {
        Folder("Foo", ("Foo.toc", $"## Title: Foo\n{Directive}\n"));

        var plan = GeneratedAddonIcon.Plan(_addOns, "Foo")!;

        Assert.True(plan.WriteTexture);
        Assert.Empty(plan.TocsToEdit);
    }

    [Fact]
    public void Apply_RefusesAWtfPath()
    {
        var wtf = Path.Combine(Path.GetDirectoryName(_addOns)!, "WTF");
        Directory.CreateDirectory(Path.Combine(wtf, "Foo"));

        Assert.Throws<InvalidOperationException>(() => GeneratedAddonIcon.Apply(wtf, "Foo", new AddonIconPlan(true, []), [1]));
        Assert.False(File.Exists(Path.Combine(wtf, "Foo", GeneratedAddonIcon.FileName)));
    }
}
