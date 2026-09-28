using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

namespace Steward.Core.Tests;

public sealed class AddonUpdaterTests : IDisposable
{
    private const string FolderName = "HoobiScripts";

    private readonly string _tempDir = Directory.CreateTempSubdirectory("steward-updater-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string CreateZip(Action<ZipArchive> populate)
    {
        var zipPath = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            populate(archive);
        }

        return zipPath;
    }

    [SuppressMessage("Security", "CA5350", Justification = "CurseForge manifests carry SHA-1 only")]
    private static string Sha1Hex(byte[] bytes) => Convert.ToHexString(SHA1.HashData(bytes));

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    [Fact]
    public void ExtractZip_ExtractsFilesUnderAddOnsPath()
    {
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        Directory.CreateDirectory(addOnsPath);
        var zipPath = CreateZip(a => WriteEntry(a, "HoobiScripts/HoobiScripts.toc", "## Version: 1.0.0"));

        AddonUpdater.ExtractZip(zipPath, addOnsPath);

        var extractedPath = Path.Combine(addOnsPath, "HoobiScripts", "HoobiScripts.toc");
        Assert.True(File.Exists(extractedPath));
        Assert.Equal("## Version: 1.0.0", File.ReadAllText(extractedPath));
    }

    [Fact]
    public void ExtractZip_Rejects_EscapingEntry()
    {
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        Directory.CreateDirectory(addOnsPath);
        var zipPath = CreateZip(a => WriteEntry(a, "../escape.txt", "nope"));

        Assert.Throws<InvalidOperationException>(() => AddonUpdater.ExtractZip(zipPath, addOnsPath));
    }

    [Fact]
    public async Task VerifyChecksumAsync_Throws_AndDeletesFile_OnMismatch()
    {
        var filePath = Path.Combine(_tempDir, "payload.zip");
        await File.WriteAllTextAsync(filePath, "payload", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AddonUpdater.VerifyChecksumAsync(filePath, new string('0', 64), null, TestContext.Current.CancellationToken));

        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task VerifyChecksumAsync_UsesSha1_WhenNoSha256()
    {
        var filePath = Path.Combine(_tempDir, "payload.zip");
        await File.WriteAllTextAsync(filePath, "payload", TestContext.Current.CancellationToken);
        var sha1 = Sha1Hex(File.ReadAllBytes(filePath)).ToLowerInvariant();

        await AddonUpdater.VerifyChecksumAsync(filePath, null, sha1, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task VerifyChecksumAsync_PrefersSha256_OverSha1()
    {
        var filePath = Path.Combine(_tempDir, "payload.zip");
        await File.WriteAllTextAsync(filePath, "payload", TestContext.Current.CancellationToken);
        var sha1 = Sha1Hex(File.ReadAllBytes(filePath));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AddonUpdater.VerifyChecksumAsync(filePath, new string('0', 64), sha1, TestContext.Current.CancellationToken));

        Assert.StartsWith("SHA-256 mismatch", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyChecksumAsync_Sha1Mismatch_Throws_AndDeletesFile()
    {
        var filePath = Path.Combine(_tempDir, "payload.zip");
        await File.WriteAllTextAsync(filePath, "payload", TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AddonUpdater.VerifyChecksumAsync(filePath, null, new string('0', 40), TestContext.Current.CancellationToken));

        Assert.StartsWith("SHA-1 mismatch", ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task VerifyChecksumAsync_NoHash_Throws()
    {
        var filePath = Path.Combine(_tempDir, "payload.zip");
        await File.WriteAllTextAsync(filePath, "payload", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AddonUpdater.VerifyChecksumAsync(filePath, null, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VerifyChecksumAsync_Succeeds_OnMatch()
    {
        var filePath = Path.Combine(_tempDir, "payload.zip");
        await File.WriteAllTextAsync(filePath, "payload", TestContext.Current.CancellationToken);
        var expectedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(filePath)));

        await AddonUpdater.VerifyChecksumAsync(filePath, expectedHash, null, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public void RemoveExistingInstall_Throws_WhenExistingFolderHasNoToc()
    {
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        Directory.CreateDirectory(Path.Combine(addOnsPath, FolderName));
        File.WriteAllText(Path.Combine(addOnsPath, FolderName, "notes.txt"), "hi");

        Assert.Throws<InvalidOperationException>(() => AddonUpdater.RemoveExistingInstall(addOnsPath, FolderName));
        Assert.True(Directory.Exists(Path.Combine(addOnsPath, FolderName)));
    }

    [Fact]
    public void RemoveExistingInstall_Deletes_AGitWorkingTreeWithReadOnlyFiles()
    {
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        var existing = Path.Combine(addOnsPath, FolderName);
        var objectsDir = Path.Combine(existing, ".git", "objects", "ab");
        Directory.CreateDirectory(objectsDir);
        File.WriteAllText(Path.Combine(existing, $"{FolderName}.toc"), "## Version: 0.9.0");
        var objectFile = Path.Combine(objectsDir, "cd1234");
        File.WriteAllText(objectFile, "object");
        File.SetAttributes(objectFile, FileAttributes.ReadOnly);

        AddonUpdater.RemoveExistingInstall(addOnsPath, FolderName);

        Assert.False(Directory.Exists(existing));
    }

    [Fact]
    public void RemoveExistingInstall_Deletes_WhenTocPresent()
    {
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        var existing = Path.Combine(addOnsPath, FolderName);
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, $"{FolderName}.toc"), "## Version: 0.9.0");

        AddonUpdater.RemoveExistingInstall(addOnsPath, FolderName);

        Assert.False(Directory.Exists(existing));
    }

    private sealed class ZipBytesStubHandler(Uri expectedUri, byte[] zipBytes) : HttpMessageHandler
    {
        public Uri? RequestedUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUri = request.RequestUri;
            if (request.RequestUri != expectedUri)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipBytes),
            });
        }
    }

    [Fact]
    public async Task InstallAsync_AbsoluteZipUrl_DownloadsItAsIs()
    {
        var zipPath = CreateZip(a => WriteEntry(a, "RXPGuides/RXPGuides.toc", "## Version: v1.0.0"));
        var zipBytes = await File.ReadAllBytesAsync(zipPath, TestContext.Current.CancellationToken);
        var sha256 = Convert.ToHexString(SHA256.HashData(zipBytes));
        var zipUri = new Uri("https://github.com/RestedXP/RXPGuides/releases/download/v1.0.0/RXPGuides-v1.0.0.zip");

        var handler = new ZipBytesStubHandler(zipUri, zipBytes);
        var updater = new AddonUpdater(new HttpClient(handler));
        var addon = new ManagedAddon("restedxp", "RXPGuides", "https://addon.example/restedxp/");
        var release = new AddonRelease("v1.0.0", zipUri.ToString(), sha256, zipBytes.Length, DateTimeOffset.UtcNow);
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        Directory.CreateDirectory(addOnsPath);

        await updater.InstallAsync(addon, "release", release, addOnsPath, null, TestContext.Current.CancellationToken);

        Assert.Equal(zipUri, handler.RequestedUri);
        Assert.True(File.Exists(Path.Combine(addOnsPath, "RXPGuides", "RXPGuides.toc")));
    }

    private static readonly Uri QuestieZip = new("https://edge.forgecdn.net/files/1/2/Questie.zip");
    private static readonly ManagedAddon Questie = new("questie", "Questie", "https://gigagrug.example/api/addons/curseforge/334372/88568/");

    private async Task<(AddonUpdater Updater, ZipBytesStubHandler Handler, string AddOnsPath, string Sha1)> QuestieSetupAsync(params string[] entries)
    {
        var zipPath = CreateZip(a =>
        {
            foreach (var entry in entries)
            {
                WriteEntry(a, entry, "## Version: 2");
            }
        });
        var zipBytes = await File.ReadAllBytesAsync(zipPath, TestContext.Current.CancellationToken);
        var handler = new ZipBytesStubHandler(QuestieZip, zipBytes);
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        Directory.CreateDirectory(addOnsPath);
        return (new AddonUpdater(new HttpClient(handler)), handler, addOnsPath, Sha1Hex(zipBytes));
    }

    private static void WriteInstalled(string addOnsPath, string folder, string tocName)
    {
        Directory.CreateDirectory(Path.Combine(addOnsPath, folder));
        File.WriteAllText(Path.Combine(addOnsPath, folder, tocName), "## Version: 1");
        File.WriteAllText(Path.Combine(addOnsPath, folder, "stale.lua"), "old");
    }

    [Fact]
    public async Task InstallAsync_MultiFolderSha1Manifest_ReplacesEveryFolder()
    {
        var (updater, _, addOnsPath, sha1) = await QuestieSetupAsync("QuestieDB/QuestieDB.toc", "Questie/Questie.toc");
        WriteInstalled(addOnsPath, "Questie", "Questie.toc");
        WriteInstalled(addOnsPath, "QuestieDB", "QuestieDB.toc");
        var release = new AddonRelease("v2", QuestieZip.ToString(), null, 0, DateTimeOffset.UtcNow, Sha1: sha1, Folders: ["QuestieDB", "Questie"]);

        await updater.InstallAsync(Questie, "release", release, addOnsPath, null, TestContext.Current.CancellationToken);

        Assert.Equal("## Version: 2", File.ReadAllText(Path.Combine(addOnsPath, "Questie", "Questie.toc")));
        Assert.Equal("## Version: 2", File.ReadAllText(Path.Combine(addOnsPath, "QuestieDB", "QuestieDB.toc")));
        Assert.False(File.Exists(Path.Combine(addOnsPath, "Questie", "stale.lua")));
        Assert.False(File.Exists(Path.Combine(addOnsPath, "QuestieDB", "stale.lua")));
    }

    [Fact]
    public async Task InstallAsync_ZipWithFolderOutsideFolders_IsRefused_AndNothingDeleted()
    {
        var (updater, _, addOnsPath, sha1) = await QuestieSetupAsync("Questie/Questie.toc", "Intruder/Intruder.toc");
        WriteInstalled(addOnsPath, "Questie", "Questie.toc");
        var release = new AddonRelease("v2", QuestieZip.ToString(), null, 0, DateTimeOffset.UtcNow, Sha1: sha1, Folders: ["Questie"]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => updater.InstallAsync(Questie, "release", release, addOnsPath, null, TestContext.Current.CancellationToken));

        Assert.True(File.Exists(Path.Combine(addOnsPath, "Questie", "stale.lua")));
        Assert.False(Directory.Exists(Path.Combine(addOnsPath, "Intruder")));
    }

    [Fact]
    public async Task InstallAsync_ZipWithRootFile_IsRefused()
    {
        var (updater, _, addOnsPath, sha1) = await QuestieSetupAsync("Questie/Questie.toc", "readme.txt");
        var release = new AddonRelease("v2", QuestieZip.ToString(), null, 0, DateTimeOffset.UtcNow, Sha1: sha1, Folders: ["Questie"]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => updater.InstallAsync(Questie, "release", release, addOnsPath, null, TestContext.Current.CancellationToken));

        Assert.False(File.Exists(Path.Combine(addOnsPath, "readme.txt")));
    }

    [Fact]
    public async Task InstallAsync_OneFolderFailsTocGuard_DeletesNone()
    {
        var (updater, _, addOnsPath, sha1) = await QuestieSetupAsync("QuestieDB/QuestieDB.toc", "Questie/Questie.toc");
        WriteInstalled(addOnsPath, "Questie", "Questie.toc");
        WriteInstalled(addOnsPath, "QuestieDB", "notes.txt");
        var release = new AddonRelease("v2", QuestieZip.ToString(), null, 0, DateTimeOffset.UtcNow, Sha1: sha1, Folders: ["Questie", "QuestieDB"]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => updater.InstallAsync(Questie, "release", release, addOnsPath, null, TestContext.Current.CancellationToken));

        Assert.True(File.Exists(Path.Combine(addOnsPath, "Questie", "stale.lua")));
        Assert.True(File.Exists(Path.Combine(addOnsPath, "QuestieDB", "stale.lua")));
    }

    [Fact]
    public async Task InstallAsync_NonDistributable_IsRefused_WithoutDownloading()
    {
        var (updater, handler, addOnsPath, sha1) = await QuestieSetupAsync("Questie/Questie.toc");
        var release = new AddonRelease("v2", QuestieZip.ToString(), null, 0, DateTimeOffset.UtcNow, Sha1: sha1, Website: "https://www.curseforge.com/wow/addons/dbm", Distributable: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => updater.InstallAsync(Questie, "release", release, addOnsPath, null, TestContext.Current.CancellationToken));

        Assert.Contains("not distributable", ex.Message, StringComparison.Ordinal);
        Assert.Null(handler.RequestedUri);
    }

    [Fact]
    public async Task InstallAsync_NoHash_IsRefused_WithoutDownloading()
    {
        var (updater, handler, addOnsPath, _) = await QuestieSetupAsync("Questie/Questie.toc");
        var release = new AddonRelease("v2", QuestieZip.ToString(), null, 0, DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => updater.InstallAsync(Questie, "release", release, addOnsPath, null, TestContext.Current.CancellationToken));

        Assert.Null(handler.RequestedUri);
    }

    [Fact]
    public void InstallFolders_FallsBackToFolderName_WhenReleaseListsNone()
    {
        Assert.Equal(["Questie"], AddonUpdater.InstallFolders(Questie, null));
        Assert.Equal(["Questie"], AddonUpdater.InstallFolders(Questie, new AddonRelease("v", "z", "s", 0, DateTimeOffset.UtcNow, Folders: [])));
        Assert.Equal(["QuestieDB", "Questie"], AddonUpdater.InstallFolders(Questie, new AddonRelease("v", "z", "s", 0, DateTimeOffset.UtcNow, Folders: ["QuestieDB", "Questie"])));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("")]
    public void InstallFolders_InvalidName_Throws(string folder)
    {
        Assert.Throws<InvalidOperationException>(
            () => AddonUpdater.InstallFolders(Questie, new AddonRelease("v", "z", "s", 0, DateTimeOffset.UtcNow, Folders: [folder])));
    }

    [Fact]
    public void Uninstall_RemovesEveryFolder()
    {
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        WriteInstalled(addOnsPath, "Questie", "Questie.toc");
        WriteInstalled(addOnsPath, "QuestieDB", "QuestieDB.toc");

        AddonUpdater.Uninstall(addOnsPath, ["Questie", "QuestieDB", "NotInstalled"]);

        Assert.False(Directory.Exists(Path.Combine(addOnsPath, "Questie")));
        Assert.False(Directory.Exists(Path.Combine(addOnsPath, "QuestieDB")));
    }

    [Fact]
    public void Uninstall_AnyFolderFailingTocGuard_DeletesNone()
    {
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        WriteInstalled(addOnsPath, "Questie", "Questie.toc");
        WriteInstalled(addOnsPath, "QuestieDB", "QuestieDB_Forever.toc");

        Assert.Throws<InvalidOperationException>(() => AddonUpdater.Uninstall(addOnsPath, ["Questie", "QuestieDB"]));

        Assert.True(Directory.Exists(Path.Combine(addOnsPath, "Questie")));
        Assert.True(Directory.Exists(Path.Combine(addOnsPath, "QuestieDB")));
    }
}
