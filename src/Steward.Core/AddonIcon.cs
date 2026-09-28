using System.Buffers.Binary;

namespace Steward.Core;

public sealed record DecodedImage(int Width, int Height, byte[] Bgra);

public static class AddonIcon
{
    public static DecodedImage? TryLoad(string addOnsPath, string folderName)
    {
        ArgumentNullException.ThrowIfNull(addOnsPath);
        ArgumentNullException.ThrowIfNull(folderName);

        try
        {
            var directive = ReadIconDirective(addOnsPath, folderName);
            if (string.IsNullOrEmpty(directive))
            {
                return null;
            }

            var resolved = Resolve(addOnsPath, directive);
            if (resolved is null)
            {
                return null;
            }

            foreach (var candidate in Candidates(resolved))
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                var bytes = File.ReadAllBytes(candidate);
                return Path.GetExtension(candidate).Equals(".blp", StringComparison.OrdinalIgnoreCase)
                    ? DecodeBlp(bytes)
                    : DecodeTga(bytes);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }

    private static string? ReadIconDirective(string addOnsPath, string folderName)
    {
        var addonDir = Path.Combine(addOnsPath, folderName);
        var tocPath = Path.Combine(addonDir, folderName + ".toc");
        var directive = TocFile.ReadDirective(tocPath, "IconTexture");
        if (directive is not null || !Directory.Exists(addonDir))
        {
            return directive;
        }

        var fallback = Directory.GetFiles(addonDir, $"{folderName}_*.toc")
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .FirstOrDefault();

        return fallback is null ? null : TocFile.ReadDirective(fallback, "IconTexture");
    }

    private static string? Resolve(string addOnsPath, string directive)
    {
        var relative = directive.Replace('/', '\\').TrimStart('\\');
        var flavourPath = Path.GetDirectoryName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(addOnsPath)));
        if (flavourPath is null)
        {
            return null;
        }

        var addOnsRoot = Path.GetFullPath(Path.TrimEndingDirectorySeparator(addOnsPath) + Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(flavourPath, relative));
        return resolved.StartsWith(addOnsRoot, StringComparison.OrdinalIgnoreCase) ? resolved : null;
    }

    private static IEnumerable<string> Candidates(string resolved)
    {
        if (Path.GetExtension(resolved).Length > 0)
        {
            yield return resolved;
        }
        else
        {
            yield return resolved + ".blp";
            yield return resolved + ".tga";
        }
    }

    public static DecodedImage DecodeTga(ReadOnlySpan<byte> data)
    {
        if (data.Length < AvatarTga.HeaderLength)
        {
            throw new FormatException("TGA file is shorter than the header");
        }

        var idLength = data[0];
        var imageType = data[2];
        var width = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        var bpp = data[16];
        var descriptor = data[17];

        if (imageType != 2 && imageType != 10)
        {
            throw new FormatException($"unsupported TGA image type {imageType}");
        }

        if (bpp != 24 && bpp != 32)
        {
            throw new FormatException($"unsupported TGA bit depth {bpp}");
        }

        var bytesPerPixel = bpp / 8;
        var pixelData = data[(AvatarTga.HeaderLength + idLength)..];
        var stride = width * 4;
        var bgra = new byte[stride * height];

        if (imageType == 2)
        {
            var rowStride = width * bytesPerPixel;
            if (pixelData.Length < rowStride * height)
            {
                throw new FormatException("TGA pixel data is truncated");
            }

            for (var row = 0; row < height; row++)
            {
                var srcRow = pixelData.Slice(row * rowStride, rowStride);
                var dstRow = bgra.AsSpan(row * stride, stride);
                for (var col = 0; col < width; col++)
                {
                    CopyPixel(srcRow.Slice(col * bytesPerPixel, bytesPerPixel), dstRow.Slice(col * 4, 4));
                }
            }
        }
        else
        {
            DecodeTgaRle(pixelData, bytesPerPixel, width, height, bgra);
        }

        if ((descriptor & 0x20) == 0)
        {
            FlipVertically(bgra, width, height);
        }

        return new DecodedImage(width, height, bgra);
    }

    private static void DecodeTgaRle(ReadOnlySpan<byte> pixelData, int bytesPerPixel, int width, int height, byte[] bgra)
    {
        var pixelCount = width * height;
        var offset = 0;
        var written = 0;

        while (written < pixelCount)
        {
            if (offset >= pixelData.Length)
            {
                throw new FormatException("RLE TGA pixel data is truncated");
            }

            var header = pixelData[offset++];
            var count = (header & 0x7F) + 1;

            if ((header & 0x80) != 0)
            {
                if (offset + bytesPerPixel > pixelData.Length)
                {
                    throw new FormatException("RLE TGA pixel data is truncated");
                }

                var pixel = pixelData.Slice(offset, bytesPerPixel);
                offset += bytesPerPixel;

                for (var i = 0; i < count && written < pixelCount; i++, written++)
                {
                    CopyPixel(pixel, bgra.AsSpan(written * 4, 4));
                }
            }
            else
            {
                for (var i = 0; i < count && written < pixelCount; i++, written++)
                {
                    if (offset + bytesPerPixel > pixelData.Length)
                    {
                        throw new FormatException("RLE TGA pixel data is truncated");
                    }

                    CopyPixel(pixelData.Slice(offset, bytesPerPixel), bgra.AsSpan(written * 4, 4));
                    offset += bytesPerPixel;
                }
            }
        }
    }

    private static void CopyPixel(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        dst[0] = src[0];
        dst[1] = src[1];
        dst[2] = src[2];
        dst[3] = src.Length == 4 ? src[3] : (byte)255;
    }

    private static void FlipVertically(byte[] bgra, int width, int height)
    {
        var stride = width * 4;
        var row = new byte[stride];
        for (var top = 0; top < height / 2; top++)
        {
            var bottom = height - 1 - top;
            var topSpan = bgra.AsSpan(top * stride, stride);
            var bottomSpan = bgra.AsSpan(bottom * stride, stride);
            topSpan.CopyTo(row);
            bottomSpan.CopyTo(topSpan);
            row.CopyTo(bottomSpan);
        }
    }

    private const int BlpHeaderSize = 4 + 4 + 4 + 4 + 4 + (16 * 4) + (16 * 4);

    public static DecodedImage DecodeBlp(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || !data[..4].SequenceEqual("BLP2"u8))
        {
            throw new FormatException("only BLP2 is supported");
        }

        if (data.Length < BlpHeaderSize + (256 * 4))
        {
            throw new FormatException("BLP2 header is truncated");
        }

        var compression = data[8];
        var alphaDepth = data[9];
        var alphaType = data[10];
        var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);
        var mipOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[20..]);
        var mipSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[84..]);

        if (mipOffset <= 0 || mipSize <= 0 || mipOffset + mipSize > data.Length)
        {
            throw new FormatException("BLP2 mip level 0 is missing or truncated");
        }

        var mip = data.Slice(mipOffset, mipSize);
        var bgra = new byte[width * height * 4];

        switch (compression)
        {
            case 1:
                DecodePalettized(mip, data.Slice(BlpHeaderSize, 256 * 4), alphaDepth, width, height, bgra);
                break;
            case 2:
                DecodeDxt(mip, alphaType, alphaDepth, width, height, bgra);
                break;
            case 3:
                if (mip.Length < bgra.Length)
                {
                    throw new FormatException("BLP2 uncompressed pixel data is truncated");
                }

                mip[..bgra.Length].CopyTo(bgra);
                break;
            default:
                throw new FormatException($"unsupported BLP2 compression {compression}");
        }

        return new DecodedImage(width, height, bgra);
    }

    private static void DecodePalettized(
        ReadOnlySpan<byte> mip, ReadOnlySpan<byte> palette, byte alphaDepth, int width, int height, byte[] bgra)
    {
        var pixelCount = width * height;
        if (mip.Length < pixelCount)
        {
            throw new FormatException("BLP2 palettized index plane is truncated");
        }

        var indices = mip[..pixelCount];
        var alphaPlane = mip[pixelCount..];

        for (var i = 0; i < pixelCount; i++)
        {
            var entry = palette.Slice(indices[i] * 4, 4);
            var dst = bgra.AsSpan(i * 4, 4);
            dst[0] = entry[0];
            dst[1] = entry[1];
            dst[2] = entry[2];
            dst[3] = ReadPaletteAlpha(alphaPlane, alphaDepth, i);
        }
    }

    private static byte ReadPaletteAlpha(ReadOnlySpan<byte> alphaPlane, byte alphaDepth, int pixelIndex)
    {
        switch (alphaDepth)
        {
            case 8:
                return pixelIndex < alphaPlane.Length ? alphaPlane[pixelIndex] : (byte)255;
            case 4:
            {
                var byteIndex = pixelIndex / 2;
                if (byteIndex >= alphaPlane.Length)
                {
                    return 255;
                }

                var nibble = pixelIndex % 2 == 0 ? alphaPlane[byteIndex] & 0x0F : (alphaPlane[byteIndex] >> 4) & 0x0F;
                return (byte)(nibble * 17);
            }
            case 1:
            {
                var byteIndex = pixelIndex / 8;
                if (byteIndex >= alphaPlane.Length)
                {
                    return 255;
                }

                var bit = (alphaPlane[byteIndex] >> (pixelIndex % 8)) & 1;
                return bit == 1 ? (byte)255 : (byte)0;
            }
            default:
                return 255;
        }
    }

    private static void DecodeDxt(
        ReadOnlySpan<byte> mip, byte alphaType, byte alphaDepth, int width, int height, byte[] bgra)
    {
        var hasSeparateAlpha = alphaType != 0;
        var blockSize = hasSeparateAlpha ? 16 : 8;
        var blocksWide = (width + 3) / 4;
        var blocksHigh = (height + 3) / 4;
        var stride = width * 4;
        var offset = 0;
        Span<byte> explicitAlpha = stackalloc byte[16];
        Span<uint> pixels = stackalloc uint[16];

        for (var by = 0; by < blocksHigh; by++)
        {
            for (var bx = 0; bx < blocksWide; bx++)
            {
                if (offset + blockSize > mip.Length)
                {
                    throw new FormatException("BLP2 DXT data is truncated");
                }

                var block = mip.Slice(offset, blockSize);
                offset += blockSize;

                ReadOnlySpan<byte> colorBlock;

                if (!hasSeparateAlpha)
                {
                    colorBlock = block;
                }
                else if (alphaType == 1)
                {
                    DecodeDxt3Alpha(block[..8], explicitAlpha);
                    colorBlock = block[8..];
                }
                else
                {
                    DecodeDxt5Alpha(block[..8], explicitAlpha);
                    colorBlock = block[8..];
                }

                DecodeColorBlock(colorBlock, hasSeparateAlpha, pixels);

                for (var py = 0; py < 4; py++)
                {
                    var y = (by * 4) + py;
                    if (y >= height)
                    {
                        continue;
                    }

                    for (var px = 0; px < 4; px++)
                    {
                        var x = (bx * 4) + px;
                        if (x >= width)
                        {
                            continue;
                        }

                        var packed = pixels[(py * 4) + px];
                        var dst = bgra.AsSpan((y * stride) + (x * 4), 4);
                        dst[0] = (byte)(packed & 0xFF);
                        dst[1] = (byte)((packed >> 8) & 0xFF);
                        dst[2] = (byte)((packed >> 16) & 0xFF);
                        dst[3] = hasSeparateAlpha
                            ? explicitAlpha[(py * 4) + px]
                            : alphaDepth == 0 ? (byte)255 : (byte)((packed >> 24) & 0xFF);
                    }
                }
            }
        }
    }

    private static void DecodeColorBlock(ReadOnlySpan<byte> block, bool fourColorOnly, Span<uint> pixels)
    {
        var color0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
        var color1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        var indices = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);

        var (r0, g0, b0) = Rgb565(color0);
        var (r1, g1, b1) = Rgb565(color1);

        Span<(byte R, byte G, byte B, byte A)> palette = stackalloc (byte, byte, byte, byte)[4];
        palette[0] = (r0, g0, b0, 255);
        palette[1] = (r1, g1, b1, 255);

        if (fourColorOnly || color0 > color1)
        {
            palette[2] = ((byte)(((2 * r0) + r1) / 3), (byte)(((2 * g0) + g1) / 3), (byte)(((2 * b0) + b1) / 3), 255);
            palette[3] = ((byte)((r0 + (2 * r1)) / 3), (byte)((g0 + (2 * g1)) / 3), (byte)((b0 + (2 * b1)) / 3), 255);
        }
        else
        {
            palette[2] = ((byte)((r0 + r1) / 2), (byte)((g0 + g1) / 2), (byte)((b0 + b1) / 2), 255);
            palette[3] = (0, 0, 0, 0);
        }

        for (var i = 0; i < 16; i++)
        {
            var index = (int)((indices >> (i * 2)) & 0x3);
            var (r, g, b, a) = palette[index];
            pixels[i] = (uint)(b | (g << 8) | (r << 16) | (a << 24));
        }
    }

    private static (byte R, byte G, byte B) Rgb565(ushort value)
    {
        var r = (value >> 11) & 0x1F;
        var g = (value >> 5) & 0x3F;
        var b = value & 0x1F;
        return ((byte)((r * 255 + 15) / 31), (byte)((g * 255 + 31) / 63), (byte)((b * 255 + 15) / 31));
    }

    private static void DecodeDxt3Alpha(ReadOnlySpan<byte> block, Span<byte> alphas)
    {
        for (var i = 0; i < 8; i++)
        {
            var lo = block[i] & 0x0F;
            var hi = (block[i] >> 4) & 0x0F;
            alphas[i * 2] = (byte)(lo * 17);
            alphas[(i * 2) + 1] = (byte)(hi * 17);
        }
    }

    private static void DecodeDxt5Alpha(ReadOnlySpan<byte> block, Span<byte> alphas)
    {
        byte alpha0 = block[0];
        byte alpha1 = block[1];

        Span<byte> palette = stackalloc byte[8];
        palette[0] = alpha0;
        palette[1] = alpha1;

        if (alpha0 > alpha1)
        {
            for (var i = 1; i < 7; i++)
            {
                palette[i + 1] = (byte)((((7 - i) * alpha0) + (i * alpha1)) / 7);
            }
        }
        else
        {
            for (var i = 1; i < 5; i++)
            {
                palette[i + 1] = (byte)((((5 - i) * alpha0) + (i * alpha1)) / 5);
            }

            palette[6] = 0;
            palette[7] = 255;
        }

        ulong indices = 0;
        for (var i = 0; i < 6; i++)
        {
            indices |= (ulong)block[2 + i] << (8 * i);
        }

        for (var i = 0; i < 16; i++)
        {
            var index = (int)((indices >> (i * 3)) & 0x7);
            alphas[i] = palette[index];
        }
    }
}
