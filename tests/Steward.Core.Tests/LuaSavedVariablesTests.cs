namespace Steward.Core.Tests;

public sealed class LuaSavedVariablesTests
{
    private const string LiveSample = """
        HoobiScriptsDB = {
        ["settings"] = {
        ["autoAcceptQuests"] = true,
        ["vendorDowngradeQuality"] = 2,
        ["primaryStat"] = "auto",
        },
        ["specs"] = {
        "WARRIOR 1491 Warrior role=DAMAGER stat=INT(4)",
        },
        ["xp"] = {
        {
        ["at"] = 1758260000,
        }, -- [1]
        },
        }
        """;

    [Fact]
    public void Parse_ReadsTheLiveFormat()
    {
        var globals = LuaSavedVariables.Parse(LiveSample);
        var db = globals["HoobiScriptsDB"];

        var settings = db.GetTable("settings");
        Assert.NotNull(settings);
        Assert.True(settings.Get("autoAcceptQuests")!.Boolean);
        Assert.Equal(2d, settings.GetNumber("vendorDowngradeQuality")!.Value);
        Assert.Equal("auto", settings.GetString("primaryStat"));

        var spec = Assert.Single(db.GetTable("specs")!.Items);
        Assert.Equal("WARRIOR 1491 Warrior role=DAMAGER stat=INT(4)", spec.Text);

        var xpEntry = Assert.Single(db.GetTable("xp")!.Items);
        Assert.Equal(1758260000d, xpEntry.GetNumber("at")!.Value);
    }

    [Theory]
    [InlineData("\\\"", "\"")]
    [InlineData("\\\\", "\\")]
    [InlineData("\\n", "\n")]
    [InlineData("\\r", "\r")]
    [InlineData("\\t", "\t")]
    [InlineData("\\|", "|")]
    [InlineData("\\124", "|")]
    [InlineData("a\\110b", "anb")]
    public void Parse_UnescapesStringValues(string escaped, string expected)
    {
        var db = LuaSavedVariables.Parse($"Db = {{ [\"v\"] = \"{escaped}\", }}")["Db"];

        Assert.Equal(expected, db.GetString("v"));
    }

    [Fact]
    public void Parse_ReadsNestedArraysWithIndexComments()
    {
        const string text = """
            Db = {
            ["rows"] = {
            { "a", "b", }, -- [1]
            { "c", }, -- [2]
            },
            }
            """;

        var rows = LuaSavedVariables.Parse(text)["Db"].GetTable("rows")!.Items;

        Assert.Equal(2, rows.Count);
        Assert.Equal("a,b", string.Join(',', rows[0].Items.Select(item => item.Text)));
        Assert.Equal("c", string.Join(',', rows[1].Items.Select(item => item.Text)));
    }

    [Fact]
    public void Parse_ReadsNegativeAndExponentNumbers()
    {
        var db = LuaSavedVariables.Parse("""Db = { ["a"] = -12, ["b"] = 1.5e3, ["c"] = -2.5E-2, ["d"] = 0.25 }""")["Db"];

        Assert.Equal(-12d, db.GetNumber("a")!.Value);
        Assert.Equal(1500d, db.GetNumber("b")!.Value);
        Assert.Equal(-0.025d, db.GetNumber("c")!.Value);
        Assert.Equal(0.25d, db.GetNumber("d")!.Value);
    }

    [Fact]
    public void Parse_ReadsMultipleGlobals()
    {
        var globals = LuaSavedVariables.Parse("""
            StewardDB = { ["a"] = 1, }
            StewardCharDB = { ["b"] = 2, }
            """);

        Assert.Equal(2, globals.Count);
        Assert.Equal(1d, globals["StewardDB"].GetNumber("a")!.Value);
        Assert.Equal(2d, globals["StewardCharDB"].GetNumber("b")!.Value);
    }

    [Fact]
    public void Parse_AcceptsBareIdentifierKeysNumericKeysAndNil()
    {
        var db = LuaSavedVariables.Parse("""Db = { name = "Hoobi", [3] = "third", ["gone"] = nil }""")["Db"];

        Assert.Equal("Hoobi", db.GetString("name"));
        Assert.Equal(LuaKind.Nil, db.Get("gone")!.Kind);
        Assert.Equal(3d, db.Table[1].Key!.Number);
        Assert.Empty(db.Items);
    }

    [Fact]
    public void Parse_AcceptsAnAbsentTrailingComma()
    {
        var items = LuaSavedVariables.Parse("""Db = { "a", "b" }""")["Db"].Items;

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void Parse_ThrowsFormatExceptionNamingTheLine()
    {
        var error = Assert.Throws<FormatException>(() => LuaSavedVariables.Parse("""
            Db = {
            ["a"] = 1,
            ["b"] ! 2,
            }
            """));

        Assert.Contains("line 3", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ThrowsFormatException_OnAnUnterminatedTable()
    {
        Assert.Throws<FormatException>(() => LuaSavedVariables.Parse("""Db = { ["a"] = 1,"""));
    }
}
