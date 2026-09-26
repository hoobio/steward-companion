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

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AddonUpdater.VerifyChecksumAsync(filePath, new string('0', 64), TestContext.Current.CancellationToken));

        Assert.False(File.Exists(filePath));
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
}
