using Microsoft.Extensions.DependencyInjection;

using Steward.App.Services;
using Steward.App.ViewModels;

namespace Steward.Desktop.Tests;

public sealed class IsolationTests(AvaloniaSession session)
{
    [Theory]
    [InlineData("Steward")]
    [InlineData("Addon")]
    public async Task Host_http_clients_never_reach_a_socket(string name)
    {
        using var host = session.Run(() => session.NewHost());
        var before = session.Requests.Count;

        using var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(name);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://example.invalid/", TestContext.Current.CancellationToken));

        Assert.Equal(before + 1, session.Requests.Count);
    }

    [Fact]
    public void Host_data_paths_live_under_the_temp_root() => session.Run(() =>
    {
        using var host = session.NewHost();
        var temp = Path.GetTempPath();

        Assert.StartsWith(temp, host.Services.GetRequiredService<MainViewModel>().DataFolder, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(temp, ManifestIcon.CacheFolder, StringComparison.OrdinalIgnoreCase);
    });
}
