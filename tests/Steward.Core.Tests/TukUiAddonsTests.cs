namespace Steward.Core.Tests;

public sealed class TukUiAddonsTests : IDisposable
{
    private const string BaseUrl = "https://api.hoobi.io/guild/api/addons/tukui/-2/";

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

        var records = TukUiAddons.Identify(_addOnsPath, ["Details", "ElvUI", "ElvUI_Libraries"], 16001, id => $"https://api.hoobi.io/guild/api/addons/tukui/{id}/");

        var record = Assert.Single(records);
        Assert.Equal("tukui--2", record.Id);
        Assert.Equal("ElvUI", record.FolderName);
        Assert.Equal("ElvUI", record.Name);
        Assert.Equal(TukUiAddons.Source, record.Source);
        Assert.Equal(-2, record.ModId);
        Assert.Equal(["ElvUI", "ElvUI_Libraries"], record.Folders);
        Assert.Equal($"{BaseUrl}icon.png", record.IconUrl);
    }

    [Fact]
    public void ToManagedAddon_IsAPublicSha256ManifestAddonGatedOnAddons()
    {
        var addon = TukUiAddons.ToManagedAddon(new ProviderAddonRecord("tukui--2", "ElvUI", "ElvUI", TukUiAddons.Source, -2, 0, ["ElvUI", "ElvUI_Options"], $"{BaseUrl}icon.png"), BaseUrl);

        Assert.Equal(BaseUrl, addon.ManifestBaseUrl);
        Assert.Equal([StewardClient.AddonsFeature], addon.Features);
        Assert.Equal("TukUI", addon.DisplaySource);
        Assert.False(addon.ManifestNeedsSession);
        Assert.False(addon.IconNeedsSession);
        Assert.True(addon.ZipResolvesAgainstManifest);
        Assert.False(addon.IsCurseForge);
        Assert.False(CurseForgeAddons.IsRecorded(addon));
        Assert.True(TukUiAddons.IsRecorded(addon));
        Assert.Equal(["ElvUI", "ElvUI_Options"], addon.Folders!);
    }

    [Fact]
    public void MatchesFor_FindsTheTukUiRecordForAFoldedRow()
    {
        WriteElvUI();
        WriteAddon("ElvUI_Options", "## Title: ElvUI Options\n");
        var records = TukUiAddons.Identify(_addOnsPath, ["ElvUI_Options", "ElvUI"], 16001, id => $"https://api.hoobi.io/guild/api/addons/tukui/{id}/");

        Assert.Equal(records, CurseForgeAddons.MatchesFor(records, new LocalAddon("ElvUI_Libraries", "ElvUI Libraries", null, null, ["ElvUI_Options"])));
    }
}
