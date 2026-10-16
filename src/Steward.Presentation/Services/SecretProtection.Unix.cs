using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;

using Microsoft.Extensions.Logging;

using Steward.Core;
using Steward.Core.Diagnostics;

namespace Steward.App.Services;

public enum KeyringAnswer
{
    Unavailable,
    NotFound,
    TimedOut,
    Found,
}

public interface ISecretKeyring
{
    string Name { get; }

    (KeyringAnswer Answer, string? Secret) Lookup(string service, string account, ILogger logger);

    KeyringAnswer Store(string service, string account, string label, string secret, ILogger logger);
}

public static class SecretProtection
{
    private const string Service = "Steward";
    private const string Account = "session-key";
    private const string Label = "Steward session key";
    private static readonly Lock Gate = new();
    private static byte[]? _key;

    public static string Folder { get; set; } = Path.Combine(LocalData.Folder, "secrets");

    public static ISecretKeyring? SecretService { get; set; }

    public static byte[]? Protect(byte[] data, ILogger logger)
    {
        try
        {
            return SecretSeal.Seal(Key(logger), data);
        }
        catch (CryptographicException ex)
        {
            logger.Warn(ex, "No secret store could hold the session key");
            return null;
        }
    }

    public static byte[] Unprotect(byte[] data, ILogger logger) => SecretSeal.Open(Key(logger), data);

    private static byte[] Key(ILogger logger)
    {
        lock (Gate)
        {
            return _key ??= LoadKey(logger);
        }
    }

    private static byte[] LoadKey(ILogger logger)
    {
        var file = Path.Combine(Folder, "session.key");
        try
        {
            if (SecretSeal.ReadKeyFile(file) is { } fromFile)
            {
                logger.Info($"Session key read from the owner-only file {file}");
                return fromFile;
            }

            byte[]? key = null;
            foreach (var keyring in Keyrings())
            {
                var (lookup, stored) = Lookup(keyring, logger);
                if (lookup == KeyringAnswer.Found)
                {
                    logger.Info($"Session key read from {keyring.Name}");
                    return stored!;
                }

                if (lookup == KeyringAnswer.TimedOut)
                {
                    throw new CryptographicException($"{keyring.Name} did not answer.");
                }

                if (lookup == KeyringAnswer.Unavailable)
                {
                    continue;
                }

                key ??= RandomNumberGenerator.GetBytes(SecretSeal.KeyLength);
                if (keyring.Store(Service, Account, Label, Convert.ToBase64String(key), logger) == KeyringAnswer.Found
                    && Lookup(keyring, logger) is (KeyringAnswer.Found, { } readBack)
                    && readBack.AsSpan().SequenceEqual(key))
                {
                    logger.Info($"Session key stored in {keyring.Name}");
                    return key;
                }
            }

            key ??= RandomNumberGenerator.GetBytes(SecretSeal.KeyLength);
            SecretSeal.WriteKeyFile(file, key);
            logger.Info($"No keyring available: session key stored in the owner-only file {file}");
            return key;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CryptographicException("The session key file could not be read or written.", ex);
        }
    }

    private static IEnumerable<ISecretKeyring> Keyrings()
    {
        if (OperatingSystem.IsMacOS())
        {
            yield return CommandKeyring.Keychain;
            yield break;
        }

        // libsecret answers a missing session bus with exit 1 and no output, the same as a missing item.
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is not { Length: > 0 })
        {
            yield break;
        }

        if (SecretService is { } secretService)
        {
            yield return secretService;
        }

        yield return CommandKeyring.SecretTool;
    }

    private static (KeyringAnswer Answer, byte[]? Key) Lookup(ISecretKeyring keyring, ILogger logger)
    {
        var (answer, secret) = keyring.Lookup(Service, Account, logger);
        if (answer != KeyringAnswer.Found)
        {
            return (answer, null);
        }

        try
        {
            return Convert.FromBase64String(secret!.Trim()) is { Length: SecretSeal.KeyLength } key ? (KeyringAnswer.Found, key) : (KeyringAnswer.NotFound, null);
        }
        catch (FormatException)
        {
            return (KeyringAnswer.NotFound, null);
        }
    }

    private sealed class CommandKeyring(bool keychain) : ISecretKeyring
    {
        private const int KeychainItemNotFound = 44;
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

        public static readonly CommandKeyring Keychain = new(keychain: true);

        public static readonly CommandKeyring SecretTool = new(keychain: false);

        public string Name => keychain ? "the login Keychain" : "the Secret Service keyring (secret-tool)";

        public (KeyringAnswer Answer, string? Secret) Lookup(string service, string account, ILogger logger) => keychain
            ? Run("security", ["find-generic-password", "-s", service, "-a", account, "-w"], null, logger)
            : Run("secret-tool", ["lookup", "service", service, "account", account], null, logger);

        public KeyringAnswer Store(string service, string account, string label, string secret, ILogger logger) => (keychain
            ? Run("security", ["-i"], $"add-generic-password -U -s {service} -a {account} -w {secret}\n", logger)
            : Run("secret-tool", ["store", $"--label={label}", "service", service, "account", account], secret, logger)).Answer;

        private (KeyringAnswer Answer, string? Output) Run(string command, string[] arguments, string? input, ILogger logger)
        {
            var start = new ProcessStartInfo(command)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            try
            {
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                process.StandardInput.Write(input ?? "");
                process.StandardInput.Close();
                if (!process.WaitForExit(CommandTimeout))
                {
                    process.Kill(entireProcessTree: true);
                    logger.Warn(null, $"{command} {arguments[0]} timed out");
                    return (KeyringAnswer.TimedOut, null);
                }

                var message = error.Result.Trim();
                if (process.ExitCode == 0)
                {
                    return (KeyringAnswer.Found, output.Result);
                }

                logger.Info($"{command} {arguments[0]} exited {process.ExitCode}: {message}");
                var notFound = keychain ? process.ExitCode == KeychainItemNotFound : process.ExitCode == 1 && message.Length == 0;
                return (notFound ? KeyringAnswer.NotFound : KeyringAnswer.Unavailable, null);
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
            {
                logger.Info($"{command} is not available: {ex.Message}");
                return (KeyringAnswer.Unavailable, null);
            }
        }
    }
}
