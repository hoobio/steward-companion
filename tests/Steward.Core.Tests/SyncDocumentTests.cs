namespace Steward.Core.Tests;

public sealed class SyncDocumentTests
{
    private static SyncPayload Parse(string payload, string signOut = "dropMe", string images = """{"guild":null,"people":{},"discord":{}}""") =>
        SyncDocument.Parse($$"""{"version":1,"signOut":"{{signOut}}","payload":{{payload}},"images":{{images}}}""", DateTimeOffset.UnixEpoch);

    private static LuaValue Value(SyncPayload payload, string key) =>
        Assert.Single(payload.Entries, entry => entry.Key?.Text == key).Value;

    private static LuaValue RenderedTable(SyncPayload payload)
    {
        var rendered = StewardSyncFile.Render(payload);
        return LuaSavedVariables.Parse("X = " + rendered["Steward.LoadSync(".Length..rendered.LastIndexOf(')')])["X"];
    }

    [Fact]
    public void SpecDps_ReachesTheRenderedTableWithServerKeysAndUnroundedNumbers()
    {
        var payload = Parse("""{"specDps":{"DRUID":{"Balance":850.64,"Feral":951.3647},"DEATHKNIGHT":{"Frost":1000}},"specDpsAt":1760000000,"guildId":"1"}""");
        var rendered = RenderedTable(payload);

        var druid = rendered.Table.Single(entry => entry.Key?.Text == "specDps").Value.Table.Single(entry => entry.Key?.Text == "DRUID").Value;
        Assert.Equal(850.64, druid.Table.Single(entry => entry.Key?.Text == "Balance").Value.Number);
        Assert.Equal(951.3647, druid.Table.Single(entry => entry.Key?.Text == "Feral").Value.Number);
        Assert.Contains(rendered.Table.Single(entry => entry.Key?.Text == "specDps").Value.Table, entry => entry.Key?.Text == "DEATHKNIGHT");
        Assert.Equal(1760000000d, rendered.Table.Single(entry => entry.Key?.Text == "specDpsAt").Value.Number);
    }

    [Fact]
    public void ADocumentWithoutSpecDps_RendersNoSpecDpsKeys()
    {
        var rendered = StewardSyncFile.Render(Parse("""{"guildId":"1"}"""));

        Assert.DoesNotContain("specDps", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void AnArray_KeepsANullElementAsAPositionalNil()
    {
        var items = Value(Parse("""{"a":[1,null,3,null]}"""), "a");

        Assert.All(items.Table, entry => Assert.Null(entry.Key));
        Assert.Equal([LuaKind.Number, LuaKind.Nil, LuaKind.Number, LuaKind.Nil], items.Table.Select(entry => entry.Value.Kind));
        Assert.Equal(3d, items.Table[2].Value.Number);
    }

    [Fact]
    public void AnObjectMemberThatIsNull_IsLeftOut()
    {
        var table = Value(Parse("""{"a":{"x":1,"y":null,"z":"s"}}"""), "a");

        Assert.Equal(["x", "z"], table.Table.Select(entry => entry.Key!.Text));
        Assert.DoesNotContain("nil", StewardSyncFile.Render(Parse("""{"a":{"y":null}}""")), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyArrayAndAnEmptyObject_BothBecomeAnEmptyTable()
    {
        var payload = Parse("""{"a":[],"o":{}}""");

        Assert.Equal(LuaKind.Table, Value(payload, "a").Kind);
        Assert.Empty(Value(payload, "a").Table);
        Assert.Equal(LuaKind.Table, Value(payload, "o").Kind);
        Assert.Empty(Value(payload, "o").Table);
    }

    [Fact]
    public void ObjectMembers_KeepDocumentOrder_AndScalarsKeepTheirType()
    {
        var payload = Parse("""{"z":"text","a":true,"m":false,"n":1760000000123,"f":0.5}""");

        Assert.Equal(["z", "a", "m", "n", "f"], payload.Entries.Select(entry => entry.Key!.Text));
        var rendered = StewardSyncFile.Render(payload);
        Assert.Contains("[\"a\"] = true,", rendered, StringComparison.Ordinal);
        Assert.Contains("[\"m\"] = false,", rendered, StringComparison.Ordinal);
        Assert.Contains("[\"n\"] = 1760000000123,", rendered, StringComparison.Ordinal);
        Assert.Contains("[\"f\"] = 0.5,", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ADigitStringKeyOrValue_StaysAString()
    {
        var table = Value(Parse("""{"links":{"300000000000000003":"400000000000000004"}}"""), "links");

        var entry = Assert.Single(table.Table);
        Assert.Equal(LuaKind.Text, entry.Key!.Kind);
        Assert.Equal("300000000000000003", entry.Key.Text);
        Assert.Equal(LuaKind.Text, entry.Value.Kind);
    }

    [Fact]
    public void TheIntWrapper_BecomesATableWithIntegerKeys()
    {
        var table = Value(Parse("""{"t":{"$int":{"123":"a","-4":{"x":1},"7":null}}}"""), "t");

        Assert.Equal([123d, -4d], table.Table.Select(entry => entry.Key!.Number));
        Assert.All(table.Table, entry => Assert.Equal(LuaKind.Number, entry.Key!.Kind));
        Assert.Contains("[123] = \"a\",", StewardSyncFile.Render(Parse("""{"t":{"$int":{"123":"a"}}}""")), StringComparison.Ordinal);
    }

    [Fact]
    public void AnIntKeyBesideOtherMembersOrWithAScalarValue_IsAnOrdinaryStringKey()
    {
        Assert.Equal(["$int", "b"], Value(Parse("""{"t":{"$int":{"1":2},"b":1}}"""), "t").Table.Select(entry => entry.Key!.Text));
        Assert.Equal("$int", Assert.Single(Value(Parse("""{"t":{"$int":"x"}}"""), "t").Table).Key!.Text);
    }

    [Theory]
    [InlineData("12a")]
    [InlineData("1.5")]
    [InlineData("")]
    [InlineData("+1")]
    [InlineData("-")]
    [InlineData(" 1")]
    [InlineData("9007199254740993")]
    [InlineData("99999999999999999999")]
    public void AnIntWrapperKeyThatIsNotABase10Integer_RejectsTheDocument(string key)
    {
        Assert.Throws<FormatException>(() => Parse("""{"t":{"$int":{""" + System.Text.Json.JsonSerializer.Serialize(key) + """:1}}}"""));
    }

    [Fact]
    public void AHostileStringAndKey_StayData()
    {
        const string hostile = "a\"]] = nil, evil = {\n\\\"x";
        var payload = Parse("""{"k\"]] = 1, [[\n\\":"v\"]]\n\\ } os.exit() --"}""");

        var table = RenderedTable(payload);

        Assert.Equal(["writtenAt", "companion", "k\"]] = 1, [[\n\\"], table.Table.Select(entry => entry.Key!.Text));
        Assert.Equal("v\"]]\n\\ } os.exit() --", table.Table[2].Value.Text);
        Assert.Equal(hostile, Value(Parse($$"""{"h":{{System.Text.Json.JsonSerializer.Serialize(hostile)}}}"""), "h").Text);
    }

    [Theory]
    [InlineData("""{"version":1,"signOut":"clear","payload":[]}""")]
    [InlineData("""{"version":1,"signOut":"clear","payload":"x"}""")]
    [InlineData("""{"version":1,"signOut":"clear","payload":null}""")]
    [InlineData("""{"version":1,"signOut":"clear"}""")]
    [InlineData("""[]""")]
    [InlineData("""not json""")]
    public void APayloadThatIsNotAnObject_IsRejected(string json)
    {
        Assert.Throws<FormatException>(() => SyncDocument.Parse(json, DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("""{"version":2,"signOut":"clear","payload":{}}""")]
    [InlineData("""{"version":"1","signOut":"clear","payload":{}}""")]
    [InlineData("""{"version":1.5,"signOut":"clear","payload":{}}""")]
    [InlineData("""{"signOut":"clear","payload":{}}""")]
    public void AVersionOtherThan1_IsRejected(string json)
    {
        Assert.Throws<FormatException>(() => SyncDocument.Parse(json, DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("""{"n":1e400}""")]
    [InlineData("""{"s":"\ud800"}""")]
    public void AValueTheConverterCannotWrite_RejectsTheDocument(string payload)
    {
        Assert.Throws<FormatException>(() => Parse(payload));
    }

    [Theory]
    [InlineData("dropMe", SyncSignOut.DropMe)]
    [InlineData("clear", SyncSignOut.Clear)]
    [InlineData("somethingNew", SyncSignOut.Clear)]
    public void SignOut_IsRead_AndAnUnknownValueClears(string value, SyncSignOut expected)
    {
        Assert.Equal(expected, Parse("{}", signOut: value).SignOut);
    }

    [Fact]
    public void Images_AreRead()
    {
        var images = Parse("{}", images: """{"guild":"https://g.example/i.png?size=64","people":{"1":"https://p.example/1.png?size=64"},"discord":{"2":"https://d.example/2.png?size=32","3":null}}""").Images;

        Assert.Equal("https://g.example/i.png?size=64", images.Guild);
        Assert.Equal("https://p.example/1.png?size=64", images.People["1"]);
        Assert.Equal(["2"], images.Discord.Keys);
    }

    [Fact]
    public void Directory_IsReadFromThePayload_WithAbsentKeysAsNull()
    {
        var directory = Parse("""
            {"members":[{},{}],"catalogue":{"Alchemy":[],"Cooking":[]},
             "directory":{"people":[{"id":"1","name":"Peer","mainGuid":"G1"},{"id":"2","name":"Out"},{"name":"no id"}],
             "characters":[{"guid":"G1","name":"Leafy","level":60,"classId":11,"linkedUserId":"1"},{"guid":"G2","name":"Twig","level":"x"}]}}
            """).Directory!;

        Assert.Equal([new DirectoryPerson("1", "Peer", "G1"), new DirectoryPerson("2", "Out", null)], directory.People);
        Assert.Equal([new DirectoryCharacter("G1", "Leafy", 60, 11, "1"), new DirectoryCharacter("G2", "Twig", 0, 0, null)], directory.Characters);
        Assert.Null(directory.Professions);
        Assert.Equal(2, directory.Members);
        Assert.Equal(2, directory.Catalogue);

        var empty = Parse("""{"me":{"id":"7"}}""").Directory!;
        Assert.Null(empty.People);
        Assert.Null(empty.Characters);
        Assert.Equal(0, empty.Members);
    }
}
