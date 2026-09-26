namespace Steward.Core.Tests;

public sealed class CharacterSyncGatingTests
{
    [Fact]
    public void IsAuthorizing_IsFalse_WhenSyncIsTheOnlyFeature()
    {
        var features = new HashSet<string>([GigagrugClient.SyncFeature], StringComparer.Ordinal);

        Assert.False(GigagrugClient.IsAuthorizing(features));
    }

    [Fact]
    public void IsAuthorizing_IsTrue_WhenSyncIsHeldAlongsideAnAuthorizingFeature()
    {
        var features = new HashSet<string>(
            [GigagrugClient.SyncFeature, GigagrugClient.StewardFeature], StringComparer.Ordinal);

        Assert.True(GigagrugClient.IsAuthorizing(features));
    }

    [Fact]
    public void IsAuthorizing_IsFalse_WhenFeatureSetIsEmpty()
    {
        Assert.False(GigagrugClient.IsAuthorizing(new HashSet<string>(StringComparer.Ordinal)));
    }

    [Fact]
    public void ShouldPush_IsTrue_WhenThereIsNoPriorPush()
    {
        Assert.True(CharacterPushGate.ShouldPush("abc", new Dictionary<string, CharacterPushRecord>(), "install"));
    }

    [Fact]
    public void ShouldPush_IsFalse_WhenFingerprintUnchanged()
    {
        var last = new Dictionary<string, CharacterPushRecord>
        {
            ["install"] = new CharacterPushRecord("abc", DateTimeOffset.UnixEpoch, 3),
        };

        Assert.False(CharacterPushGate.ShouldPush("abc", last, "install"));
    }

    [Fact]
    public void ShouldPush_IsTrue_WhenFingerprintChanged()
    {
        var last = new Dictionary<string, CharacterPushRecord>
        {
            ["install"] = new CharacterPushRecord("abc", DateTimeOffset.UnixEpoch, 3),
        };

        Assert.True(CharacterPushGate.ShouldPush("def", last, "install"));
    }

    [Fact]
    public void ShouldPush_IsFalse_WhenThereIsNothingToPush()
    {
        var last = new Dictionary<string, CharacterPushRecord>
        {
            ["install"] = new CharacterPushRecord("abc", DateTimeOffset.UnixEpoch, 3),
        };

        Assert.False(CharacterPushGate.ShouldPush(null, last, "install"));
    }
}
