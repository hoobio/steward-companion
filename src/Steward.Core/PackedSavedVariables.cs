using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Steward.Core;

public static class PackedSavedVariables
{
    private const int Break = -1;

    // The Steward addon's Steward.Pack: C_EncodingUtil.SerializeCBOR, CompressString with its default method, standard base64.
    public static LuaValue Decode(string packed)
    {
        try
        {
            using var bytes = new MemoryStream();
            using (var inflate = Inflate(Convert.FromBase64String(packed)))
            {
                inflate.CopyTo(bytes);
            }

            var reader = new Reader(bytes.ToArray());
            var value = reader.Next() ?? throw new FormatException("the packed value is a CBOR break");
            return reader.AtEnd ? value : throw new FormatException("the packed value has trailing bytes");
        }
        catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            throw new FormatException("the packed value is not valid zlib-compressed CBOR", e);
        }
    }

    private static Stream Inflate(byte[] compressed)
    {
        var source = new MemoryStream(compressed);
        return compressed switch
        {
            [0x1F, 0x8B, ..] => new GZipStream(source, CompressionMode.Decompress),
            [var cmf and (0x08 or 0x18 or 0x28 or 0x38 or 0x48 or 0x58 or 0x68 or 0x78), var flg, ..] when (cmf * 256 + flg) % 31 == 0
                => new ZLibStream(source, CompressionMode.Decompress),
            _ => new DeflateStream(source, CompressionMode.Decompress),
        };
    }

    private sealed class Reader(byte[] data)
    {
        private int position;

        public bool AtEnd => position == data.Length;

        public LuaValue? Next()
        {
            var initial = data[position++];
            var (major, info) = (initial >> 5, initial & 0x1F);
            if (initial == 0xFF)
            {
                return null;
            }

            if (major == 7)
            {
                return Simple(info);
            }

            var length = info == 31 ? Break : (long)Argument(info);
            return major switch
            {
                0 => LuaValue.FromNumber(length),
                1 => LuaValue.FromNumber(-1 - (double)length),
                2 or 3 => LuaValue.FromString(Encoding.UTF8.GetString(Bytes(length))),
                4 => LuaValue.FromTable([.. Items(length).Select(item => new LuaEntry(null, item))]),
                5 => LuaValue.FromTable(Pairs(length)),
                _ => Next() ?? throw new FormatException("a CBOR tag wraps a break"),
            };
        }

        private LuaValue Simple(int info) => info switch
        {
            20 => LuaValue.FromBoolean(false),
            21 => LuaValue.FromBoolean(true),
            22 or 23 => LuaValue.Nil,
            25 => LuaValue.FromNumber((double)BinaryPrimitives.ReadHalfBigEndian(Take(2))),
            26 => LuaValue.FromNumber(BinaryPrimitives.ReadSingleBigEndian(Take(4))),
            27 => LuaValue.FromNumber(BinaryPrimitives.ReadDoubleBigEndian(Take(8))),
            _ => throw new FormatException($"unsupported CBOR simple value {info}"),
        };

        private ulong Argument(int info) => info switch
        {
            < 24 => (ulong)info,
            24 => Take(1)[0],
            25 => BinaryPrimitives.ReadUInt16BigEndian(Take(2)),
            26 => BinaryPrimitives.ReadUInt32BigEndian(Take(4)),
            27 => BinaryPrimitives.ReadUInt64BigEndian(Take(8)),
            _ => throw new FormatException($"unsupported CBOR argument {info}"),
        };

        private byte[] Bytes(long length)
        {
            if (length != Break)
            {
                return Take((int)length).ToArray();
            }

            var chunks = new List<byte>();
            while (Next() is { } chunk)
            {
                chunks.AddRange(Encoding.UTF8.GetBytes(chunk.Text ?? throw new FormatException("an indefinite string holds a non-string chunk")));
            }

            return [.. chunks];
        }

        private List<LuaValue> Items(long length)
        {
            var items = new List<LuaValue>();
            for (var i = 0L; length == Break || i < length; i++)
            {
                if (Next() is not { } item)
                {
                    break;
                }

                items.Add(item);
            }

            return items;
        }

        private List<LuaEntry> Pairs(long length)
        {
            var entries = new List<LuaEntry>();
            for (var i = 0L; length == Break || i < length; i++)
            {
                if (Next() is not { } key)
                {
                    break;
                }

                entries.Add(new LuaEntry(key, Next() ?? throw new FormatException("a CBOR map key has no value")));
            }

            return entries;
        }

        private ReadOnlySpan<byte> Take(int count)
        {
            var span = data.AsSpan(position, count);
            position += count;
            return span;
        }
    }
}
