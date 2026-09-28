using Steward.App.ViewModels;

using Microsoft.UI.Xaml.Controls;

namespace Steward.App.Views;

public sealed partial class GetAddonsDialog : ContentDialog
{
    public GetAddonsDialog(GetAddonsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public GetAddonsViewModel ViewModel { get; }
}
