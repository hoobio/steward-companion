using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using Steward.App.ViewModels;
using Steward.Core;
using Steward.Desktop.Tests.UiPaths;

namespace Steward.Desktop.Tests;

public sealed class TukUiClientTests(AvaloniaSession session)
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string ClientVersion = "1.60.1.70245";

    [Fact]
    public void Get_addons_installs_ElvUI_from_TukUI_as_the_record_TOC_adoption_makes() => session.Run(() =>
    {
        var (host, main, install, requests) = Start();
        using var lifetime = host;
        var viewModel = new GetAddonsViewModel(main, install, 88568, host.Services.GetRequiredService<StewardClient>(), host.Services.GetRequiredService<AddonUpdater>().TukUi, NullLogger.Instance);

        var load = viewModel.LoadAsync();
        AvaloniaSession.WaitFor(() => load.IsCompleted);
        var elvui = viewModel.Results[0];
        Assert.Equal(("ElvUI", "TukUI", "16.00"), (elvui.Name, elvui.Source, elvui.LatestVersion));
        Assert.Null(viewModel.Error);

        elvui.InstallCommand.Execute(null);
        AvaloniaSession.WaitFor(() => !elvui.IsInstalling);

        Assert.Null(elvui.Failure);
        Assert.True(elvui.IsInstalled);
        var record = Assert.Single(host.Services.GetRequiredService<AppStateStore>().Load().ProviderAddons[install.FlavourPath]);
        var listed = host.Services.GetRequiredService<AddonUpdater>().TukUi.Cached(-2)!;
        Assert.Equal(TukUiAddons.Record(listed) with { Folders = record.Folders }, record);
        Assert.Equal(["ElvUI", "ElvUI_Libraries", "ElvUI_Options"], record.Folders);
        Assert.True(File.Exists(Path.Combine(install.AddOnsPath, "ElvUI_Options", "ElvUI_Options.toc")));
        Assert.Contains(requests, uri => uri.AbsolutePath.StartsWith("/v1/download/elvui/", StringComparison.Ordinal));
        Assert.DoesNotContain(requests, uri => uri.AbsolutePath.Contains("/api/addons/tukui/", StringComparison.Ordinal));
        Assert.True(MainViewModel.IsInstalled(install, "tukui--2"));
        main.Installs.Remove(install);
    });

    [Fact]
    public void A_server_backed_TukUI_record_checks_TukUI_directly_and_takes_its_icon() => session.Run(() =>
    {
        var (host, main, install, requests) = Start(store => store.ProviderAddons["{path}"] =
        [
            new("tukui--2", "ElvUI", "ElvUI", TukUiAddons.Source, -2, 0, ["ElvUI", "ElvUI_Libraries", "ElvUI_Options"], "https://api.hoobi.io/guild/api/addons/tukui/-2/icon.png"),
        ]);
        using var lifetime = host;
        var row = Assert.Single(install.AddonRows, row => row.AddonId == "tukui--2");
        Assert.Equal(-2, TukUiAddons.ProjectId(row.Addon));

        var probe = (Task<bool>)typeof(MainViewModel).GetMethod("ProbeProviderAddonsAsync", Private)!.Invoke(main, [new List<ManagedAddon> { row.Addon }, true])!;
        AvaloniaSession.WaitFor(() => probe.IsCompleted);

        var releases = (Dictionary<string, IReadOnlyDictionary<string, AddonRelease?>>)typeof(MainViewModel).GetField("_releases", Private)!.GetValue(main)!;
        Assert.Equal("16.00", releases["tukui--2"]["release"]!.Version);
        var record = Assert.Single(host.Services.GetRequiredService<AppStateStore>().Load().ProviderAddons[install.FlavourPath]);
        Assert.Equal("https://storage.tukui.org/ElvUI-Logo-Square.png", record.IconUrl);
        Assert.Equal("https://tukui.org/elvui", record.WebsiteUrl);
        Assert.DoesNotContain(requests, uri => uri.AbsolutePath.Contains("/api/addons/tukui/", StringComparison.Ordinal));
        Assert.DoesNotContain(session.Requests, uri => uri.AbsolutePath.Contains("/api/addons/tukui/", StringComparison.Ordinal));
        main.Installs.Remove(install);
    });

    [Fact]
    public void A_CurseForge_recorded_ElvUI_stays_a_CurseForge_row() => session.Run(() =>
    {
        var (host, main, install, _) = Start(store => store.ProviderAddons["{path}"] =
        [
            new("curseforge-231166-88568", "ElvUI", "ElvUI", CurseForgeAddons.Source, 231166, 88568, ["ElvUI", "ElvUI_Libraries", "ElvUI_Options"]),
        ], writeElvUI: true);
        using var lifetime = host;

        var rescan = install.RescanLocalAsync();
        AvaloniaSession.WaitFor(() => rescan.IsCompleted);

        Assert.Equal("curseforge-231166-88568", Assert.Single(host.Services.GetRequiredService<AppStateStore>().Load().ProviderAddons[install.FlavourPath]).Id);
        Assert.DoesNotContain(install.AddonRows, row => row.AddonId == "tukui--2");
        Assert.DoesNotContain(install.LocalRows, row => row.CanAdopt);
        main.Installs.Remove(install);
    });

    private (IHost Host, MainViewModel Main, WowInstallViewModel Install, ConcurrentQueue<Uri> Requests) Start(Action<AppState>? seed = null, bool writeElvUI = false)
    {
        var temp = session.NewFolder();
        var path = Path.Combine(temp, "_forever_");
        var addOns = Path.Combine(path, "Interface", "AddOns");
        Directory.CreateDirectory(addOns);
        if (writeElvUI)
        {
            foreach (var folder in new[] { "ElvUI", "ElvUI_Libraries", "ElvUI_Options" })
            {
                Directory.CreateDirectory(Path.Combine(addOns, folder));
                File.WriteAllText(Path.Combine(addOns, folder, $"{folder}.toc"), $"## Interface: 16001\n## Title: {folder}\n## Version: v16.00\n## X-Curse-Project-ID: 231166\n## X-Tukui-ProjectID: -2\n## X-Tukui-ProjectFolders: ElvUI, ElvUI_Libraries, ElvUI_Options\n");
            }
        }

        var requests = new ConcurrentQueue<Uri>();
        var host = session.NewHost(() => new TukUiApi(requests));
        var main = host.Services.GetRequiredService<MainViewModel>();
        if (seed is not null)
        {
            var store = host.Services.GetRequiredService<AppStateStore>();
            var state = store.Load();
            seed(state);
            state.ProviderAddons[path] = state.ProviderAddons["{path}"];
            state.ProviderAddons.Remove("{path}");
            store.Save(state);
        }

        main.UserName = "Hoobi";
        main.IsSignedIn = true;
        main.IsAuthorized = true;
        ((HashSet<string>)typeof(MainViewModel).GetField("_features", Private)!.GetValue(main)!).Add(StewardClient.AddonsFeature);
        var install = (WowInstallViewModel)typeof(MainViewModel)
            .GetMethod("AddInstall", Private)!
            .Invoke(main, [new WowInstall(temp, "_forever_", path, addOns, "wowf", ClientVersion, "World of Warcraft: Forever"), false])!;
        return (host, main, install, requests);
    }

    private sealed class TukUiApi(ConcurrentQueue<Uri> requests) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _steward = new(new FakeApi().Handler());

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            requests.Enqueue(uri);
            if (uri.Host != "api.tukui.org")
            {
                return await _steward.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }

            return uri.AbsolutePath switch
            {
                "/v1/addons" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(FakeApi.TukUiList.Replace("\"logo_square_url\":null,\"directories\":[\"ElvUI\"", "\"logo_square_url\":\"https://storage.tukui.org/ElvUI-Logo-Square.png\",\"directories\":[\"ElvUI\"", StringComparison.Ordinal)),
                },
                "/v1/download/elvui/token" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ElvUIZip()) },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        protected override void Dispose(bool disposing)
        {
            _steward.Dispose();
            base.Dispose(disposing);
        }

        private static byte[] ElvUIZip()
        {
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var folder in new[] { "ElvUI", "ElvUI_Libraries", "ElvUI_Options" })
                {
                    using var writer = new StreamWriter(archive.CreateEntry($"{folder}/{folder}.toc").Open());
                    writer.Write("## Interface: 16001\n## Version: v16.00\n");
                }
            }

            return stream.ToArray();
        }
    }
}
