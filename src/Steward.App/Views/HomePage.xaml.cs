using Steward.App.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Steward.App.Views;

public sealed partial class HomePage : Page
{
    private static readonly (string Id, int Index, double Stars, double MinWidth, double MinTableWidth)[] TableColumns =
    [
        ("name", 1, 3, 80, 0),
        ("version", 2, 2, 64, 0),
        ("channel", 3, 1.2, 64, 760),
        ("source", 4, 1, 56, 840),
    ];

    private const int StatusIndex = 5;
    private const double StatusStars = 1.5;
    private const double StatusMinWidth = 56;
    private const string CompactColumnId = "channel";

    private readonly HashSet<Grid> _tableRows = [];
    private readonly Dictionary<string, ColumnResizeGrip> _grips;
    private double _actionsWidth = 32;
    private double _defaultSpace;

    public HomePage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        _grips = new()
        {
            ["name"] = NameGrip,
            ["version"] = VersionGrip,
            ["channel"] = ChannelGrip,
            ["source"] = SourceGrip,
        };
        foreach (var (id, grip) in _grips)
        {
            grip.ColumnRange = () => MeasureColumn(id);
            grip.WidthRequested += (_, width) => ResizeColumn(id, width);
            grip.WidthCommitted += (_, _) => ViewModel?.SaveColumnWidths();
            grip.ResetRequested += (_, _) => ResetColumn(id);
        }
    }

    public MainViewModel ViewModel { get; }

    private (double Width, double Minimum, double Maximum) MeasureColumn(string id)
    {
        var column = TableColumns.First(column => column.Id == id);
        var width = TableHeader.ColumnDefinitions[column.Index].ActualWidth;
        var statusSlack = TableHeader.ColumnDefinitions[StatusIndex].ActualWidth - _actionsWidth - StatusMinWidth;
        return (width, column.MinWidth, width + Math.Max(0, statusSlack));
    }

    private void ResizeColumn(string id, double width)
    {
        ViewModel?.SetColumnWidth(id, width);
        ApplyColumnsToAll();
    }

    private void ResetColumn(string id)
    {
        ViewModel?.SetColumnWidth(id, null);
        ViewModel?.SaveColumnWidths();
        ApplyColumnsToAll();
    }

    private void ApplyColumnsToAll()
    {
        foreach (var row in _tableRows.Append(TableHeader))
        {
            ApplyColumns(row);
        }
    }

    private void ApplyColumns(Grid row)
    {
        var tableWidth = TableBody.ActualWidth;
        var shown = TableColumns.Where(column => tableWidth >= column.MinTableWidth).ToList();
        var space = tableWidth - row.Padding.Left - row.Padding.Right - row.ColumnSpacing * (row.ColumnDefinitions.Count - 1)
            - row.ColumnDefinitions[0].Width.Value - _actionsWidth;
        if (_defaultSpace <= 0 && space > 0)
        {
            _defaultSpace = space;
        }

        var stars = TableColumns.Sum(column => column.Stars) + StatusStars;
        var widths = shown.ToDictionary(column => column.Id, column => ViewModel?.ColumnWidth(column.Id) ?? _defaultSpace * column.Stars / stars);
        var total = widths.Values.Sum();
        var available = Math.Max(0, space - StatusMinWidth);
        var scale = total > available && total > 0 ? available / total : 1;

        foreach (var (id, index, _, minWidth, _) in TableColumns)
        {
            var isShown = widths.TryGetValue(id, out var pixels);
            row.ColumnDefinitions[index].Width = new GridLength(isShown ? Math.Max(pixels * scale, minWidth) : 0);
            if (row == TableHeader)
            {
                _grips[id].Visibility = isShown ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        if (row.Tag is IAddonTableRow item)
        {
            item.IsCompact = !widths.ContainsKey(CompactColumnId);
        }
    }

    private void OnTableSizeChanged(object sender, SizeChangedEventArgs e) => ApplyColumnsToAll();

    private void OnTableRowPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is Grid row)
        {
            _tableRows.Add(row);
            ApplyColumns(row);
        }
    }

    private void OnTableRowClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is Grid row)
        {
            _tableRows.Remove(row);
            RecomputeActionsWidth();
        }
    }

    private void OnActionsSizeChanged(object sender, SizeChangedEventArgs e) => RecomputeActionsWidth();

    private void RecomputeActionsWidth()
    {
        var width = _tableRows
            .Select(row => row.FindName("Actions") as FrameworkElement)
            .Where(actions => actions is { Visibility: Visibility.Visible })
            .Select(actions => actions!.ActualWidth)
            .DefaultIfEmpty(0)
            .Max();
        if (Math.Abs(width - _actionsWidth) > 0.5)
        {
            _actionsWidth = width;
            ApplyColumnsToAll();
        }
    }

    private T? TaggedItem<T>(object sender, string handler)
        where T : class
    {
        if (((FrameworkElement)sender).Tag is T item)
        {
            return item;
        }

        ViewModel?.WarnUi($"{handler} could not resolve its {typeof(T).Name} from the element's Tag");
        return null;
    }

    private async void OnChangelogClick(object sender, RoutedEventArgs e)
    {
        if (TaggedItem<AddonRowViewModel>(sender, nameof(OnChangelogClick)) is not { } row)
        {
            return;
        }

        await new ChangelogDialog(row.ChangelogTitle, row.Notes) { XamlRoot = XamlRoot }.ShowAsync();
    }

    private async void OnGetAddonsClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CreateGetAddons() is not { } getAddons)
        {
            return;
        }

        var dialog = new GetAddonsDialog(getAddons) { XamlRoot = XamlRoot };
        _ = getAddons.LoadAsync();
        await dialog.ShowAsync();
    }

    private void OnManifestIconFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (TaggedItem<AddonRowViewModel>(sender, nameof(OnManifestIconFailed)) is { } row)
        {
            row.ManifestIconFailed = true;
        }
    }

    private void OnUpdateAllClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.UpdateAllCommand.CanExecute(null) == true)
        {
            ViewModel.UpdateAllCommand.Execute(null);
        }
        else
        {
            UpdateAllMore.Flyout.ShowAt(UpdateAllMore);
        }
    }
}
