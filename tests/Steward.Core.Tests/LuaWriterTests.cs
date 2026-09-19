namespace Steward.Core.Tests;

public sealed class LuaWriterTests
{
    private static LuaValue RoundTrip(LuaValue value) =>
        LuaSavedVariables.Parse("X = " + LuaWriter.Serialize(value))["X"];

    [Theory]
    [InlineData("plain")]
    [InlineData("with \"quotes\"")]
    [InlineData("back\\slash")]
    [InlineData("line\nbreak")]
    [InlineData("carriage\rreturn")]
    [InlineData("a\ttab")]
    [InlineData("pipe|colour code")]
    [InlineData("Grüg")]
    [InlineData("")]
    public void Serialize_RoundTripsStrings(string text)
    {
        var value = RoundTrip(LuaValue.FromString(text));

        Assert.Equal(text, value.Text);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(60d)]
    [InlineData(-12d)]
    [InlineData(0.25d)]
    [InlineData(-2.5d)]
    [InlineData(1758260000d)]
    public void Serialize_RoundTripsNumbers(double number)
    {
        var value = RoundTrip(LuaValue.FromNumber(number));

        Assert.Equal(number, value.Number);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Serialize_RoundTripsBooleans(bool boolean)
    {
        var value = RoundTrip(LuaValue.FromBoolean(boolean));

        Assert.Equal(boolean, value.Boolean);
    }

    [Fact]
    public void Serialize_RoundTripsNestedTablesAndArrays()
    {
        var table = LuaValue.FromTable(
            new LuaEntry(LuaValue.FromString("name"), LuaValue.FromString("Hoobi")),
            new LuaEntry(LuaValue.FromNumber(3), LuaValue.FromString("third")),
            new LuaEntry(LuaValue.FromString("items"), LuaValue.Array([LuaValue.FromString("a"), LuaValue.FromString("b")])));

        var value = RoundTrip(table);

        Assert.Equal("Hoobi", value.GetString("name"));
        Assert.Equal("third", value.Table.Single(e => e.Key?.Number == 3).Value.Text);
        Assert.Equal(["a", "b"], value.GetTable("items")!.Items.Select(i => i.Text));
    }

    [Fact]
    public void Serialize_PreservesKeyOrder()
    {
        var table = LuaValue.FromTable(
            new LuaEntry(LuaValue.FromString("b"), LuaValue.FromNumber(2)),
            new LuaEntry(LuaValue.FromString("a"), LuaValue.FromNumber(1)));

        var value = RoundTrip(table);

        Assert.Equal(["b", "a"], value.Table.Select(e => e.Key!.Text));
    }
}
