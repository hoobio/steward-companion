using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using FluentAvalonia.UI.Controls;

using Steward.App.ViewModels;
using Steward.Desktop.Services;

namespace Steward.Desktop.Views;

public sealed partial class RestedXpSignInDialog : FAContentDialog
{
    public RestedXpSignInDialog(RestedXpViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        if (viewModel.IsPreview)
        {
            CodeBox.Text = Grouped(viewModel.MfaCode);
        }
        else
        {
            viewModel.ErrorMessage = null;
        }

        CodeBox.TextChanged += OnCodeChanged;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    public RestedXpViewModel ViewModel { get; }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RestedXpViewModel.IsSignedIn) && ViewModel.IsSignedIn)
        {
            Hide();
        }
        else if (e.PropertyName is nameof(RestedXpViewModel.MfaVisibility) or nameof(RestedXpViewModel.CredentialsVisibility))
        {
            Dispatcher.UIThread.Post(() => AppDialogs.FocusFirstInput(this));
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Key == Key.Escape && !e.Handled)
        {
            ViewModel.BackCommand.Execute(null);
        }

        base.OnKeyUp(e);
    }

    private static string Grouped(string digits) => digits.Length > 3 ? $"{digits[..3]} {digits[3..]}" : digits;

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.BackCommand.Execute(null);
        Hide();
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        CodeBox.Text = string.Empty;
        ViewModel.BackCommand.Execute(null);
    }

    private void OnCodeChanged(object? sender, TextChangedEventArgs e)
    {
        var box = (TextBox)sender!;
        var digits = new string([.. (box.Text ?? string.Empty).Where(char.IsAsciiDigit).Take(6)]);
        ViewModel.MfaCode = digits;

        var grouped = Grouped(digits);
        if (!string.Equals(box.Text, grouped, StringComparison.Ordinal))
        {
            box.Text = grouped;
            box.CaretIndex = grouped.Length;
        }
    }
}
