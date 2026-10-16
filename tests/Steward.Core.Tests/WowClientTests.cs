namespace Steward.Core.Tests;

public sealed class WowClientTests
{
    public static TheoryData<string, string, bool> FolderSegmentCases()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var (file, folder, expected) in new[]
        {
            (@"C:\Games\WoW\_classic_beta_\WowClassic.exe", @"C:\Games\WoW\_classic_beta_", true),
            (@"C:\Games\WoW\_retail_\Wow.exe", @"C:\Games\WoW\_classic_beta_", false),
            (@"C:\Games\WoW\_classic_beta_\WowClassic.exe", @"C:\Games\WoW\_classic_", false),
            (@"C:\Games\WoW\_classic_\WowClassic.exe", @"C:\Games\WoW\_classic_beta_", false),
            (@"c:\games\wow\_retail_\wow.exe", @"C:\Games\WoW\_RETAIL_", true),
            (@"C:\Games\WoW\_retail_\Wow.exe", @"C:\Games\WoW\_retail_\", true),
            (@"C:\Games\WoW\_retail_", @"C:\Games\WoW\_retail_", false),
        })
        {
            data.Add(Native(file), Native(folder), expected);
        }

        return data;
    }

    private static string Native(string windowsPath) =>
        OperatingSystem.IsWindows() ? windowsPath : "/" + windowsPath[3..].Replace('\\', '/');

    [Theory]
    [MemberData(nameof(FolderSegmentCases))]
    public void IsUnder_MatchesWholeFolderSegments(string filePath, string folderPath, bool expected)
    {
        Assert.Equal(expected, WowClient.IsUnder(filePath, folderPath));
    }

    [Fact]
    public void WineClients_MapsTheClientPathThroughItsPrefix()
    {
        var root = Directory.CreateTempSubdirectory("steward-proc-").FullName;
        try
        {
            var prefix = Path.Combine(root, "Games", "battlenet");
            var steam = Path.Combine(root, "compatdata", "123");
            WriteProcess(root, 100, ["/opt/wine/bin/wine", @"C:\Program Files (x86)\World of Warcraft\_forever_\Wow.exe", "-launch"], $"HOME={root}\0WINEPREFIX={prefix}\0");
            WriteProcess(root, 200, ["wine", @"c:\program files\world of warcraft\_classic_beta_\WowClassic.exe"], $"STEAM_COMPAT_DATA_PATH={steam}\0");
            WriteProcess(root, 300, ["wine", @"Z:\home\u\wow\_forever_\Wow.exe"], string.Empty);
            WriteProcess(root, 400, ["wine", @"C:\Program Files (x86)\Battle.net\Battle.net.exe"], $"WINEPREFIX={prefix}\0");
            WriteProcess(root, 500, ["wine", @"C:\wow\_forever_\Wow.exe"], string.Empty);
            Directory.CreateDirectory(Path.Combine(root, "self"));

            var clients = WowClient.WineClients(root, Path.Combine(root, "home")).OrderBy(client => client.ProcessId).ToList();

            Assert.Equal(
                [
                    (Path.Combine(prefix, "drive_c", "Program Files (x86)", "World of Warcraft", "_forever_", "Wow.exe"), 100),
                    (Path.Combine(steam, "pfx", "drive_c", "program files", "world of warcraft", "_classic_beta_", "WowClassic.exe"), 200),
                    (Path.Combine("/", "home", "u", "wow", "_forever_", "Wow.exe"), 300),
                    (Path.Combine(root, "home", ".wine", "drive_c", "wow", "_forever_", "Wow.exe"), 500),
                ],
                clients);
            Assert.True(WowClient.IsUnder(clients[0].FileName, Path.Combine(prefix, "drive_c", "Program Files (x86)", "World of Warcraft", "_forever_")));
            Assert.True(WowClient.IsUnder(clients[1].FileName, Path.Combine(steam, "pfx", "drive_c", "Program Files", "World of Warcraft", "_classic_beta_")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteProcess(string root, int processId, string[] arguments, string environment)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, processId.ToString(System.Globalization.CultureInfo.InvariantCulture))).FullName;
        File.WriteAllText(Path.Combine(folder, "cmdline"), string.Join('\0', arguments) + "\0\0\0");
        File.WriteAllText(Path.Combine(folder, "environ"), environment);
    }

    [Fact]
    public async Task WaitForExitAsync_ReturnsForUnknownProcessId()
    {
        await WowClient.WaitForExitAsync(int.MaxValue, CancellationToken.None);
    }

    [Fact]
    public void Find_ReturnsNull_WhenNoClientUnderTheFolder()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var install = new WowInstall(folder.FullName, "_retail_", folder.FullName, folder.FullName, null, null, "Retail");

            Assert.Null(WowClient.Find(install));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
