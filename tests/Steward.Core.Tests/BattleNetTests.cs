namespace Steward.Core.Tests;

public sealed class BattleNetTests : IDisposable
{
    private readonly string _cache = Directory.CreateTempSubdirectory("steward-bnet-cache-").FullName;

    public void Dispose() => Directory.Delete(_cache, recursive: true);

    [Fact]
    public void FamilyDefaults_ReadsTheNamedFamilysDefaultProductTypeFromEveryRegion()
    {
        Directory.CreateDirectory(Path.Combine(_cache, "1a", "68"));
        File.WriteAllText(
            Path.Combine(_cache, "1a", "68", "entry"),
            "{\"families\":[{\"base\":{\"default_product_type\":\"wow_classic\",\"name\":\"x\"},\"id\":\"WoWC\"},"
            + "{\"base\":{\"badges\":[{\"id\":\"b\"}],\"default_product_type\":\"wow_classic_beta\",\"starter_items\":[{\"action\":{\"type\":\"url\"},\"id\":\"website\"}]},\"id\":\"WoWF\"}]}");
        File.WriteAllText(Path.Combine(_cache, "cn"), "{\"base\":{\"default_product_type\":\"wow_cn_beta\"},\"id\":\"WoWF\"}");
        File.WriteAllBytes(Path.Combine(_cache, "image"), [0x89, 0x50, 0x4E, 0x47, 0x22, 0x69, 0x64]);

        Assert.Equal(["wow_classic_beta", "wow_cn_beta"], BattleNet.FamilyDefaults(_cache, "WoWF").Order(StringComparer.Ordinal));
        Assert.Equal(["wow_classic"], BattleNet.FamilyDefaults(_cache, "WoWC"));
        Assert.Empty(BattleNet.FamilyDefaults(_cache, "D3"));
        Assert.Empty(BattleNet.FamilyDefaults(Path.Combine(_cache, "missing"), "WoWF"));
    }
}
