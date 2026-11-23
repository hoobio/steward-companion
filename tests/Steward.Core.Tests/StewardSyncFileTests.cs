namespace Steward.Core.Tests;

public sealed class StewardSyncFileTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("steward-sync-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static SyncPayload SamplePayload() => SyncDocument.Parse(
        """{"version":1,"signOut":"dropMe","payload":{"statuses":["Officer","Raider"],"me":{"id":"700"}},"images":{"guild":null,"people":{},"discord":{}}}""",
        DateTimeOffset.FromUnixTimeSeconds(1758270000));

    private static AvatarImage SampleAvatar() =>
        new("https://cdn.discordapp.com/avatars/1/hash.png?size=64", 64, 64, new byte[64 * 64 * 4]);

    private static LuaValue Parse(string rendered)
    {
        var asAssignment = rendered.Replace("Steward.LoadSync(", "X = ", StringComparison.Ordinal);
        return LuaSavedVariables.Parse(asAssignment[..asAssignment.LastIndexOf(')')])["X"];
    }

    private string InstallAddon(bool withToc = true)
    {
        var addOnsPath = Path.Combine(_root, "AddOns");
        var addonPath = Path.Combine(addOnsPath, "Steward");
        Directory.CreateDirectory(addonPath);
        if (withToc)
        {
            File.WriteAllText(Path.Combine(addonPath, "Steward.toc"), "## Interface: 11507\n");
        }

        return addOnsPath;
    }

    [Fact]
    public void Render_StartsWithTheLoadSyncCall()
    {
        var rendered = StewardSyncFile.Render(SamplePayload());

        Assert.StartsWith("Steward.LoadSync(", rendered, StringComparison.Ordinal);
        Assert.EndsWith(")" + Environment.NewLine, rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WritesTheOverlayFirst_ThenThePayloadKeysInDocumentOrder()
    {
        var table = Parse(StewardSyncFile.Render(SamplePayload(), fingerprint: "ABC"));

        Assert.Equal(["writtenAt", "companion", "fingerprint", "statuses", "me"], table.Table.Select(entry => entry.Key!.Text));
        Assert.Equal(1758270000d, table.GetNumber("writtenAt"));
        Assert.Equal(["Officer", "Raider"], table.GetTable("statuses")!.Items.Select(item => item.Text));
    }

    [Fact]
    public void Render_WritesTheCompanionMarkerListingEveryForwardedRecord()
    {
        var companion = Parse(StewardSyncFile.Render(SamplePayload())).GetTable("companion")!;

        Assert.Equal(["talents", "notes", "linkSet"], companion.GetTable("forwards")!.Items.Select(item => item.Text));
    }

    [Fact]
    public void Render_KeepsTheOfficerLinkSetAtAndTheDiscordUsernameUnchanged()
    {
        var payload = SyncDocument.Parse(
            """{"version":1,"signOut":"dropMe","payload":{"linkSetAt":{"Player-1234-0000ABCD":1795000000},"discord":[{"id":"300000000000000003","username":"peer"}]}}""",
            DateTimeOffset.FromUnixTimeSeconds(1758270000));

        var table = Parse(StewardSyncFile.Render(payload));

        Assert.Equal(1795000000d, table.GetTable("linkSetAt")!.GetNumber("Player-1234-0000ABCD"));
        Assert.Equal("peer", table.GetTable("discord")!.Items[0].GetString("username"));
    }

    [Fact]
    public void Fingerprint_IsTheSha256OfThePayloadTheImageUrlsAndTheCompanionMarker()
    {
        var payload = SamplePayload();
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            LuaWriter.Serialize(LuaValue.FromTable(payload.Entries))
            + LuaWriter.Serialize(Parse(StewardSyncFile.Render(payload)).GetTable("companion")!))));

        Assert.Equal(expected, StewardSyncFile.Fingerprint(payload));
    }

    [Fact]
    public void Fingerprint_IgnoresTheWriteTime_AndChangesWithThePayloadOrAnImageUrl()
    {
        var payload = SamplePayload();
        var withImage = payload with { Images = SyncImages.None with { People = new Dictionary<string, string> { ["1"] = "https://cdn.example.com/1.png?size=64" } } };

        Assert.Equal(StewardSyncFile.Fingerprint(payload), StewardSyncFile.Fingerprint(payload with { WrittenAt = DateTimeOffset.UnixEpoch }));
        Assert.NotEqual(StewardSyncFile.Fingerprint(payload), StewardSyncFile.Fingerprint(payload with { Entries = [.. payload.Entries.Reverse()] }));
        Assert.NotEqual(StewardSyncFile.Fingerprint(payload), StewardSyncFile.Fingerprint(withImage));
    }

    [Fact]
    public void Write_KeepsTheFingerprintInsideTheFirst512Characters_ForALargeDocumentWithAnAvatar()
    {
        var addOnsPath = InstallAddon();
        var payload = SamplePayload() with
        {
            Avatar = SampleAvatar(),
            Entries = [.. Enumerable.Range(0, 2000).Select(i => new LuaEntry(LuaValue.FromString($"key{i}"), LuaValue.FromString(new string('x', 100))))],
        };

        StewardSyncFile.Write(addOnsPath, payload);

        var text = File.ReadAllText(StewardSyncFile.PathFor(addOnsPath));
        Assert.InRange(text.IndexOf("[\"fingerprint\"]", StringComparison.Ordinal), 0, 400);
        Assert.Equal(StewardSyncFile.Fingerprint(payload), StewardSyncFile.ReadFingerprint(addOnsPath));
    }

    [Fact]
    public void Write_CreatesTheSyncFile_WhenTheAddonIsInstalled()
    {
        var addOnsPath = InstallAddon();

        StewardSyncFile.Write(addOnsPath, SamplePayload());

        var target = Path.Combine(addOnsPath, "Steward", "StewardSync.lua");
        Assert.True(File.Exists(target));
        Assert.StartsWith("Steward.LoadSync(", File.ReadAllText(target), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_Throws_WhenTheTocIsMissing()
    {
        var addOnsPath = InstallAddon(withToc: false);

        Assert.Throws<InvalidOperationException>(() => StewardSyncFile.Write(addOnsPath, SamplePayload()));

        Assert.False(File.Exists(Path.Combine(addOnsPath, "Steward", "StewardSync.lua")));
    }

    [Fact]
    public void Write_Throws_ForAnAddOnsPathContainingAWtfSegment()
    {
        var addOnsPath = Path.Combine(_root, "WTF", "AddOns");
        var addonPath = Path.Combine(addOnsPath, "Steward");
        Directory.CreateDirectory(addonPath);
        File.WriteAllText(Path.Combine(addonPath, "Steward.toc"), "## Interface: 11507\n");

        Assert.Throws<InvalidOperationException>(() => StewardSyncFile.Write(addOnsPath, SamplePayload()));

        Assert.False(File.Exists(Path.Combine(addonPath, "StewardSync.lua")));
    }

    [Fact]
    public void Write_WritesTheAvatarAndNamesIt_WhenThePayloadCarriesOne()
    {
        var addOnsPath = InstallAddon();
        var payload = SamplePayload() with { Avatar = SampleAvatar() };

        StewardSyncFile.Write(addOnsPath, payload);

        var avatar = Path.Combine(addOnsPath, "Steward", "Avatar.tga");
        Assert.True(File.Exists(avatar));
        Assert.Equal(18 + (64 * 64 * 4), new FileInfo(avatar).Length);
        var table = Parse(File.ReadAllText(StewardSyncFile.PathFor(addOnsPath)));
        Assert.Equal(["writtenAt", "companion", "fingerprint", "avatar"], table.Table.Take(4).Select(entry => entry.Key!.Text));
        Assert.Equal(@"Interface\AddOns\Steward\Avatar.tga", table.GetString("avatar"));
    }

    [Fact]
    public void Write_OmitsTheAvatarKey_WhenThePayloadCarriesNoAvatar()
    {
        var addOnsPath = InstallAddon();

        StewardSyncFile.Write(addOnsPath, SamplePayload());

        Assert.False(File.Exists(Path.Combine(addOnsPath, "Steward", "Avatar.tga")));
        Assert.DoesNotContain(
            "avatar",
            File.ReadAllText(Path.Combine(addOnsPath, "Steward", "StewardSync.lua")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Write_OmitsTheAvatarKey_WhenTheImageCannotBeEncoded()
    {
        var addOnsPath = InstallAddon();
        var payload = SamplePayload() with
        {
            Avatar = new AvatarImage("https://cdn.discordapp.com/avatars/1/hash.png?size=64", 48, 48, new byte[48 * 48 * 4]),
        };

        StewardSyncFile.Write(addOnsPath, payload);

        Assert.False(File.Exists(Path.Combine(addOnsPath, "Steward", "Avatar.tga")));
        Assert.DoesNotContain(
            "avatar",
            File.ReadAllText(Path.Combine(addOnsPath, "Steward", "StewardSync.lua")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Write_LeavesNoTempFileBehind()
    {
        var addOnsPath = InstallAddon();

        StewardSyncFile.Write(addOnsPath, SamplePayload());

        Assert.Empty(Directory.GetFiles(Path.Combine(addOnsPath, "Steward"), "*.tmp"));
    }

    [Fact]
    public void Write_ReplacesAnExistingFile()
    {
        var addOnsPath = InstallAddon();
        var target = Path.Combine(addOnsPath, "Steward", "StewardSync.lua");
        File.WriteAllText(target, "Steward.LoadSync({[\"stale\"] = true})");

        StewardSyncFile.Write(addOnsPath, SamplePayload());

        Assert.DoesNotContain("stale", File.ReadAllText(target), StringComparison.Ordinal);
    }

    [Fact]
    public void SignedOut_DropMe_KeepsEveryKeyButMe()
    {
        var signedOut = SyncPayload.SignedOut(SamplePayload(), DateTimeOffset.FromUnixTimeSeconds(1758280000), heldStewardFeature: true)!;

        Assert.Equal(["writtenAt", "companion", "statuses"], Parse(StewardSyncFile.Render(signedOut)).Table.Select(entry => entry.Key!.Text));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1758280000), signedOut.WrittenAt);
    }

    [Fact]
    public void SignedOut_Clear_WritesTheOverlayOnly()
    {
        var last = SamplePayload() with
        {
            SignOut = SyncSignOut.Clear,
            Images = SyncImages.None with { People = new Dictionary<string, string> { ["1"] = "https://cdn.example.com/1.png?size=64" } },
            Avatar = SampleAvatar(),
        };

        var signedOut = SyncPayload.SignedOut(last, DateTimeOffset.FromUnixTimeSeconds(1758280000), heldStewardFeature: true)!;

        Assert.Empty(signedOut.Entries);
        Assert.Null(signedOut.Avatar);
        Assert.Empty(signedOut.Images.People);
        Assert.Equal(["writtenAt", "companion"], Parse(StewardSyncFile.Render(signedOut)).Table.Select(entry => entry.Key!.Text));
    }

    [Fact]
    public void SignedOut_WithNoLastDocumentForAnOfficer_WritesNothing()
    {
        Assert.Null(SyncPayload.SignedOut(null, DateTimeOffset.UnixEpoch, heldStewardFeature: true));
    }

    [Fact]
    public void SignedOut_WithNoLastDocumentForANonOfficer_WritesTheOverlayOnly()
    {
        var signedOut = SyncPayload.SignedOut(null, DateTimeOffset.UnixEpoch, heldStewardFeature: false)!;

        Assert.Empty(signedOut.Entries);
        Assert.Equal(["writtenAt", "companion"], Parse(StewardSyncFile.Render(signedOut)).Table.Select(entry => entry.Key!.Text));
    }
}
