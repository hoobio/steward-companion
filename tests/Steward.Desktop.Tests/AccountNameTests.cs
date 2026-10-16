using Microsoft.Extensions.DependencyInjection;

using Steward.App.ViewModels;

namespace Steward.Desktop.Tests;

public sealed class AccountNameTests(AvaloniaSession session)
{
    private const string Reconnecting = "Reconnecting to api.hoobi.io; changes still arrive on the next check";

    [Theory]
    [InlineData(null, LiveUpdatesState.Reconnecting, Reconnecting)]
    [InlineData("", LiveUpdatesState.Reconnecting, Reconnecting)]
    [InlineData("Hoobi", LiveUpdatesState.Reconnecting, "Hoobi, " + Reconnecting)]
    [InlineData("Hoobi", LiveUpdatesState.Hidden, "Hoobi")]
    [InlineData(null, LiveUpdatesState.Hidden, null)]
    public void The_account_name_joins_only_the_parts_that_are_set(string? user, LiveUpdatesState state, string? expected) => session.Run(() =>
    {
        using var host = session.NewHost();
        var main = host.Services.GetRequiredService<MainViewModel>();
        main.UserName = user;
        main.LiveUpdatesState = state;

        Assert.Equal(expected, main.AccountAutomationName);
    });
}
