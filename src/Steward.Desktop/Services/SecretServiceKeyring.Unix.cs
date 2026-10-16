using System.Text;

using Microsoft.Extensions.Logging;

using Steward.App.Services;
using Steward.Core.Diagnostics;

using Tmds.DBus.Protocol;

namespace Steward.Desktop.Services;

internal sealed class SecretServiceKeyring : ISecretKeyring
{
    private const string Destination = "org.freedesktop.secrets";
    private const string ServicePath = "/org/freedesktop/secrets";
    private const string ServiceInterface = "org.freedesktop.Secret.Service";
    private const string NoPath = "/";
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    public string Name => "the Secret Service keyring (D-Bus)";

    public (KeyringAnswer Answer, string? Secret) Lookup(string service, string account, ILogger logger) =>
        Run(logger, async connection =>
        {
            var session = await OpenSessionAsync(connection);
            try
            {
                var (unlocked, locked) = await CallAsync<(ObjectPath[], ObjectPath[])>(connection, ServicePath, ServiceInterface, "SearchItems", "a{ss}",
                    (ref MessageWriter writer) => WriteAttributes(ref writer, service, account),
                    reader => (reader.ReadArrayOfObjectPath(), reader.ReadArrayOfObjectPath()));
                if (unlocked.Length == 0 && locked.Length == 0)
                {
                    return (KeyringAnswer.NotFound, null);
                }

                var item = unlocked.Length > 0 ? unlocked[0].ToString() : locked[0].ToString();
                if (unlocked.Length == 0 && !await UnlockAsync(connection, item))
                {
                    return (KeyringAnswer.TimedOut, null);
                }

                var secret = await CallAsync(connection, item, "org.freedesktop.Secret.Item", "GetSecret", "o",
                    (ref MessageWriter writer) => writer.WriteObjectPath(session),
                    reader =>
                    {
                        reader.AlignStruct();
                        reader.ReadObjectPath();
                        reader.ReadArrayOfByte();
                        return reader.ReadArrayOfByte();
                    });
                return (KeyringAnswer.Found, Encoding.UTF8.GetString(secret));
            }
            finally
            {
                await CloseSessionAsync(connection, session);
            }
        });

    public KeyringAnswer Store(string service, string account, string label, string secret, ILogger logger) =>
        Run(logger, async connection =>
        {
            var collection = await CallAsync(connection, ServicePath, ServiceInterface, "ReadAlias", "s",
                (ref MessageWriter writer) => writer.WriteString("default"),
                reader => reader.ReadObjectPathAsString());
            if (collection == NoPath)
            {
                logger.Info("The Secret Service has no default collection");
                return (KeyringAnswer.Unavailable, (string?)null);
            }

            if (!await UnlockAsync(connection, collection))
            {
                return (KeyringAnswer.TimedOut, null);
            }

            var session = await OpenSessionAsync(connection);
            try
            {
                var prompt = await CallAsync(connection, collection, "org.freedesktop.Secret.Collection", "CreateItem", "a{sv}(oayays)b",
                    (ref MessageWriter writer) =>
                    {
                        var properties = writer.WriteDictionaryStart();
                        writer.WriteDictionaryEntryStart();
                        writer.WriteString("org.freedesktop.Secret.Item.Label");
                        writer.WriteVariantString(label);
                        writer.WriteDictionaryEntryStart();
                        writer.WriteString("org.freedesktop.Secret.Item.Attributes");
                        writer.WriteSignature("a{ss}");
                        WriteAttributes(ref writer, service, account);
                        writer.WriteDictionaryEnd(properties);
                        writer.WriteStructureStart();
                        writer.WriteObjectPath(session);
                        writer.WriteArray(Array.Empty<byte>());
                        writer.WriteArray(Encoding.UTF8.GetBytes(secret));
                        writer.WriteString("text/plain");
                        writer.WriteBool(true);
                    },
                    reader =>
                    {
                        reader.ReadObjectPath();
                        return reader.ReadObjectPathAsString();
                    });
                return (prompt == NoPath || await PromptAsync(connection, prompt) ? KeyringAnswer.Found : KeyringAnswer.TimedOut, null);
            }
            finally
            {
                await CloseSessionAsync(connection, session);
            }
        }).Answer;

    private static (KeyringAnswer Answer, string? Secret) Run(ILogger logger, Func<DBusConnection, Task<(KeyringAnswer, string?)>> call)
    {
        try
        {
            var task = Task.Run(() => call(DBusConnection.Session));
            if (task.Wait(Limit))
            {
                return task.Result;
            }

            _ = task.ContinueWith(failed => failed.Exception, TaskScheduler.Default);
            logger.Warn(null, "The Secret Service did not answer within 5 seconds");
            return (KeyringAnswer.TimedOut, null);
        }
        catch (AggregateException ex) when (ex.InnerException is DBusExceptionBase or DBusConnectionException or InvalidOperationException)
        {
            logger.Info($"The Secret Service is not available over D-Bus: {ex.InnerException.Message}");
            return (KeyringAnswer.Unavailable, null);
        }
    }

    // The plain algorithm sends the secret unencrypted over the user's own session bus, which only that user's processes can reach.
    private static Task<string> OpenSessionAsync(DBusConnection connection) =>
        CallAsync(connection, ServicePath, ServiceInterface, "OpenSession", "sv",
            (ref MessageWriter writer) =>
            {
                writer.WriteString("plain");
                writer.WriteVariantString("");
            },
            reader =>
            {
                reader.ReadVariantValue();
                return reader.ReadObjectPathAsString();
            });

    private static Task CloseSessionAsync(DBusConnection connection, string session) =>
        connection.CallMethodAsync(Message(connection, session, "org.freedesktop.Secret.Session", "Close", null, null));

    private static async Task<bool> UnlockAsync(DBusConnection connection, string path)
    {
        var prompt = await CallAsync(connection, ServicePath, ServiceInterface, "Unlock", "ao",
            (ref MessageWriter writer) => writer.WriteArray(new[] { new ObjectPath(path) }),
            reader =>
            {
                reader.ReadArrayOfObjectPath();
                return reader.ReadObjectPathAsString();
            });
        return prompt == NoPath || await PromptAsync(connection, prompt);
    }

    private static async Task<bool> PromptAsync(DBusConnection connection, string prompt)
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watch = await connection.WatchSignalAsync(
            null,
            prompt,
            "org.freedesktop.Secret.Prompt",
            "Completed",
            (message, _) => message.GetBodyReader().ReadBool(),
            notification =>
            {
                if (notification.HasValue)
                {
                    completed.TrySetResult(!notification.Value);
                }
            },
            ObserverFlags.None,
            false,
            null);
        await connection.CallMethodAsync(Message(connection, prompt, "org.freedesktop.Secret.Prompt", "Prompt", "s", (ref MessageWriter writer) => writer.WriteString("")));
        return await completed.Task;
    }

    private static void WriteAttributes(ref MessageWriter writer, string service, string account)
    {
        var attributes = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString("service");
        writer.WriteString(service);
        writer.WriteDictionaryEntryStart();
        writer.WriteString("account");
        writer.WriteString(account);
        writer.WriteDictionaryEnd(attributes);
    }

    private delegate void Body(ref MessageWriter writer);

    private static Task<T> CallAsync<T>(DBusConnection connection, string path, string @interface, string member, string signature, Body body, Func<Reader, T> read) =>
        connection.CallMethodAsync(Message(connection, path, @interface, member, signature, body), (reply, _) => read(reply.GetBodyReader()), null);

    private static MessageBuffer Message(DBusConnection connection, string path, string @interface, string member, string? signature, Body? body)
    {
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(Destination, path, @interface, member, signature, MessageFlags.None);
            body?.Invoke(ref writer);
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }
}
