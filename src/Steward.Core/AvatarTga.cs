using System.Buffers.Binary;

namespace Steward.Core;

public static class AvatarTga
{
    public const int HeaderLength = 18;

    public static byte[] Encode(AvatarImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (!IsPowerOfTwo(image.Width) || !IsPowerOfTwo(image.Height))
        {
            throw new ArgumentOutOfRangeException(
                nameof(image),
                $"{image.Width}x{image.Height} is not a power-of-two texture size; the client refuses it");
        }

        var stride = image.Width * 4;
        if (image.Bgra.Length != stride * image.Height)
        {
            throw new ArgumentException(
                $"expected {stride * image.Height} BGRA bytes for {image.Width}x{image.Height}, got {image.Bgra.Length}",
                nameof(image));
        }

        var bytes = new byte[HeaderLength + image.Bgra.Length];
        bytes[2] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), (ushort)image.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), (ushort)image.Height);
        bytes[16] = 32;
        bytes[17] = 8;

        for (var row = 0; row < image.Height; row++)
        {
            image.Bgra.AsSpan((image.Height - 1 - row) * stride, stride)
                .CopyTo(bytes.AsSpan(HeaderLength + (row * stride)));
        }

        return bytes;
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}
