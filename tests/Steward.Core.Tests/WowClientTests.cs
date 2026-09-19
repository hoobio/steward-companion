namespace Steward.Core.Tests;

public sealed class WowClientTests
{
    [Theory]
    [InlineData(@"C:\Games\WoW\_classic_beta_\WowClassic.exe", @"C:\Games\WoW\_classic_beta_", true)]
    [InlineData(@"C:\Games\WoW\_retail_\Wow.exe", @"C:\Games\WoW\_classic_beta_", false)]
    [InlineData(@"C:\Games\WoW\_classic_beta_\WowClassic.exe", @"C:\Games\WoW\_classic_", false)]
    [InlineData(@"C:\Games\WoW\_classic_\WowClassic.exe", @"C:\Games\WoW\_classic_beta_", false)]
    [InlineData(@"c:\games\wow\_retail_\wow.exe", @"C:\Games\WoW\_RETAIL_", true)]
    [InlineData(@"C:\Games\WoW\_retail_\Wow.exe", @"C:\Games\WoW\_retail_\", true)]
    [InlineData(@"C:\Games\WoW\_retail_", @"C:\Games\WoW\_retail_", false)]
    public void IsUnder_MatchesWholeFolderSegments(string filePath, string folderPath, bool expected)
    {
        Assert.Equal(expected, WowClient.IsUnder(filePath, folderPath));
    }
}
