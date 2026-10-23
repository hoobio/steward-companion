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

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AddonUpdater.VerifyChecksumAsync(filePath, new string('0', 64), TestContext.Current.CancellationToken));

        Assert.StartsWith("SHA-256 mismatch", ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task VerifyChecksumAsync_NoHash_Throws()
    {
        var filePath = Path.Combine(_tempDir, "payload.zip");
        await File.WriteAllTextAsync(filePath, "payload", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AddonUpdater.VerifyChecksumAsync(filePath, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VerifyChecksumAsync_Succeeds_OnMatch()
    {
        var filePath = Path.Combine(_tempDir, "payload.zip");
        await File.WriteAllTextAsync(filePath, "payload", TestContext.Current.CancellationToken);
        var expectedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(filePath)));

        await AddonUpdater.VerifyChecksumAsync(filePath, expectedHash, TestContext.Current.CancellationToken);

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

    [Fact]
    public void RemoveExistingInstall_Deletes_WhenOnlyAFlavourTocPresent()
    {
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        var existing = Path.Combine(addOnsPath, FolderName);
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, $"{FolderName}_Forever.toc"), "## Version: 0.9.0");

        AddonUpdater.RemoveExistingInstall(addOnsPath, FolderName);

        Assert.False(Directory.Exists(existing));
    }

    [Fact]
    public void RemoveExistingInstall_Throws_WhenOnlyAnotherFoldersTocPresent()
    {
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        var existing = Path.Combine(addOnsPath, FolderName);
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "Other_Forever.toc"), "## Version: 0.9.0");
        File.WriteAllText(Path.Combine(existing, "Other.toc"), "## Version: 0.9.0");

        Assert.Throws<InvalidOperationException>(() => AddonUpdater.RemoveExistingInstall(addOnsPath, FolderName));
        Assert.True(Directory.Exists(existing));
    }

    private sealed class ZipBytesStubHandler(Uri expectedUri, byte[] zipBytes) : HttpMessageHandler
    {
        public Uri? RequestedUri { get; private set; }

        public long Size => zipBytes.Length;

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

    private sealed class RepublishedHandler(Uri oldZip, Uri newZip, byte[] newZipBytes, string manifestJson) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(request.RequestUri switch
            {
                var uri when uri == newZip => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(newZipBytes) },
                var uri when uri == oldZip => new HttpResponseMessage(HttpStatusCode.NotFound),
                var uri when uri!.AbsolutePath.EndsWith("latest-release.json", StringComparison.Ordinal) =>
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestJson) },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
        }
    }

    private async Task<(RepublishedHandler Handler, AddonUpdater Updater, ManagedAddon Addon, AddonRelease Stale, string AddOnsPath)> RepublishedSetupAsync(bool manifestHasNewBuild)
    {
        var zipBytes = await File.ReadAllBytesAsync(CreateZip(a => WriteEntry(a, "RXPGuides/RXPGuides.toc", "## Version: v2")), TestContext.Current.CancellationToken);
        var sha256 = Convert.ToHexString(SHA256.HashData(zipBytes));
        var oldZip = new Uri("https://api.example/zips/RXPGuides-v1.zip");
        var newZip = new Uri("https://api.example/zips/RXPGuides-v2.zip");
        var manifest = manifestHasNewBuild
            ? $$"""{"version":"v2","zip":"{{newZip}}","sha256":"{{sha256}}","size":{{zipBytes.Length}},"released":"2026-10-12T00:00:00+00:00"}"""
            : "null";
        var handler = new RepublishedHandler(oldZip, newZip, zipBytes, manifest);
        var addon = new ManagedAddon("restedxp", "RXPGuides", "https://api.example/addons/restedxp/");
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        Directory.CreateDirectory(addOnsPath);
        return (handler, new AddonUpdater(new HttpClient(handler)), addon, new AddonRelease("v1", oldZip.ToString(), sha256, zipBytes.Length, DateTimeOffset.UtcNow), addOnsPath);
    }

    [Fact]
    public async Task InstallRefetchingOn404Async_Zip404_RefetchesTheManifestAndInstallsTheNewBuild()
    {
        var (handler, updater, addon, stale, addOnsPath) = await RepublishedSetupAsync(manifestHasNewBuild: true);

        var (folders, installed) = await updater.InstallRefetchingOn404Async(addon, "release", stale, addOnsPath, null, TestContext.Current.CancellationToken);

        Assert.Equal("v2", installed.Version);
        Assert.Equal(["RXPGuides"], folders);
        Assert.True(File.Exists(Path.Combine(addOnsPath, "RXPGuides", "RXPGuides.toc")));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task InstallRefetchingOn404Async_StillMissingAfterTheRefetch_FailsWithTheOriginal404()
    {
        var (handler, updater, addon, stale, addOnsPath) = await RepublishedSetupAsync(manifestHasNewBuild: false);

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => updater.InstallRefetchingOn404Async(addon, "release", stale, addOnsPath, null, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    private static readonly Uri QuestieZip = new("https://edge.forgecdn.net/files/1/2/Questie.zip");
    private static readonly ManagedAddon Questie = new("questie", "Questie", "https://steward.example/api/addons/curseforge/334372/88568/");

    private async Task<(AddonUpdater Updater, ZipBytesStubHandler Handler, string AddOnsPath, string Sha256)> QuestieSetupAsync(params string[] entries)
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
        return (new AddonUpdater(new HttpClient(handler)), handler, addOnsPath, Convert.ToHexString(SHA256.HashData(zipBytes)));
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
        var (updater, _, addOnsPath, sha256) = await QuestieSetupAsync("QuestieDB/QuestieDB.toc", "Questie/Questie.toc");
        WriteInstalled(addOnsPath, "Questie", "Questie.toc");
        WriteInstalled(addOnsPath, "QuestieDB", "QuestieDB.toc");
        var release = new AddonRelease("v2", QuestieZip.ToString(), sha256, 0, DateTimeOffset.UtcNow, Folders: ["QuestieDB", "Questie"]);

        await updater.InstallAsync(Questie, "release", release, addOnsPath, null, TestContext.Current.CancellationToken);

        Assert.Equal("## Version: 2", File.ReadAllText(Path.Combine(addOnsPath, "Questie", "Questie.toc")));
        Assert.Equal("## Version: 2", File.ReadAllText(Path.Combine(addOnsPath, "QuestieDB", "QuestieDB.toc")));
        Assert.False(File.Exists(Path.Combine(addOnsPath, "Questie", "stale.lua")));
        Assert.False(File.Exists(Path.Combine(addOnsPath, "QuestieDB", "stale.lua")));
    }

    [Fact]
    public async Task InstallAsync_WaitsForAddOnsWriteLock_BeforeTouchingFolders()
    {
        var (updater, _, addOnsPath, sha256) = await QuestieSetupAsync("QuestieDB/QuestieDB.toc", "Questie/Questie.toc");
        WriteInstalled(addOnsPath, "Questie", "Questie.toc");
        var release = new AddonRelease("v2", QuestieZip.ToString(), sha256, 0, DateTimeOffset.UtcNow, Folders: ["QuestieDB", "Questie"]);
        using var held = new ManualResetEventSlim();
        using var releaseHolder = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            lock (AddonUpdater.AddOnsWriteLock)
            {
                held.Set();
                releaseHolder.Wait();
            }
        });
        holder.Start();
        held.Wait(TestContext.Current.CancellationToken);

        var install = updater.InstallAsync(Questie, "release", release, addOnsPath, null, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        var stillOld = File.Exists(Path.Combine(addOnsPath, "Questie", "stale.lua"));
        releaseHolder.Set();
        await install;
        holder.Join();

        Assert.True(stillOld);
        Assert.False(File.Exists(Path.Combine(addOnsPath, "Questie", "stale.lua")));
    }

    [Fact]
    public async Task InstallAsync_ZipWithFolderOutsideFolders_IsRefused_AndNothingDeleted()
    {
        var (updater, _, addOnsPath, sha256) = await QuestieSetupAsync("Questie/Questie.toc", "Intruder/Intruder.toc");
        WriteInstalled(addOnsPath, "Questie", "Questie.toc");
        var release = new AddonRelease("v2", QuestieZip.ToString(), sha256, 0, DateTimeOffset.UtcNow, Folders: ["Questie"]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => updater.InstallAsync(Questie, "release", release, addOnsPath, null, TestContext.Current.CancellationToken));

        Assert.True(File.Exists(Path.Combine(addOnsPath, "Questie", "stale.lua")));
        Assert.False(Directory.Exists(Path.Combine(addOnsPath, "Intruder")));
    }

    [Fact]
    public async Task InstallAsync_CurseForgeZipWithUnlistedFolder_InstallsAndReportsIt()
    {
        var (updater, handler, addOnsPath, _) = await QuestieSetupAsync("Questie/Questie.toc", "QuestieExtra/QuestieExtra.toc");
        WriteInstalled(addOnsPath, "Questie", "Questie.toc");
        var release = new AddonRelease("v2", QuestieZip.ToString(), null, handler.Size, DateTimeOffset.UtcNow, Folders: ["Questie"]);

        var folders = await updater.InstallAsync(CurseForgeQuestie, "release", release, addOnsPath, null, TestContext.Current.CancellationToken);

        Assert.Equal(["Questie", "QuestieExtra"], folders);
        Assert.True(File.Exists(Path.Combine(addOnsPath, "QuestieExtra", "QuestieExtra.toc")));
        Assert.False(File.Exists(Path.Combine(addOnsPath, "Questie", "stale.lua")));
    }

    [Theory]
    [InlineData("Questie/../WeakAuras/WeakAuras.lua")]
    [InlineData("Questie\\..\\WeakAuras\\WeakAuras.lua")]
    [InlineData("Questie/./Questie.lua")]
    [InlineData("Questie//Questie.lua")]
    [InlineData("Questie/C:/Questie.lua")]
    [InlineData("/WeakAuras/WeakAuras.lua")]
    public async Task InstallAsync_ZipEntryWithUnsafePath_IsRefused_AndNothingWritten(string entry)
    {
        var (updater, _, addOnsPath, sha256) = await QuestieSetupAsync("Questie/Questie.toc", entry);
        WriteInstalled(addOnsPath, "Questie", "Questie.toc");
        WriteInstalled(addOnsPath, "WeakAuras", "WeakAuras.toc");
        var release = new AddonRelease("v2", QuestieZip.ToString(), sha256, 0, DateTimeOffset.UtcNow, Folders: ["Questie"]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => updater.InstallAsync(Questie, "release", release, addOnsPath, null, TestContext.Current.CancellationToken));

        Assert.True(File.Exists(Path.Combine(addOnsPath, "Questie", "stale.lua")));
        Assert.False(File.Exists(Path.Combine(addOnsPath, "WeakAuras", "WeakAuras.lua")));
        Assert.True(File.Exists(Path.Combine(addOnsPath, "WeakAuras", "stale.lua")));
    }

    [Fact]
    public void ExtractZip_Rejects_DotSegmentEntry()
    {
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        Directory.CreateDirectory(addOnsPath);
        var zipPath = CreateZip(a => WriteEntry(a, "Questie/../WeakAuras/WeakAuras.lua", "nope"));

        Assert.Throws<InvalidOperationException>(() => AddonUpdater.ExtractZip(zipPath, addOnsPath));
        Assert.False(Directory.Exists(Path.Combine(addOnsPath, "WeakAuras")));
    }

    [Fact]
    public async Task InstallAsync_CurseForgeZipWithForeignFolder_IsRefused_AndNothingWritten()
    {
        var (updater, handler, addOnsPath, _) = await QuestieSetupAsync("Questie/Questie.toc", "HoobiScripts/HoobiScripts.toc");
        WriteInstalled(addOnsPath, "Questie", "Questie.toc");
        WriteInstalled(addOnsPath, "HoobiScripts", "HoobiScripts.toc");
        var release = new AddonRelease("v2", QuestieZip.ToString(), null, handler.Size, DateTimeOffset.UtcNow, Folders: ["Questie"]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => updater.InstallAsync(CurseForgeQuestie, "release", release, addOnsPath, null, TestContext.Current.CancellationToken, ["hoobiscripts"]));

        Assert.Contains("HoobiScripts", ex.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(addOnsPath, "Questie", "stale.lua")));
        Assert.True(File.Exists(Path.Combine(addOnsPath, "HoobiScripts", "stale.lua")));
        Assert.Equal("## Version: 1", File.ReadAllText(Path.Combine(addOnsPath, "HoobiScripts", "HoobiScripts.toc")));
    }

    [Fact]
    public async Task InstallAsync_ZipWithRootFile_IsRefused()
    {
        var (updater, _, addOnsPath, sha256) = await QuestieSetupAsync("Questie/Questie.toc", "readme.txt");
        var release = new AddonRelease("v2", QuestieZip.ToString(), sha256, 0, DateTimeOffset.UtcNow, Folders: ["Questie"]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => updater.InstallAsync(Questie, "release", release, addOnsPath, null, TestContext.Current.CancellationToken));

        Assert.False(File.Exists(Path.Combine(addOnsPath, "readme.txt")));
    }

    [Fact]
    public async Task InstallAsync_OneFolderFailsTocGuard_DeletesNone()
    {
        var (updater, _, addOnsPath, sha256) = await QuestieSetupAsync("QuestieDB/QuestieDB.toc", "Questie/Questie.toc");
        WriteInstalled(addOnsPath, "Questie", "Questie.toc");
        WriteInstalled(addOnsPath, "QuestieDB", "notes.txt");
        var release = new AddonRelease("v2", QuestieZip.ToString(), sha256, 0, DateTimeOffset.UtcNow, Folders: ["Questie", "QuestieDB"]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => updater.InstallAsync(Questie, "release", release, addOnsPath, null, TestContext.Current.CancellationToken));

        Assert.True(File.Exists(Path.Combine(addOnsPath, "Questie", "stale.lua")));
        Assert.True(File.Exists(Path.Combine(addOnsPath, "QuestieDB", "stale.lua")));
    }

    [Fact]
    public async Task InstallAsync_NonDistributable_IsRefused_WithoutDownloading()
    {
        var (updater, handler, addOnsPath, sha256) = await QuestieSetupAsync("Questie/Questie.toc");
        var release = new AddonRelease("v2", QuestieZip.ToString(), sha256, 0, DateTimeOffset.UtcNow, Website: "https://www.curseforge.com/wow/addons/dbm", Distributable: false);

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
        WriteInstalled(addOnsPath, "QuestieDB", "Questie_Forever.toc");

        Assert.Throws<InvalidOperationException>(() => AddonUpdater.Uninstall(addOnsPath, ["Questie", "QuestieDB"]));

        Assert.True(Directory.Exists(Path.Combine(addOnsPath, "Questie")));
        Assert.True(Directory.Exists(Path.Combine(addOnsPath, "QuestieDB")));
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private static readonly ManagedAddon CurseForgeQuestie = new(
        "curseforge-334372-88568", "Questie", "https://steward.example/api/addons/curseforge/334372/88568/", Source: CurseForgeAddons.Source);

    private static readonly AddonRelease QuestieRelease = new("v2", QuestieZip.ToString(), new string('0', 64), 1, DateTimeOffset.UtcNow);

    private static (AddonUpdater Updater, List<CurseForgeDownloadFailure> Reports, SemaphoreSlim Signal) ReportingUpdater(
        HttpMessageHandler handler, Func<CurseForgeDownloadFailure, Task>? report = null)
    {
        var reports = new List<CurseForgeDownloadFailure>();
        var signal = new SemaphoreSlim(0);
        var updater = new AddonUpdater(new HttpClient(handler), reportDownloadFailure: async (failure, _) =>
        {
            lock (reports)
            {
                reports.Add(failure);
            }

            signal.Release();
            if (report is not null)
            {
                await report(failure);
            }
        });
        return (updater, reports, signal);
    }

    [Fact]
    public async Task InstallAsync_CurseForgeZipForbidden_ReportsTheFailure()
    {
        var (updater, reports, signal) = ReportingUpdater(new StatusHandler(HttpStatusCode.Forbidden));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => updater.InstallAsync(CurseForgeQuestie, "release", QuestieRelease, _tempDir, null, TestContext.Current.CancellationToken));
        await signal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        var report = Assert.Single(reports);
        Assert.Equal(334372, report.ModId);
        Assert.Equal("v2", report.Version);
        Assert.Equal(QuestieZip.AbsoluteUri, report.Url);
        Assert.Equal(403, report.Status);
        Assert.Contains("403", report.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallAsync_CurseForgeSizeMismatch_ReportsWithNullStatus()
    {
        var (setupUpdater, handler, addOnsPath, _) = await QuestieSetupAsync("Questie/Questie.toc");
        _ = setupUpdater;
        var (updater, reports, signal) = ReportingUpdater(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => updater.InstallAsync(CurseForgeQuestie, "release", QuestieRelease, addOnsPath, null, TestContext.Current.CancellationToken));
        await signal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var report = Assert.Single(reports);
        Assert.Null(report.Status);
        Assert.StartsWith("Size mismatch", report.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallAsync_CurseForgeWithoutHash_InstallsWhenTheSizeMatches()
    {
        var (updater, handler, addOnsPath, _) = await QuestieSetupAsync("Questie/Questie.toc");
        var release = new AddonRelease("v2", QuestieZip.ToString(), null, handler.Size, DateTimeOffset.UtcNow, Folders: ["Questie"]);

        var folders = await updater.InstallAsync(CurseForgeQuestie, "release", release, addOnsPath, null, TestContext.Current.CancellationToken);

        Assert.Equal(["Questie"], folders);
        Assert.True(File.Exists(Path.Combine(addOnsPath, "Questie", "Questie.toc")));
    }

    [Fact]
    public async Task InstallAsync_CurseForgeZipFailingItsCrc_IsRefused_AndNothingDeleted()
    {
        var zipPath = CreateZip(a =>
        {
            var entry = a.CreateEntry("Questie/Questie.toc", CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("## Version: 2 ORIGINAL");
        });
        var zipBytes = await File.ReadAllBytesAsync(zipPath, TestContext.Current.CancellationToken);
        var payload = System.Text.Encoding.ASCII.GetBytes("ORIGINAL");
        var at = zipBytes.AsSpan().IndexOf(payload);
        System.Text.Encoding.ASCII.GetBytes("TAMPERED").CopyTo(zipBytes, at);
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        WriteInstalled(addOnsPath, "Questie", "Questie.toc");
        var updater = new AddonUpdater(new HttpClient(new ZipBytesStubHandler(QuestieZip, zipBytes)));
        var release = new AddonRelease("v2", QuestieZip.ToString(), null, zipBytes.Length, DateTimeOffset.UtcNow, Folders: ["Questie"]);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => updater.InstallAsync(CurseForgeQuestie, "release", release, addOnsPath, null, TestContext.Current.CancellationToken));

        Assert.True(File.Exists(Path.Combine(addOnsPath, "Questie", "stale.lua")));
    }

    [Fact]
    public async Task InstallAsync_DirectRelease_TakesTheZipFoldersThatHoldAToc()
    {
        var (updater, handler, addOnsPath, _) = await QuestieSetupAsync("Questie/Questie.toc", "QuestieDB/QuestieDB.toc", "QuestieDB/Data/db.lua");
        WriteInstalled(addOnsPath, "Questie", "Questie.toc");
        var release = new AddonRelease("v2", QuestieZip.ToString(), null, handler.Size, DateTimeOffset.UtcNow) { Direct = true };

        var folders = await updater.InstallAsync(CurseForgeQuestie, "release", release, addOnsPath, null, TestContext.Current.CancellationToken);

        Assert.Equal(["Questie", "QuestieDB"], folders);
        Assert.True(File.Exists(Path.Combine(addOnsPath, "QuestieDB", "Data", "db.lua")));
        Assert.False(File.Exists(Path.Combine(addOnsPath, "Questie", "stale.lua")));
    }

    [Fact]
    public async Task InstallAsync_DirectReleaseShippingAForeignFolder_IsRefused()
    {
        var (updater, handler, addOnsPath, _) = await QuestieSetupAsync("Questie/Questie.toc", "HoobiScripts/HoobiScripts.toc");
        WriteInstalled(addOnsPath, "HoobiScripts", "HoobiScripts.toc");
        var release = new AddonRelease("v2", QuestieZip.ToString(), null, handler.Size, DateTimeOffset.UtcNow) { Direct = true };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => updater.InstallAsync(CurseForgeQuestie, "release", release, addOnsPath, null, TestContext.Current.CancellationToken, ["HoobiScripts"]));

        Assert.True(File.Exists(Path.Combine(addOnsPath, "HoobiScripts", "stale.lua")));
    }

    [Fact]
    public async Task InstallAsync_NonCurseForgeAddon_ReportsNothing()
    {
        var (updater, reports, signal) = ReportingUpdater(new StatusHandler(HttpStatusCode.Forbidden));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => updater.InstallAsync(Questie, "release", QuestieRelease, _tempDir, null, TestContext.Current.CancellationToken));

        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken));
        Assert.Empty(reports);
    }

    [Fact]
    public async Task InstallAsync_SameAddonAndVersionFailingTwice_ReportsOnce()
    {
        var (updater, reports, signal) = ReportingUpdater(new StatusHandler(HttpStatusCode.Forbidden));

        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<HttpRequestException>(
                () => updater.InstallAsync(CurseForgeQuestie, "release", QuestieRelease, _tempDir, null, TestContext.Current.CancellationToken));
        }

        await signal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken));
        Assert.Single(reports);
    }

    [Fact]
    public async Task InstallAsync_ReportItselfFailing_LeavesTheInstallFailureUnchanged()
    {
        var (updater, _, signal) = ReportingUpdater(
            new StatusHandler(HttpStatusCode.Forbidden), _ => throw new SessionExpiredException());

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => updater.InstallAsync(CurseForgeQuestie, "release", QuestieRelease, _tempDir, null, TestContext.Current.CancellationToken));
        await signal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    private static readonly ManagedAddon ProtectedScripts = new(
        "hoobiscripts", "HoobiScripts", "https://api.hoobi.io/guild/api/addons/protected/hoobiscripts/", Source: AddonCatalogue.ProtectedSource);

    [Theory]
    [InlineData("Protected", null, true)]
    [InlineData("CurseForge", null, true)]
    [InlineData("Steward", null, false)]
    [InlineData("Steward", "session", true)]
    [InlineData("Protected", "none", false)]
    public async Task GetLatestAsync_UsesTheSessionClientOnlyWhenTheManifestNeedsIt(string source, string? manifestAuth, bool session)
    {
        var addon = Questie with { Source = source, ManifestAuth = manifestAuth };
        var updater = new AddonUpdater(
            new HttpClient(new StatusHandler(HttpStatusCode.NotFound)), sessionClient: new HttpClient(new StatusHandler(HttpStatusCode.Unauthorized)));

        Func<Task> fetch = () => updater.GetLatestAsync(addon, "release", TestContext.Current.CancellationToken);

        if (session)
        {
            await Assert.ThrowsAsync<SessionExpiredException>(fetch);
        }
        else
        {
            await Assert.ThrowsAsync<HttpRequestException>(fetch);
        }
    }

    [Fact]
    public void ZipUri_RelativeZipOnTheStaticSite_ResolvesAgainstTheManifest()
    {
        Assert.Equal(
            "https://addon.hoobi.io/questie/questie-1.0.zip",
            AddonUpdater.ZipUri(Questie with { ManifestBaseUrl = "https://addon.hoobi.io/questie/" }, "release", "questie-1.0.zip").AbsoluteUri);
    }

    [Fact]
    public void ZipUri_AbsoluteZip_IsUsedAsGivenWhateverZipRelativeSays()
    {
        const string signed = "https://api.hoobi.io/guild/api/addons/protected/hoobiscripts/hoobiscripts-1.0.zip?t=1.2.abc";

        Assert.Equal(signed, AddonUpdater.ZipUri(ProtectedScripts, "release", signed).AbsoluteUri);
        Assert.Equal(signed, AddonUpdater.ZipUri(Questie with { ZipRelative = true }, "release", signed).AbsoluteUri);
    }

    [Fact]
    public void ZipUri_RelativeZipWhenNotZipRelative_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => AddonUpdater.ZipUri(ProtectedScripts, "release", "hoobiscripts-1.0.zip"));
        Assert.Throws<InvalidOperationException>(() => AddonUpdater.ZipUri(Questie with { ZipRelative = false }, "release", "questie-1.0.zip"));
    }
}
