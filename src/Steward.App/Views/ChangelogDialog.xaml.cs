using Microsoft.UI.Xaml.Controls;
using Steward.Core;

namespace Steward.App.Views;

public sealed partial class ChangelogDialog : ContentDialog
{
    public ChangelogDialog(string title, IReadOnlyList<ChangelogBlock> blocks)
    {
        InitializeComponent();
        Title = title;
        Blocks.Children.Add(ChangelogView.Build(blocks));
    }
}
