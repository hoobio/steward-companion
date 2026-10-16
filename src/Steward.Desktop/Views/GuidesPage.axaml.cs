using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;

using FluentAvalonia.UI.Controls;

using Steward.App.ViewModels;
using Steward.Core;

namespace Steward.Desktop.Views;

public sealed partial class GuidesPage : UserControl
{
    public static readonly IMultiValueConverter ReloadNotice = new FuncMultiValueConverter<object?, bool>(values =>
    {
        var items = values.ToArray();
        return items is [RestedXpInstallViewModel guide, var client, var writtenAt]
            && guide.ReloadNoticeVisibility(client as WowClientProcess, writtenAt as DateTimeOffset?);
    });

    public GuidesPage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }

    private void OnSignInClick(object? sender, RoutedEventArgs e) => ViewModel?.ShowRestedXpSignIn?.Invoke();

    private void OnSessionBarClosed(FAInfoBar sender, EventArgs args) => ViewModel?.NavigateToAddons?.Invoke();
}
