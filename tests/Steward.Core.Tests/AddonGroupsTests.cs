namespace Steward.Core.Tests;

public sealed class AddonGroupsTests
{
    private static readonly ManagedAddon Scripts = new("hoobiscripts", "HoobiScripts", "https://addon.hoobi.io/hoobiscripts/");

    private static readonly ManagedAddon ActionBars =
        new("hoobiscripts-actionbars", "HoobiScripts_ActionBars", "https://addon.hoobi.io/hoobiscripts-actionbars/") { Parent = "hoobiscripts" };

    private static readonly ManagedAddon Steward = new("steward", "Steward", "https://addon.hoobi.io/steward/");

    [Fact]
    public void FoldedInto_FoldsAChildWhoseParentIsVisible()
    {
        var folded = AddonGroups.FoldedInto([Steward, Scripts, ActionBars]);

        Assert.Equal("hoobiscripts", Assert.Single(folded, pair => pair.Key == "hoobiscripts-actionbars").Value);
        Assert.Single(folded);
    }

    [Fact]
    public void FoldedInto_LeavesAChildWhoseParentIsNotVisible()
    {
        Assert.Empty(AddonGroups.FoldedInto([Steward, ActionBars]));
    }

    [Fact]
    public void FoldedInto_NeverFoldsIntoAnotherChild()
    {
        var nested = new ManagedAddon("nested", "Nested", "https://addon.hoobi.io/nested/") { Parent = "hoobiscripts-actionbars" };

        Assert.DoesNotContain("nested", AddonGroups.FoldedInto([Scripts, ActionBars, nested]).Keys);
    }

    [Fact]
    public void FoldedInto_ToleratesDuplicateIds()
    {
        var folded = AddonGroups.FoldedInto([Scripts, Scripts with { Name = "Again" }, ActionBars, ActionBars]);

        Assert.Equal("hoobiscripts", Assert.Single(folded).Value);
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    public void IsRolledUp_SkipsAHiddenIgnoredOrUndistributableChild(bool distributable, bool hidden, bool ignored, bool expected)
    {
        Assert.Equal(expected, AddonGroups.IsRolledUp(distributable, hidden, ignored));
    }

    [Fact]
    public void CatalogueParent_MapsToTheManagedAddon()
    {
        var addon = new CatalogueAddon("hoobiscripts-actionbars", "HoobiScripts_ActionBars", "https://addon.hoobi.io/hoobiscripts-actionbars/", Parent: "hoobiscripts")
            .ToManagedAddon();

        Assert.Equal("hoobiscripts", addon.Parent);
        Assert.Null(new CatalogueAddon("steward", "Steward", "https://addon.hoobi.io/steward/").ToManagedAddon().Parent);
    }

    [Fact]
    public void StoredChannel_FollowsTheParentsChannel()
    {
        var channels = new Dictionary<string, string> { ["hoobiscripts"] = "release", ["hoobiscripts-actionbars"] = "pre-release" };

        Assert.Equal("release", AddonGroups.StoredChannel(channels, ActionBars with { Parent = "hoobiscripts" }));
        Assert.Equal("pre-release", AddonGroups.StoredChannel(new Dictionary<string, string> { ["hoobiscripts-actionbars"] = "pre-release" }, ActionBars with { Parent = "hoobiscripts" }));
        Assert.Equal("release", AddonGroups.StoredChannel(channels, Scripts));
        Assert.Null(AddonGroups.StoredChannel(channels, Steward));
    }

    [Theory]
    [InlineData(true, true, false, false, true)]
    [InlineData(false, true, true, true, true)]
    [InlineData(false, true, false, true, true)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, true, true, false, false)]
    public void NeedsUpdate_RollsUpTheGroup(bool parentUpdate, bool parentInstalled, bool childInstalled, bool childUpdate, bool expected)
    {
        Assert.Equal(expected, AddonGroups.NeedsUpdate(parentUpdate, parentInstalled, [(childInstalled, childUpdate)]));
    }

    [Fact]
    public void CombineChangelogs_AddsAHeadedSectionPerChildWithContent()
    {
        var parent = Release("1.2.0", ["Parent fix"]);
        var child = Release("0.3.0-pre-release.4", ["Bars fix"]);

        var combined = Changelogs.Combine(parent, [("Hoobi Scripts: ActionBars", child), ("Empty", Release("1.0.0", [])), ("None", null)]);

        Assert.Equal(["item", "heading", "item"], combined.Select(block => block.Kind));
        Assert.Equal("Parent fix", combined[0].Runs![0].Text);
        Assert.Equal("Hoobi Scripts: ActionBars 0.3.0-pre-release.4", combined[1].Runs![0].Text);
        Assert.Equal("Bars fix", combined[2].Runs![0].Text);
    }

    private static AddonRelease Release(string version, IReadOnlyList<string> notes) =>
        new(version, "addon.zip", null, 0, DateTimeOffset.UnixEpoch, notes);
}
