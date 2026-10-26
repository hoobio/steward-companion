using System.Reflection;
using System.Runtime.InteropServices;

using Steward.Core;

namespace Steward.App;

public static class AppInfo
{
    // The build flags are stamped on the app's own assembly, not on this library.
    public static readonly Assembly EntryAssembly = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;

    public static readonly bool IsGitHubRelease = EntryAssembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(attribute => attribute.Key == "GitHubRelease" && attribute.Value == "true");

    public static readonly bool IsPreRelease = EntryAssembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(attribute => attribute.Key == "StorePreRelease" && attribute.Value == "true");

    public static readonly bool IsPortableZip = EntryAssembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(attribute => attribute.Key == "PortableZip" && attribute.Value == "true");

    public static readonly string? StoreDisplayVersion = EntryAssembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "StoreDisplayVersion")?.Value is { Length: > 0 } value
            ? value
            : null;

    public const string PackageFamilyName = "Hoobi.Steward_thayxpy3eqg0g";

    private const int AppModelErrorNoPackage = 15700;

    public static readonly bool IsPackaged = ResolveIsPackaged();

    public static readonly bool IsWine = ResolveIsWine();

    public static readonly bool IsLinux = IsWine || OperatingSystem.IsLinux();

    public static readonly string BuildName = ResolveBuildName();

    public static readonly AppUpdateSource UpdateSource =
        AppUpdateSources.Resolve(OperatingSystem.IsWindows() && !IsWine, IsPackaged, IsGitHubRelease, IsPortableZip);

    public static string BuildSuffix => IsGitHubRelease ? (IsPreRelease ? " (Pre-release)" : "")
        : BuildName == "debug" ? " (Debug)" : " (Development)";

    public static readonly bool IsScreenshot = Services.ScreenshotPreview.IsRequested(Environment.GetCommandLineArgs());

    public static string WindowTitle => TitleFor(IsScreenshot);

    public static string TitleFor(bool screenshot) => screenshot ? "Steward" : $"Steward{BuildSuffix}{(IsLinux ? " (Linux)" : "")}";

    // The Store release and a Store flight are the same package on a machine, so they contend for the same instance.
    public static readonly string Train = BuildName == "prerelease" ? "store" : BuildName;

    public static string TrainDisplayName(string train) => train switch
    {
        "store" => "Store",
        "msi" => "MSI",
        "dev" => "Development",
        "debug" => "Debug",
        "linux" => "Linux",
        "macos" => "macOS",
        _ when train.EndsWith("-avalonia", StringComparison.Ordinal) => "preview",
        _ => train,
    };

    private static bool ResolveIsPackaged()
    {
        var length = 0;
        return OperatingSystem.IsWindows() && GetCurrentPackageFullName(ref length, nint.Zero) != AppModelErrorNoPackage;
    }

    private static bool ResolveIsWine() =>
        NativeLibrary.TryLoad("ntdll.dll", out var ntdll) && NativeLibrary.TryGetExport(ntdll, "wine_get_version", out _);

    private static string ResolveBuildName() => (IsPackaged, IsGitHubRelease, IsPreRelease) switch
    {
        (true, true, true) => "prerelease",
        (true, true, false) => "store",
        (true, false, _) => "dev",
        (false, true, _) => OperatingSystem.IsWindows() ? (IsPortableZip ? AppUpdateSources.PortableZipBuildName(StoreDisplayVersion) : "msi")
            : OperatingSystem.IsMacOS() ? "macos" : "linux",
        (false, false, _) => "debug",
    };

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, nint packageFullName);
}
