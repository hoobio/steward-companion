namespace Steward.Core.Tests;

public sealed class AddonIconTests
{
    [Fact]
    public void DecodeTga_RoundTripsAnImageEncodedByAvatarTga()
    {
        byte[] bgra =
        [
            10, 20, 30, 255, 40, 50, 60, 255,
            70, 80, 90, 128, 100, 110, 120, 0,
        ];
        var encoded = AvatarTga.Encode(new AvatarImage("https://cdn.discordapp.com/avatars/1/hash.png?size=64", 2, 2, bgra));

        var decoded = AddonIcon.DecodeTga(encoded);

        Assert.Equal(2, decoded.Width);
        Assert.Equal(2, decoded.Height);
        Assert.Equal(bgra, decoded.Bgra);
    }

    [Fact]
    public void DecodeTga_DecodesA2x2BottomUpUncompressedImage()
    {
        var header = new byte[18];
        header[2] = 2;
        header[12] = 2;
        header[14] = 2;
        header[16] = 32;

        byte[] bottomRow = [1, 2, 3, 255, 4, 5, 6, 255];
        byte[] topRow = [7, 8, 9, 255, 10, 11, 12, 255];
        var data = header.Concat(bottomRow).Concat(topRow).ToArray();

        var decoded = AddonIcon.DecodeTga(data);

        Assert.Equal(topRow, decoded.Bgra[..8]);
        Assert.Equal(bottomRow, decoded.Bgra[8..]);
    }

    [Fact]
    public void DecodeTga_DecodesARleEncodedImage()
    {
        var header = new byte[18];
        header[2] = 10;
        header[12] = 5;
        header[14] = 1;
        header[16] = 32;
        header[17] = 0x20;

        byte[] pixelData =
        [
            0x83, 1, 2, 3, 4,
            0x00, 5, 6, 7, 8,
        ];
        var data = header.Concat(pixelData).ToArray();

        var decoded = AddonIcon.DecodeTga(data);

        Assert.Equal(5, decoded.Width);
        Assert.Equal(1, decoded.Height);
        Assert.Equal(
            new byte[] { 1, 2, 3, 4, 1, 2, 3, 4, 1, 2, 3, 4, 1, 2, 3, 4, 5, 6, 7, 8 },
            decoded.Bgra[..20]);
    }

    [Fact]
    public void DecodeBlp_ThrowsForABlp1Header()
    {
        var data = new byte[BlpHeaderLength];
        "BLP1"u8.CopyTo(data);

        Assert.Throws<FormatException>(() => AddonIcon.DecodeBlp(data));
    }

    [Fact]
    public void DecodeBlp_DecodesAPalettizedImage()
    {
        const int width = 2;
        const int height = 1;
        var palette = new byte[256 * 4];
        palette[0] = 10;
        palette[1] = 20;
        palette[2] = 30;
        palette[3] = 40;
        palette[4] = 50;
        palette[5] = 60;
        palette[6] = 70;
        palette[7] = 80;

        byte[] mip = [0, 1, 200, 210];
        var data = BuildBlp2(compression: 1, alphaDepth: 8, alphaType: 0, width, height, palette, mip);

        var decoded = AddonIcon.DecodeBlp(data);

        Assert.Equal(new byte[] { 10, 20, 30, 200, 50, 60, 70, 210 }, decoded.Bgra);
    }

    [Fact]
    public void DecodeBlp_DecodesAnUncompressedImage()
    {
        const int width = 2;
        const int height = 1;
        byte[] mip = [1, 2, 3, 4, 5, 6, 7, 8];
        var data = BuildBlp2(compression: 3, alphaDepth: 8, alphaType: 0, width, height, new byte[256 * 4], mip);

        var decoded = AddonIcon.DecodeBlp(data);

        Assert.Equal(mip, decoded.Bgra);
    }

    [Fact]
    public void DecodeBlp_DecodesADxt1Block()
    {
        const int width = 4;
        const int height = 4;

        ushort color0 = 0b11111_000000_00000;
        ushort color1 = 0b00000_000000_11111;
        uint indices = 0;
        for (var i = 0; i < 16; i++)
        {
            indices |= (uint)((i % 2 == 0 ? 0 : 1) << (i * 2));
        }

        byte[] mip =
        [
            (byte)(color0 & 0xFF), (byte)(color0 >> 8),
            (byte)(color1 & 0xFF), (byte)(color1 >> 8),
            (byte)(indices & 0xFF), (byte)((indices >> 8) & 0xFF), (byte)((indices >> 16) & 0xFF), (byte)((indices >> 24) & 0xFF),
        ];
        var data = BuildBlp2(compression: 2, alphaDepth: 0, alphaType: 0, width, height, new byte[256 * 4], mip);

        var decoded = AddonIcon.DecodeBlp(data);

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, decoded.Bgra[..4]);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, decoded.Bgra[4..8]);
    }

    [Fact]
    public void TryLoad_ResolvesAnExtensionlessIconTextureToATgaFile()
    {
        var addOnsPath = CreateAddOnsDir();
        var folder = Path.Combine(addOnsPath, "Foo");
        Directory.CreateDirectory(Path.Combine(folder, "Media"));
        File.WriteAllText(
            Path.Combine(folder, "Foo.toc"),
            "## IconTexture: Interface\\AddOns\\Foo\\Media\\icon\r\n");
        File.WriteAllBytes(Path.Combine(folder, "Media", "icon.tga"), MakeSolidTga());

        var decoded = AddonIcon.TryLoad(addOnsPath, "Foo");

        Assert.NotNull(decoded);
        Assert.Equal(1, decoded!.Width);
    }

    [Fact]
    public void TryLoad_ResolvesForwardSlashesAndMixedCase()
    {
        var addOnsPath = CreateAddOnsDir();
        var folder = Path.Combine(addOnsPath, "Foo");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "Foo.toc"),
            "## IconTexture: interface/addons/FOO/ICON.TGA\r\n");
        File.WriteAllBytes(Path.Combine(folder, "ICON.TGA"), MakeSolidTga());

        var decoded = AddonIcon.TryLoad(addOnsPath, "Foo");

        Assert.NotNull(decoded);
    }

    [Fact]
    public void TryLoad_ReturnsNullForADotDotEscape()
    {
        var addOnsPath = CreateAddOnsDir();
        var folder = Path.Combine(addOnsPath, "Foo");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "Foo.toc"),
            "## IconTexture: Interface\\AddOns\\..\\..\\secret.tga\r\n");

        Assert.Null(AddonIcon.TryLoad(addOnsPath, "Foo"));
    }

    [Fact]
    public void TryLoad_ReturnsNullWhenTheDirectiveIsMissing()
    {
        var addOnsPath = CreateAddOnsDir();
        var folder = Path.Combine(addOnsPath, "Foo");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Foo.toc"), "## Title: Foo\r\n");

        Assert.Null(AddonIcon.TryLoad(addOnsPath, "Foo"));
    }

    [Fact]
    public void TryLoad_ReturnsNullForACorruptFile()
    {
        var addOnsPath = CreateAddOnsDir();
        var folder = Path.Combine(addOnsPath, "Foo");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "Foo.toc"),
            "## IconTexture: Interface\\AddOns\\Foo\\icon.tga\r\n");
        File.WriteAllBytes(Path.Combine(folder, "icon.tga"), [1, 2, 3]);

        Assert.Null(AddonIcon.TryLoad(addOnsPath, "Foo"));
    }

    private const int BlpHeaderLength = 4 + 4 + 4 + 4 + 4 + (16 * 4) + (16 * 4) + (256 * 4);

    private static byte[] BuildBlp2(
        byte compression, byte alphaDepth, byte alphaType, int width, int height, byte[] palette, byte[] mip)
    {
        var data = new byte[BlpHeaderLength + mip.Length];
        "BLP2"u8.CopyTo(data);
        data[4] = 1;
        data[8] = compression;
        data[9] = alphaDepth;
        data[10] = alphaType;
        BitConverter.GetBytes((uint)width).CopyTo(data, 12);
        BitConverter.GetBytes((uint)height).CopyTo(data, 16);
        BitConverter.GetBytes((uint)BlpHeaderLength).CopyTo(data, 20);
        BitConverter.GetBytes((uint)mip.Length).CopyTo(data, 84);
        palette.CopyTo(data, 4 + 4 + 4 + 4 + 4 + (16 * 4) + (16 * 4));
        mip.CopyTo(data, BlpHeaderLength);
        return data;
    }

    private static byte[] MakeSolidTga()
    {
        var header = new byte[18];
        header[2] = 2;
        header[12] = 1;
        header[14] = 1;
        header[16] = 32;
        header[17] = 0x20;
        return header.Concat(new byte[] { 1, 2, 3, 255 }).ToArray();
    }

    private static string CreateAddOnsDir()
    {
        var flavourPath = Path.Combine(Path.GetTempPath(), "Steward.Core.Tests", Guid.NewGuid().ToString("N"));
        var addOnsPath = Path.Combine(flavourPath, "Interface", "AddOns");
        Directory.CreateDirectory(addOnsPath);
        return addOnsPath;
    }
}
