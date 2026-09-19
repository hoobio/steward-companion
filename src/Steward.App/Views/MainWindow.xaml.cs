using Steward.App.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Steward.App.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        SystemBackdrop = new MicaBackdrop();
        ViewModel.OwnerWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
    }

    public MainViewModel ViewModel { get; }
}
