using System.Security.Cryptography;

namespace Steward.Core;

public static class SecretSeal
{
    public const int KeyLength = 32;

    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const UnixFileMode OwnerOnlyFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static byte[] Seal(byte[] key, byte[] data)
    {
        var blob = new byte[NonceLength + TagLength + data.Length];
        var nonce = blob.AsSpan(0, NonceLength);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagLength);
        aes.Encrypt(nonce, data, blob.AsSpan(NonceLength + TagLength), blob.AsSpan(NonceLength, TagLength));
        return blob;
    }

    public static byte[] Open(byte[] key, byte[] blob)
    {
        if (blob.Length < NonceLength + TagLength)
        {
            throw new CryptographicException("The sealed secret is too short.");
        }

        var data = new byte[blob.Length - NonceLength - TagLength];
        using var aes = new AesGcm(key, TagLength);
        aes.Decrypt(blob.AsSpan(0, NonceLength), blob.AsSpan(NonceLength + TagLength), blob.AsSpan(NonceLength, TagLength), data);
        return data;
    }

    public static byte[]? ReadKeyFile(string path)
    {
        try
        {
            var key = File.ReadAllBytes(path);
            return key.Length == KeyLength ? key : null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    public static void WriteKeyFile(string path, byte[] key)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var temp = path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
        }
        else
        {
            Directory.CreateDirectory(folder, OwnerOnlyFolder);
            File.SetUnixFileMode(folder, OwnerOnlyFolder);
            options.UnixCreateMode = OwnerOnlyFile;
            File.Delete(temp);
        }

        using (var stream = new FileStream(temp, options))
        {
            stream.Write(key);
        }

        File.Move(temp, path, overwrite: true);
    }
}
