using System.IO.Compression;
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
    public void RemoveExistingInstall_Throws_WhenFolderIsAGitWorkingTree()
    {
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        var existing = Path.Combine(addOnsPath, FolderName);
        Directory.CreateDirectory(Path.Combine(existing, ".git"));
        File.WriteAllText(Path.Combine(existing, $"{FolderName}.toc"), "## Version: 0.9.0");

        Assert.Throws<InvalidOperationException>(() => AddonUpdater.RemoveExistingInstall(addOnsPath, FolderName));
        Assert.True(Directory.Exists(existing));
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
}
