namespace Steward.Core.Tests;

public sealed class AvatarTgaTests
{
    private static AvatarImage Image(int width, int height, byte[] bgra) =>
        new("https://cdn.discordapp.com/avatars/1/hash.png?size=64", width, height, bgra);

    [Fact]
    public void Encode_ProducesTheHeaderTheClientAccepts()
    {
        var encoded = AvatarTga.Encode(Image(2, 2, new byte[2 * 2 * 4]));

        Assert.Equal(
            new byte[] { 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 2, 0, 32, 8 },
            encoded[..18]);
        Assert.Equal(18 + (2 * 2 * 4), encoded.Length);
    }

    [Fact]
    public void Encode_WritesTheRowsBottomUp()
    {
        byte[] bgra =
        [
            1, 2, 3, 4, 5, 6, 7, 8,
            9, 10, 11, 12, 13, 14, 15, 16,
        ];

        var encoded = AvatarTga.Encode(Image(2, 2, bgra));

        Assert.Equal(new byte[] { 9, 10, 11, 12, 13, 14, 15, 16 }, encoded[18..26]);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, encoded[26..34]);
    }

    [Fact]
    public void Encode_MatchesTheAddonIconHeader()
    {
        var encoded = AvatarTga.Encode(Image(64, 64, new byte[64 * 64 * 4]));

        using var icon = typeof(StewardGuidesAddon).Assembly
            .GetManifestResourceStream("Steward.Core.Assets.Icon.tga")!;
        var header = new byte[18];
        icon.ReadExactly(header);

        Assert.Equal(header, encoded[..18]);
    }

    [Theory]
    [InlineData(48, 64)]
    [InlineData(64, 100)]
    [InlineData(0, 0)]
    public void Encode_RejectsANonPowerOfTwoSize(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AvatarTga.Encode(Image(width, height, new byte[Math.Max(width * height * 4, 0)])));
    }

    [Fact]
    public void Encode_RejectsAPixelBufferThatDoesNotMatchTheSize()
    {
        Assert.Throws<ArgumentException>(() => AvatarTga.Encode(Image(4, 4, new byte[8])));
    }
}
