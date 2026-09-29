using Steward.App.ViewModels;

using Microsoft.UI.Xaml.Controls;

namespace Steward.App.Views;

public sealed partial class SyncPage : Page
{
    public SyncPage(MainViewModel main)
    {
        ViewModel = main.Sync;
        InitializeComponent();
    }

    public SyncViewModel ViewModel { get; }
}
