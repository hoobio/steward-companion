using System.Text.Json;

namespace Steward.Core.Tests;

public sealed class AddonRequirementsTests
{
    private static AddonRelease Release(string version, params AddonRequirement[] requires) =>
        new(version, "a.zip", "ab", 1, DateTimeOffset.UnixEpoch, Requires: requires);

    private static ManagedAddon Addon(string id) => new(id, id, $"https://example.test/{id}/", Name: id.ToUpperInvariant());

    private sealed class World
    {
        public Dictionary<string, AddonRelease?> Releases { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Present { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Failing { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Installed { get; } = [];

        public List<string> Statuses { get; } = [];

        public AddonRequirementHooks Hooks => new(
            requirement => requirement.Id == "unknown" ? null : Addon(requirement.Id),
            (addon, _) => Present.Contains(addon.Id),
            (addon, _) => Task.FromResult(Releases.GetValueOrDefault(addon.Id) is { } release ? ("release", release) : ((string, AddonRelease)?)null),
            (addon, _, _, _) => Install(addon),
            Statuses.Add);

        private Task Install(ManagedAddon addon)
        {
            if (Failing.Contains(addon.Id))
            {
                throw new InvalidOperationException("disk full");
            }

            Installed.Add(addon.Id);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Installs_requirements_depth_first_before_the_dependant()
    {
        var world = new World();
        world.Releases["a"] = Release("1", new AddonRequirement("b"));
        world.Releases["b"] = Release("1");
        world.Releases["c"] = Release("1");

        await AddonRequirements.InstallAsync(Addon("top"), Release("1", new AddonRequirement("a"), new AddonRequirement("c")), world.Hooks, CancellationToken.None);

        Assert.Equal(["b", "a", "c"], world.Installed);
        Assert.Equal(["installing B (needed by A)", "installing A (needed by TOP)", "installing C (needed by TOP)"], world.Statuses);
    }

    [Fact]
    public async Task Skips_installed_or_ignored_requirements_and_cycles()
    {
        var world = new World();
        world.Present.Add("a");
        world.Releases["b"] = Release("1", new AddonRequirement("top"), new AddonRequirement("b"));

        await AddonRequirements.InstallAsync(Addon("top"), Release("1", new AddonRequirement("a"), new AddonRequirement("b"), new AddonRequirement("b")), world.Hooks, CancellationToken.None);

        Assert.Equal(["b"], world.Installed);
    }

    [Fact]
    public async Task Stops_at_the_maximum_depth()
    {
        var world = new World();
        world.Releases["d1"] = Release("1", new AddonRequirement("d2"));
        world.Releases["d2"] = Release("1", new AddonRequirement("d3"));
        world.Releases["d3"] = Release("1", new AddonRequirement("d4"));
        world.Releases["d4"] = Release("1");

        await AddonRequirements.InstallAsync(Addon("top"), Release("1", new AddonRequirement("d1")), world.Hooks, CancellationToken.None);

        Assert.Equal(["d3", "d2", "d1"], world.Installed);
    }

    [Fact]
    public async Task A_failed_requirement_names_itself_and_the_dependant()
    {
        var world = new World();
        world.Releases["a"] = Release("1");
        world.Failing.Add("a");

        var error = await Assert.ThrowsAsync<AddonRequirementException>(() =>
            AddonRequirements.InstallAsync(Addon("top"), Release("1", new AddonRequirement("a")), world.Hooks, CancellationToken.None));

        Assert.Equal("Could not install A (needed by TOP): disk full", error.Message);
    }

    [Fact]
    public async Task A_requirement_with_no_build_or_no_source_fails()
    {
        var world = new World();

        var missing = await Assert.ThrowsAsync<AddonRequirementException>(() =>
            AddonRequirements.InstallAsync(Addon("top"), Release("1", new AddonRequirement("a")), world.Hooks, CancellationToken.None));
        var unknown = await Assert.ThrowsAsync<AddonRequirementException>(() =>
            AddonRequirements.InstallAsync(Addon("top"), Release("1", new AddonRequirement("unknown", "Mystery")), world.Hooks, CancellationToken.None));

        Assert.StartsWith("Could not install A (needed by TOP): no build", missing.Message, StringComparison.Ordinal);
        Assert.Equal("Mystery (needed by TOP) is not available to install", unknown.Message);
    }

    [Fact]
    public async Task Catalogue_requires_install_alongside_the_manifest_ones()
    {
        var world = new World();
        world.Releases["a"] = Release("1");
        world.Releases["b"] = Release("1");

        await AddonRequirements.InstallAsync(Addon("top") with { Requires = [new AddonRequirement("b")] }, Release("1", new AddonRequirement("a")), world.Hooks, CancellationToken.None);

        Assert.Equal(["a", "b"], world.Installed);
    }

    [Fact]
    public void Parses_server_objects_and_catalogue_ids()
    {
        const string json = """
            {"version":"1","zip":"a.zip","sha1":"ab","size":1,"released":"2026-10-06T00:00:00Z","requires":[
              {"id":"curseforge:334372","name":"Questie","folder_name":"Questie","manifest_base_url":"https://api.hoobi.io/guild/api/addons/curseforge/334372/88568/",
               "requires":[{"id":"curseforge:1","name":null,"folder_name":null,"manifest_base_url":"https://x/","requires":[]}]},
              "steward-auctioning", 5, {"name":"no id"}]}
            """;

        var release = JsonSerializer.Deserialize(json, CompanionJsonContext.Default.AddonRelease)!;
        var requires = AddonRequirements.Of(new ManagedAddon("x", "x"), release);

        Assert.Equal(["curseforge:334372", "steward-auctioning"], requires.Select(requirement => requirement.Id));
        Assert.Equal("Questie", requires[0].FolderName);
        Assert.Equal("https://api.hoobi.io/guild/api/addons/curseforge/334372/88568/", requires[0].ManifestBaseUrl);
        Assert.Equal("curseforge:1", Assert.Single(requires[0].Requires!).Id);
    }

    [Fact]
    public void A_catalogue_row_round_trips_its_requires()
    {
        IReadOnlyList<CatalogueAddon> catalogue = [new("a", "A", "https://x/", Requires: [new AddonRequirement("b"), new AddonRequirement("curseforge:2", "Two", "Two", "https://y/")])];

        var json = JsonSerializer.Serialize(catalogue, CompanionJsonContext.Default.IReadOnlyListCatalogueAddon);
        var back = JsonSerializer.Deserialize(json, CompanionJsonContext.Default.IReadOnlyListCatalogueAddon)!;

        Assert.Contains("\"requires\":[\"b\",{\"id\":\"curseforge:2\"", json, StringComparison.Ordinal);
        Assert.True(AddonCatalogue.Same(catalogue, back));
        Assert.Equal(["b", "curseforge:2"], back[0].ToManagedAddon().Requires!.Select(requirement => requirement.Id));
        Assert.DoesNotContain("requires", JsonSerializer.Serialize<IReadOnlyList<CatalogueAddon>>([new("a", "A", "https://x/")], CompanionJsonContext.Default.IReadOnlyListCatalogueAddon), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("curseforge-334372-88568", "curseforge:334372", true)]
    [InlineData("curseforge-3343720-88568", "curseforge:334372", false)]
    [InlineData("steward-auctioning", "Steward-Auctioning", true)]
    [InlineData("hoobiscripts", "curseforge:1", false)]
    public void Matches_rows_to_requirements(string addonId, string requirementId, bool expected) =>
        Assert.Equal(expected, AddonRequirements.Matches(addonId, requirementId));
}
