namespace Steward.Core.Tests;

public sealed class InstallVolumeTests
{
    private const string Install = "/mnt/games/World of Warcraft/_forever_";

    [Fact]
    public void AnInstallOnTheRootFilesystem_IsPresent()
    {
        Assert.True(InstallVolume.IsPresent("/home/hoobi/Games/battlenet/drive_c/World of Warcraft/_forever_", ["/", "/boot"], ["/", "/boot"]));
    }

    [Fact]
    public void AnInstallOnAMountedDisk_IsPresent()
    {
        Assert.True(InstallVolume.IsPresent(Install, ["/", "/mnt/games"], ["/", "/mnt/games"]));
    }

    [Fact]
    public void AnUnmountedFstabDisk_IsAbsent()
    {
        Assert.False(InstallVolume.IsPresent("/data/WoW/_forever_", ["/"], ["/", "/data"]));
    }

    [Theory]
    [InlineData("/mnt/games/World of Warcraft/_forever_")]
    [InlineData("/media/hoobi/GAMES/World of Warcraft/_forever_")]
    [InlineData("/run/media/hoobi/GAMES/World of Warcraft/_forever_")]
    public void AnUnmountedRemovableMediaDisk_IsAbsent(string path)
    {
        Assert.False(InstallVolume.IsPresent(path, ["/", "/boot"], ["/"]));
    }

    [Fact]
    public void MountPrefixes_MatchWholeSegments()
    {
        Assert.False(InstallVolume.IsPresent("/mnt/dd/WoW/_forever_", ["/", "/mnt/d"], ["/"]));
        Assert.False(InstallVolume.IsPresent("/data2/WoW/_forever_", ["/", "/data"], ["/", "/data2"]));
    }

    [Fact]
    public void ParseMountPoints_ReadsTheFieldAndUnescapesOctal()
    {
        string[] mountinfo =
        [
            "22 1 8:2 / / rw,relatime shared:1 - ext4 /dev/sda2 rw",
            @"98 22 8:17 / /mnt/my\040games rw,relatime shared:40 - ext4 /dev/sdb1 rw",
        ];
        string[] fstab =
        [
            "# <file system> <mount point> <type>",
            "UUID=1 / ext4 defaults 0 1",
            "UUID=2 none swap sw 0 0",
            "",
            "UUID=3\t/data/\text4\tnofail\t0\t2",
        ];

        Assert.Equal(["/", "/mnt/my games"], InstallVolume.ParseMountPoints(mountinfo, 4));
        Assert.Equal(["/", "/data"], InstallVolume.ParseMountPoints(fstab, 1));
    }
}
