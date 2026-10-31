using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Steward.Core.Tests;

public sealed class PackedSavedVariablesTests
{
    private static string Pack(byte[] cbor)
    {
        using var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(cbor);
        }

        return Convert.ToBase64String(buffer.ToArray());
    }

    private static LuaValue Decode(string hex) => PackedSavedVariables.Decode(Pack(Convert.FromHexString(hex)));

    [Theory]
    [InlineData("1903e8", 1000)]
    [InlineData("3903e7", -1000)]
    [InlineData("f93c00", 1.0)]
    [InlineData("fb3ff199999999999a", 1.1)]
    [InlineData("1b000000e8d4a51000", 1000000000000)]
    public void Decode_ReadsRfc8949Numbers(string hex, double expected) => Assert.Equal(expected, Decode(hex).Number);

    [Fact]
    public void Decode_ReadsRfc8949Containers()
    {
        var nested = Decode("8201820203");
        Assert.Equal(1, nested.Items[0].Number);
        Assert.Equal([2.0, 3.0], nested.Items[1].Items.Select(i => i.Number));

        var indefinite = Decode("bf61610161629f0203ffff");
        Assert.Equal(1, indefinite.GetNumber("a"));
        Assert.Equal([2.0, 3.0], indefinite.GetTable("b")!.Items.Select(i => i.Number));

        Assert.Equal("streaming", Decode("7f657374726561646d696e67ff").Text);
        Assert.Equal(LuaKind.Nil, Decode("f6").Kind);

        var integerKeys = Decode("a201020304");
        Assert.Equal([1.0, 3.0], integerKeys.Table.Select(e => e.Key!.Number));
        Assert.Equal([2.0, 4.0], integerKeys.Table.Select(e => e.Value.Number));
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    public void Decode_RefusesAValueThatIsNotPackedCbor(string packed) =>
        Assert.Throws<FormatException>(() => PackedSavedVariables.Decode(packed));

    [Fact]
    public void Read_TakesPackedCharacters_AsThePlainTable()
    {
        const string plain = """
            StewardDB = {
            ["characters"] = {
            ["Player-5826-0A1B2C3D"] = {
            ["gear"] = {
            ["schema"] = 2,
            ["observedAt"] = 1790900000,
            ["level"] = 60,
            ["equipped"] = {
            [16] = { ["link"] = "item:19019:1900:0:0:0:0:0:0:60:0:0:0:0", ["itemID"] = 19019, ["quality"] = 5, ["equipLoc"] = "INVTYPE_WEAPON", ["ilvl"] = 80 },
            },
            ["bags"] = {},
            ["fp"] = "8bb1ce6a",
            },
            },
            },
            }
            """;
        var characters = LuaSavedVariables.Parse(plain)["StewardDB"].GetTable("characters")!;
        var packed = $$"""
            StewardDB = {
            ["packedCharacters"] = "{{Pack(Cbor(characters))}}",
            }
            """;

        var fromPlain = StewardSavedVariables.Read([("account.lua", DateTimeOffset.UnixEpoch, plain)]);
        var fromPacked = StewardSavedVariables.Read([("account.lua", DateTimeOffset.UnixEpoch, packed)]);

        Assert.Equal(0, fromPacked.Skipped);
        Assert.Equal(19019, fromPacked.Gear["Player-5826-0A1B2C3D"].Equipped["16"].ItemId);
        Assert.Equal(JsonSerializer.Serialize(fromPlain.Gear), JsonSerializer.Serialize(fromPacked.Gear));
    }

    private static byte[] Cbor(LuaValue value)
    {
        var bytes = new List<byte>();
        Write(bytes, value);
        return [.. bytes];
    }

    private static void Write(List<byte> bytes, LuaValue value)
    {
        switch (value.Kind)
        {
            case LuaKind.Number when value.Number == Math.Floor(value.Number) && value.Number >= 0:
                Head(bytes, 0, (ulong)value.Number);
                break;
            case LuaKind.Number:
                bytes.Add(0xFB);
                var number = new byte[8];
                BinaryPrimitives.WriteDoubleBigEndian(number, value.Number);
                bytes.AddRange(number);
                break;
            case LuaKind.Text:
                var text = Encoding.UTF8.GetBytes(value.Text!);
                Head(bytes, 3, (ulong)text.Length);
                bytes.AddRange(text);
                break;
            case LuaKind.Boolean:
                bytes.Add(value.Boolean ? (byte)0xF5 : (byte)0xF4);
                break;
            case LuaKind.Nil:
                bytes.Add(0xF6);
                break;
            default:
                Head(bytes, 5, (ulong)value.Table.Count);
                var index = 0;
                foreach (var entry in value.Table)
                {
                    Write(bytes, entry.Key ?? LuaValue.FromNumber(++index));
                    Write(bytes, entry.Value);
                }

                break;
        }
    }

    private static void Head(List<byte> bytes, int major, ulong argument)
    {
        if (argument < 24)
        {
            bytes.Add((byte)((major << 5) | (int)argument));
            return;
        }

        bytes.Add((byte)((major << 5) | 27));
        var buffer = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(buffer, argument);
        bytes.AddRange(buffer);
    }
}
