using FluentAvalonia.UI.Controls;

using Steward.Core;

namespace Steward.Desktop.Views;

public sealed partial class ChangelogDialog : FAContentDialog
{
    public ChangelogDialog(string title, IReadOnlyList<ChangelogBlock> blocks, string? dates = null)
    {
        Title = title;
        Blocks = blocks;
        Dates = dates;
        DataContext = this;
        InitializeComponent();
    }

    public IReadOnlyList<ChangelogBlock> Blocks { get; }

    public string? Dates { get; }

    public bool HasDates => !string.IsNullOrEmpty(Dates);

    protected override Type StyleKeyOverride => typeof(FAContentDialog);
}
