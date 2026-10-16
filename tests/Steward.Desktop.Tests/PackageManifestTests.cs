using System.Xml.Linq;

using Steward.App;
using Steward.Core;

namespace Steward.Desktop.Tests;

public sealed class PackageManifestTests
{
    private static readonly XNamespace Foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace Uap10 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/10";
    private static readonly XNamespace Desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";

    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly XDocument Manifest = XDocument.Load(Path.Combine(RepoRoot, "packaging", "windows", "Package.appxmanifest"));

    private static readonly string AppExe = typeof(App).Assembly.GetName().Name + ".exe";

    [Fact]
    public void Store_identity_is_the_reserved_one()
    {
        var identity = Manifest.Root!.Element(Foundation + "Identity")!;
        Assert.Equal("Hoobi.Steward", (string?)identity.Attribute("Name"));
        Assert.Equal("CN=D74C026B-1081-4787-BDE6-0CFA2F1EDD71", (string?)identity.Attribute("Publisher"));
        Assert.Equal("x64", (string?)identity.Attribute("ProcessorArchitecture"));
    }

    [Fact]
    public void Application_and_startup_task_start_the_avalonia_exe()
    {
        var application = Manifest.Descendants(Foundation + "Application").Single();
        Assert.Equal(AppExe, (string?)application.Attribute("Executable"));
        Assert.Equal("Windows.FullTrustApplication", (string?)application.Attribute("EntryPoint"));

        var startup = application.Descendants(Desktop + "Extension").Single(e => (string?)e.Attribute("Category") == "windows.startupTask");
        Assert.Equal(AppExe, (string?)startup.Attribute("Executable"));
        Assert.Equal(StartupRegistration.TrayArgument, (string?)startup.Attribute(Uap10 + "Parameters"));
        Assert.Equal("StewardStartup", (string?)startup.Element(Desktop + "StartupTask")!.Attribute("TaskId"));
    }

    [Fact]
    public void Protocol_is_the_curseforge_scheme()
    {
        var protocol = Manifest.Descendants(Uap + "Protocol").Single();
        Assert.Equal(CurseForgeLinks.Scheme, (string?)protocol.Attribute("Name"));
    }

    [Fact]
    public void Every_visual_asset_has_a_file()
    {
        var assets = Directory.GetFiles(Path.Combine(RepoRoot, "src", "Steward.App", "Assets"), "*.png").Select(Path.GetFileName).ToList();
        var references = Manifest.Descendants()
            .SelectMany(e => e.Attributes().Select(a => a.Value).Append(e.HasElements ? "" : e.Value))
            .Where(v => v.StartsWith(@"Assets\", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(references);
        foreach (var reference in references)
        {
            var stem = Path.GetFileNameWithoutExtension(reference.Replace('\\', '/'));
            Assert.Contains(assets, file => file == stem + ".png" || file!.StartsWith(stem + ".", StringComparison.Ordinal));
        }
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Steward.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Steward.slnx was not found above the test output");
    }
}
