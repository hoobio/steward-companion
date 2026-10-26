namespace Steward.Core.Tests;

public sealed class TukUiAddonsTests : IDisposable
{
    private const string StewardIcon = "https://api.hoobi.io/guild/api/addons/tukui/-2/icon.png";
    private const string Logo = "https://storage.tukui.org/ElvUI-Logo-Square.png";

    private static readonly TukUiAddon ElvUI = new(-2, "elvui", "ElvUI", "Elv", null, "16.00", "2026-10-13", null, "https://tukui.org/elvui", Logo, null, ["ElvUI", "ElvUI_Libraries", "ElvUI_Options"], null);

    private readonly string _addOnsPath = Directory.CreateTempSubdirectory("steward-tukui-").FullName;

    public void Dispose() => Directory.Delete(_addOnsPath, recursive: true);

    private void WriteAddon(string folderName, string toc)
    {
        Directory.CreateDirectory(Path.Combine(_addOnsPath, folderName));
        File.WriteAllText(Path.Combine(_addOnsPath, folderName, $"{folderName}.toc"), toc);
    }

    private void WriteElvUI() => WriteAddon(
        "ElvUI",
        "## Interface: 11509, 16001, 20506\n## Title: |cff1784d1ElvUI|r\n## Version: v16.00\n## X-Tukui-ProjectID: -2\n## X-Tukui-ProjectFolders: ElvUI, ElvUI_Libraries, ElvUI_Options\n");

    [Fact]
    public void Identify_BuildsOneRecordFromTheDeclaringToc_WithOnlyTheFoldersOnDisk()
    {
        WriteElvUI();
        WriteAddon("ElvUI_Libraries", "## Title: ElvUI Libraries\n");
        WriteAddon("Details", "## Title: Details\n");

        var records = TukUiAddons.Identify(_addOnsPath, ["Details", "ElvUI", "ElvUI_Libraries"], 16001, id => id == -2 ? ElvUI : null);

        var record = Assert.Single(records);
        Assert.Equal("tukui--2", record.Id);
        Assert.Equal("ElvUI", record.FolderName);
        Assert.Equal("ElvUI", record.Name);
        Assert.Equal(TukUiAddons.Source, record.Source);
        Assert.Equal(-2, record.ModId);
        Assert.Equal(["ElvUI", "ElvUI_Libraries"], record.Folders);
        Assert.Equal(Logo, record.IconUrl);
        Assert.Equal("https://tukui.org/elvui", record.WebsiteUrl);
    }

    [Fact]
    public void Identify_WithNoListLoaded_LeavesTheIconForTheFirstCheck()
    {
        WriteElvUI();

        var record = Assert.Single(TukUiAddons.Identify(_addOnsPath, ["ElvUI"], 16001, _ => null));

        Assert.Null(record.IconUrl);
    }

    [Fact]
    public void Identify_ATocDeclaringBothIds_IsClaimedByTukUi()
    {
        WriteAddon("ElvUI", "## Interface: 16001\n## Title: ElvUI\n## X-Curse-Project-ID: 231166\n## X-Tukui-ProjectID: -2\n## X-Tukui-ProjectFolders: ElvUI\n");

        var record = Assert.Single(TukUiAddons.Identify(_addOnsPath, ["ElvUI"], 16001, _ => null));

        Assert.Equal("tukui--2", record.Id);
        Assert.Equal(231166, LocalAddons.ReadDeclaredIds(_addOnsPath, "ElvUI", 16001)!.CurseProjectId);
    }

    [Fact]
    public void ToManagedAddon_IsAClientSideTukUiAddonGatedOnAddons()
    {
        var addon = TukUiAddons.ToManagedAddon(new ProviderAddonRecord("tukui--2", "ElvUI", "ElvUI", TukUiAddons.Source, -2, 0, ["ElvUI", "ElvUI_Options"], Logo));

        Assert.StartsWith("https://api.tukui.org/", addon.ManifestBaseUrl, StringComparison.Ordinal);
        Assert.Equal(-2, TukUiAddons.ProjectId(addon));
        Assert.Equal(Logo, addon.IconUrl);
        Assert.Equal([StewardClient.AddonsFeature], addon.Features);
        Assert.Equal("TukUI", addon.DisplaySource);
        Assert.False(addon.ManifestNeedsSession);
        Assert.False(addon.IconNeedsSession);
        Assert.False(addon.IsCurseForge);
        Assert.False(CurseForgeAddons.IsRecorded(addon));
        Assert.True(TukUiAddons.IsRecorded(addon));
        Assert.Equal(["ElvUI", "ElvUI_Options"], addon.Folders!);
    }

    [Fact]
    public void A_server_backed_record_maps_to_the_client_side_source_and_takes_the_listing_icon()
    {
        var stored = new ProviderAddonRecord("tukui--2", "ElvUI", "ElvUI", TukUiAddons.Source, -2, 0, ["ElvUI", "ElvUI_Libraries", "ElvUI_Options"], StewardIcon);

        var addon = TukUiAddons.ToManagedAddon(stored);
        var listed = TukUiAddons.WithListing(stored, ElvUI);

        Assert.DoesNotContain("/api/addons/tukui/", addon.ManifestBaseUrl, StringComparison.Ordinal);
        Assert.Null(addon.IconUrl);
        Assert.DoesNotContain("/api/addons/tukui/", addon.IconUri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(Logo, listed.IconUrl);
        Assert.Equal("https://tukui.org/elvui", listed.WebsiteUrl);
        Assert.Equal(stored with { IconUrl = Logo, WebsiteUrl = "https://tukui.org/elvui" }, listed);
        Assert.Equal(listed, TukUiAddons.WithListing(listed, ElvUI));
    }

    [Fact]
    public void Record_FromTheList_MatchesWhatTocAdoptionRecords()
    {
        WriteElvUI();
        WriteAddon("ElvUI_Libraries", "## Title: ElvUI Libraries\n");
        WriteAddon("ElvUI_Options", "## Title: ElvUI Options\n");
        var adopted = Assert.Single(TukUiAddons.Identify(_addOnsPath, ["ElvUI", "ElvUI_Libraries", "ElvUI_Options"], 16001, _ => ElvUI));

        var installed = TukUiAddons.Record(ElvUI);

        Assert.Equal(adopted with { Folders = installed.Folders }, installed);
        Assert.Equal(adopted.Folders, installed.Folders);
    }

    [Fact]
    public void ProjectId_IsOnlyReadFromTukUiRecords()
    {
        Assert.Equal(-1, TukUiAddons.ProjectId(new ManagedAddon("tukui--1", "Tukui", Source: TukUiAddons.Source)));
        Assert.Null(TukUiAddons.ProjectId(new ManagedAddon("tukui--1", "Tukui")));
        Assert.Null(TukUiAddons.ProjectId(new ManagedAddon("curseforge-1-2", "Tukui", Source: TukUiAddons.Source)));
    }

    [Fact]
    public void MatchesFor_FindsTheTukUiRecordForAFoldedRow()
    {
        WriteElvUI();
        WriteAddon("ElvUI_Options", "## Title: ElvUI Options\n");
        var records = TukUiAddons.Identify(_addOnsPath, ["ElvUI_Options", "ElvUI"], 16001, _ => null);

        Assert.Equal(records, CurseForgeAddons.MatchesFor(records, new LocalAddon("ElvUI_Libraries", "ElvUI Libraries", null, null, ["ElvUI_Options"])));
    }
}
