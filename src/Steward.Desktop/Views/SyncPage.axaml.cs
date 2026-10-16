using Avalonia.Controls;

using Steward.App.ViewModels;

namespace Steward.Desktop.Views;

public sealed partial class SyncPage : UserControl
{
    public SyncPage(MainViewModel main)
    {
        ArgumentNullException.ThrowIfNull(main);

        ViewModel = main.Sync;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public SyncViewModel ViewModel { get; }
}
