namespace Steward.Core.Tests;

public sealed class LocalDataTests
{
    [Fact]
    public void Folder_IsUnderLocalApplicationData_OnWindows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steward"),
            LocalData.Folder);
    }

    [Fact]
    public void Folder_IsAbsoluteAndCreated_WhenTheDataHomeDoesNotExistYet()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");
        var dataHome = Path.Combine(Directory.CreateTempSubdirectory("steward-xdg-").FullName, "missing", "share");
        var previous = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", dataHome);
        try
        {
            Assert.Equal(Path.Combine(dataHome, "Steward"), LocalData.Folder);
            Assert.True(Directory.Exists(dataHome));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", previous);
        }
    }
}
