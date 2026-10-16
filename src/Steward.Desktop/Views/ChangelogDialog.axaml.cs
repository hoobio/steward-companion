using FluentAvalonia.UI.Controls;

using Steward.Core;

namespace Steward.Desktop.Views;

public sealed partial class ChangelogDialog : FAContentDialog
{
    public ChangelogDialog(string title, IReadOnlyList<ChangelogBlock> blocks)
    {
        Title = title;
        Blocks = blocks;
        DataContext = this;
        InitializeComponent();
    }

    public IReadOnlyList<ChangelogBlock> Blocks { get; }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);
}
