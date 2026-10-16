using System.Security.Cryptography;

using Microsoft.Extensions.Logging;

namespace Steward.App.Services;

public static class SecretProtection
{
    public static string Folder { get; set; } = "";

    public static byte[]? Protect(byte[] data, ILogger logger) => ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

    public static byte[] Unprotect(byte[] data, ILogger logger) => ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
}
