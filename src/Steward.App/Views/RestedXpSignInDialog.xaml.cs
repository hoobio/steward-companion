using System.ComponentModel;

using Steward.App.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Steward.App.Views;

public sealed partial class RestedXpSignInDialog : ContentDialog
{
    public RestedXpSignInDialog(RestedXpViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;
        InitializeComponent();
        if (viewModel.IsPreview)
        {
            CodeBox.Text = Grouped(viewModel.MfaCode);
        }
        else
        {
            viewModel.ErrorMessage = null;
        }

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    public RestedXpViewModel ViewModel { get; }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RestedXpViewModel.IsSignedIn) && ViewModel.IsSignedIn)
        {
            Hide();
        }
    }

    private static string Grouped(string digits) => digits.Length > 3 ? $"{digits[..3]} {digits[3..]}" : digits;

    private void OnCodeBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (FindDescendant((TextBox)sender, "DeleteButton") is not FrameworkElement deleteButton)
        {
            return;
        }

        deleteButton.Width = 0;
        deleteButton.MinWidth = 0;
        deleteButton.MaxWidth = 0;
        deleteButton.Margin = new Thickness(0);
        deleteButton.Opacity = 0;
        deleteButton.IsHitTestVisible = false;
    }

    private static FrameworkElement? FindDescendant(DependencyObject root, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element && element.Name == name)
            {
                return element;
            }

            if (FindDescendant(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        ViewModel.BackCommand.Execute(null);
        Hide();
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        CodeBox.Text = string.Empty;
        ViewModel.BackCommand.Execute(null);
    }

    private void OnCodeChanged(object sender, TextChangedEventArgs e)
    {
        var box = (TextBox)sender;
        var digits = new string([.. box.Text.Where(char.IsAsciiDigit).Take(6)]);
        ViewModel.MfaCode = digits;

        var grouped = Grouped(digits);
        if (!string.Equals(box.Text, grouped, StringComparison.Ordinal))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                box.Text = grouped;
                box.SelectionStart = grouped.Length;
            });
        }
    }
}
