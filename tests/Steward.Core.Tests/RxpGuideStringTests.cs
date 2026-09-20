namespace Steward.Core.Tests;

public sealed class RxpGuideStringTests
{
    [Fact]
    public void Apply_ReplacesAnExistingAssignment()
    {
        const string existing = "RXPData = {\n}\nRXPString = \"old\"\nRXPSettings = {\n}\n";

        var updated = RxpGuideString.Apply(existing, "83|1084041902:payload%|40000");

        Assert.Equal(
            "RXPData = {\n}\nRXPString = \"83|1084041902:payload%|40000\"\nRXPSettings = {\n}\n",
            updated);
    }

    [Fact]
    public void Apply_AppendsWhenTheGlobalIsAbsent()
    {
        const string existing = "RXPData = {\n}\n";

        var updated = RxpGuideString.Apply(existing, "abc");

        Assert.Equal("RXPData = {\n}\nRXPString = \"abc\"\n", updated);
    }

    [Fact]
    public void Apply_AppendsANewlineFirstWhenTheFileDoesNotEndInOne()
    {
        var updated = RxpGuideString.Apply("RXPData = {}", "abc");

        Assert.Equal("RXPData = {}\nRXPString = \"abc\"\n", updated);
    }

    [Fact]
    public void Apply_LeavesOtherGlobalsByteIdentical()
    {
        const string existing = "RXPData = {\n\t[\"RXPString\"] = \"decoy\",\n}\nRXPString = nil\nRXPDB = {\n}\n";

        var updated = RxpGuideString.Apply(existing, "new");

        Assert.Equal("RXPData = {\n\t[\"RXPString\"] = \"decoy\",\n}\nRXPString = \"new\"\nRXPDB = {\n}\n", updated);
    }

    [Fact]
    public void Apply_PreservesCarriageReturns()
    {
        const string existing = "RXPString = nil\r\nRXPDB = {\r\n}\r\n";

        var updated = RxpGuideString.Apply(existing, "new");

        Assert.Equal("RXPString = \"new\"\r\nRXPDB = {\r\n}\r\n", updated);
    }

    [Fact]
    public void Apply_EscapesQuotesAndBackslashes()
    {
        var updated = RxpGuideString.Apply("RXPString = nil\n", "a\"b\\c\nd");

        Assert.Equal("RXPString = \"a\\\"b\\\\c\\nd\"\n", updated);
    }
}
