using Steward.App.ViewModels;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Steward.App.Views;

public sealed partial class SyncPage : Page
{
    public SyncPage()
    {
        InitializeComponent();
    }

    public SyncViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = (e.Parameter as MainViewModel)?.Sync;
        Bindings.Update();
    }
}
