using System.Security.Cryptography;

namespace Steward.Core.Tests;

public sealed class SecretSealTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("steward-secrets-").FullName;
    private string KeyPath => Path.Combine(_root, "secrets", "session.key");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void KeyFile_RoundTripsAndOpensWhatItSealed()
    {
        var key = RandomNumberGenerator.GetBytes(SecretSeal.KeyLength);
        SecretSeal.WriteKeyFile(KeyPath, key);

        var read = SecretSeal.ReadKeyFile(KeyPath)!;
        var blob = SecretSeal.Seal(key, "gg_session value"u8.ToArray());

        Assert.Equal(key, read);
        Assert.Equal("gg_session value"u8.ToArray(), SecretSeal.Open(read, blob));
        Assert.DoesNotContain("gg_session", Convert.ToBase64String(blob), StringComparison.Ordinal);
    }

    [Fact]
    public void KeyFile_IsOwnerOnly()
    {
        SecretSeal.WriteKeyFile(KeyPath, RandomNumberGenerator.GetBytes(SecretSeal.KeyLength));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyPath));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.GetDirectoryName(KeyPath)!));
        }
    }

    [Fact]
    public void ReadKeyFile_MissingOrCorrupt_ReadsAsNoKey()
    {
        Assert.Null(SecretSeal.ReadKeyFile(KeyPath));

        Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
        File.WriteAllText(KeyPath, "not a key");

        Assert.Null(SecretSeal.ReadKeyFile(KeyPath));
    }

    [Fact]
    public void Open_WithAnotherKeyOrATamperedBlob_ThrowsCryptographicException()
    {
        var key = RandomNumberGenerator.GetBytes(SecretSeal.KeyLength);
        var blob = SecretSeal.Seal(key, "token"u8.ToArray());
        var tampered = (byte[])blob.Clone();
        tampered[^1] ^= 1;

        Assert.ThrowsAny<CryptographicException>(() => SecretSeal.Open(RandomNumberGenerator.GetBytes(SecretSeal.KeyLength), blob));
        Assert.ThrowsAny<CryptographicException>(() => SecretSeal.Open(key, tampered));
        Assert.ThrowsAny<CryptographicException>(() => SecretSeal.Open(key, [1, 2, 3]));
    }
}
