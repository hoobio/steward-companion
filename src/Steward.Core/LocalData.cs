namespace Steward.Core;

public static class LocalData
{
    // Off Windows .NET answers "" while $XDG_DATA_HOME or ~/.local/share does not exist yet, which made every data path relative.
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            OperatingSystem.IsWindows() ? Environment.SpecialFolderOption.None : Environment.SpecialFolderOption.Create),
        "Steward");
}
