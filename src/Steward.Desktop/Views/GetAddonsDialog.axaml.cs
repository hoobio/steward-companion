using System.Collections.Specialized;

using FluentAvalonia.UI.Controls;

using Steward.App.ViewModels;

namespace Steward.Desktop.Views;

public sealed partial class GetAddonsDialog : FAContentDialog
{
    public GetAddonsDialog(GetAddonsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        if (viewModel is not null)
        {
            viewModel.Results.CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Reset)
                {
                    ResultsScroller.Offset = default;
                }
            };
        }
    }

    public GetAddonsViewModel ViewModel { get; }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);
}
