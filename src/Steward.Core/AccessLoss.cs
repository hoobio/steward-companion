using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Steward.Core;

public sealed record LostAccessAddon(
    [property: JsonPropertyName("flavour_path")] string FlavourPath,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("folders")] IReadOnlyList<string> Folders,
    [property: JsonPropertyName("first_missing")] DateTimeOffset FirstMissing,
    [property: JsonPropertyName("due")] bool Due = false);

public static class AccessLoss
{
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(30);

    public static string Notice(LostAccessAddon addon)
    {
        ArgumentNullException.ThrowIfNull(addon);
        return $"{addon.Name} was removed because your access ended";
    }

    public static bool IsDistributed(ManagedAddon addon)
    {
        ArgumentNullException.ThrowIfNull(addon);
        return !addon.IsCurseForge && addon.Source is AddonCatalogue.StewardSource or AddonCatalogue.ProtectedSource;
    }

    public static bool IsRevoked(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String
                && error.GetString() == "revoked";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static Dictionary<string, LostAccessAddon> Revoke(AppState state, IReadOnlyList<ManagedAddon>? catalogue, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        var lost = new Dictionary<string, LostAccessAddon>(state.LostAccess ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var addon in (catalogue ?? []).Where(IsDistributed))
        {
            foreach (var entry in Installed(state, addon))
            {
                lost[entry.Key] = entry.Value with { FirstMissing = now, Due = true };
            }
        }

        return lost;
    }

    public static Dictionary<string, LostAccessAddon> Track(
        AppState state, IReadOnlyList<ManagedAddon>? previous, IReadOnlyList<CatalogueAddon>? current, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (current is not { Count: > 0 })
        {
            return new Dictionary<string, LostAccessAddon>(state.LostAccess ?? [], StringComparer.OrdinalIgnoreCase);
        }

        var listed = current.Select(addon => addon.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lost = new Dictionary<string, LostAccessAddon>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, entry) in state.LostAccess ?? [])
        {
            if (!listed.Contains(entry.Id) && state.Installs.ContainsKey(key))
            {
                lost[key] = entry with { Due = entry.Due || now - entry.FirstMissing >= Grace };
            }
        }

        foreach (var addon in (previous ?? []).Where(addon => IsDistributed(addon) && !listed.Contains(addon.Id)))
        {
            foreach (var entry in Installed(state, addon))
            {
                lost.TryAdd(entry.Key, entry.Value with { FirstMissing = now });
            }
        }

        return lost;
    }

    public static IReadOnlyList<LostAccessAddon> RemoveDue(
        AppState state, Func<LostAccessAddon, bool> blocked, Action<LostAccessAddon, Exception>? failed = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(blocked);
        var removed = new List<LostAccessAddon>();
        foreach (var (key, entry) in state.LostAccess.Where(entry => entry.Value.Due).ToList())
        {
            if (blocked(entry))
            {
                continue;
            }

            try
            {
                lock (AddonUpdater.AddOnsWriteLock)
                {
                    AddonUpdater.Uninstall(AddOnsPath(entry.FlavourPath), entry.Folders);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed?.Invoke(entry, ex);
                continue;
            }
            catch (InvalidOperationException ex)
            {
                failed?.Invoke(entry, ex);
                state.LostAccess.Remove(key);
                continue;
            }

            state.Installs.Remove(key);
            state.IgnoredAddons.RemoveAll(ignored => string.Equals(ignored, key, StringComparison.OrdinalIgnoreCase));
            state.LostAccess.Remove(key);
            removed.Add(entry);
        }

        return removed;
    }

    public static string AddOnsPath(string flavourPath) => Path.Combine(flavourPath, "Interface", "AddOns");

    private static IEnumerable<KeyValuePair<string, LostAccessAddon>> Installed(AppState state, ManagedAddon addon) =>
        state.Installs
            .Where(install => install.Key.EndsWith("|" + addon.Id, StringComparison.OrdinalIgnoreCase))
            .Select(install => KeyValuePair.Create(install.Key, new LostAccessAddon(
                install.Key[..^(addon.Id.Length + 1)],
                addon.Id,
                addon.DisplayName,
                addon.Source,
                [.. (install.Value.Folders ?? []).Append(addon.FolderName).Distinct(StringComparer.OrdinalIgnoreCase)],
                default)));
}

public sealed class SessionRevocation
{
    private int _signedIn;
    private int _revoked;

    public void SetSignedIn(bool signedIn)
    {
        Volatile.Write(ref _signedIn, signedIn ? 1 : 0);
        Interlocked.Exchange(ref _revoked, 0);
    }

    public bool Report()
    {
        if (Volatile.Read(ref _signedIn) == 0)
        {
            return false;
        }

        Interlocked.Exchange(ref _revoked, 1);
        return true;
    }

    public bool Take() => Interlocked.Exchange(ref _revoked, 0) == 1 && Volatile.Read(ref _signedIn) == 1;
}

public sealed class SessionRevokedHandler(Action? onRevoked = null) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
            if (AccessLoss.IsRevoked(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)))
            {
                onRevoked?.Invoke();
            }
        }

        return response;
    }
}
