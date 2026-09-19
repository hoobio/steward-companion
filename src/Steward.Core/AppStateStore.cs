using System.Text.Json;

namespace Steward.Core;

public sealed class AppStateStore
{
    private const string DefaultChannel = "beta";

    private readonly string _path;

    public AppStateStore(string? path = null) =>
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Steward",
            "state.json");

    public static string Key(string flavourPath, string addonId) => $"{flavourPath}|{addonId}";

    public AppState Load()
    {
        if (!File.Exists(_path))
        {
            return new AppState(DefaultChannel, []);
        }

        var json = File.ReadAllText(_path);
        return JsonSerializer.Deserialize(json, CompanionJsonContext.Default.AppState)
            ?? new AppState(DefaultChannel, []);
    }

    public void Save(AppState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(state, CompanionJsonContext.Default.AppState));
    }
}
