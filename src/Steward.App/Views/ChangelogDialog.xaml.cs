using Microsoft.UI.Xaml.Controls;

namespace Steward.App.Views;

public sealed partial class ChangelogDialog : ContentDialog
{
    public ChangelogDialog(string title, IReadOnlyList<string> notes)
    {
        InitializeComponent();
        Title = title;
        Notes.ItemsSource = notes;
    }
}
