using Steward.App.Services;
using Steward.App.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

using Windows.Foundation;

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

    private static readonly string[] WidestActions = ["Switch to pre-release", "Update on CurseForge", "Sign in to RestedXP"];

    private const int StatusIndex = 5;
    private const int OverflowIndex = 6;
    private const double StatusStars = 1.5;
    private const double ActionFontSize = 13;
    private const double ActionPadding = 26;
    private const string CompactColumnId = "channel";
    private const int VersionIndex = 2;
    private const double VersionLineSpacing = 4;

    private readonly HashSet<Grid> _tableRows = [];
    private readonly TextBlock _versionMeasure = new() { TextWrapping = TextWrapping.NoWrap };
    private readonly Dictionary<Grid, double> _versionNeeds = [];
    private readonly Dictionary<string, ColumnResizeGrip> _grips;
    private double _statusMinWidth;
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

    private double VersionFloor => _versionNeeds.Values.DefaultIfEmpty().Max();

    private double StatusMinWidth => _statusMinWidth > 0 ? _statusMinWidth : _statusMinWidth = WidestActions.Max(MeasureAction);

    private static double MeasureAction(string label)
    {
        var text = new TextBlock { Text = label, FontSize = ActionFontSize, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Ceiling(text.DesiredSize.Width + ActionPadding);
    }

    private (double Width, double Minimum, double Maximum) MeasureColumn(string id)
    {
        var column = TableColumns.First(column => column.Id == id);
        var width = TableHeader.ColumnDefinitions[column.Index].ActualWidth;
        var statusSlack = TableHeader.ColumnDefinitions[StatusIndex].ActualWidth - StatusMinWidth;
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
            - row.ColumnDefinitions[0].Width.Value - row.ColumnDefinitions[OverflowIndex].Width.Value;
        if (_defaultSpace <= 0 && space > 0)
        {
            _defaultSpace = space;
        }

        var stars = TableColumns.Sum(column => column.Stars) + StatusStars;
        var widths = shown.ToDictionary(column => column.Id, column => ViewModel?.ColumnWidth(column.Id) ?? _defaultSpace * column.Stars / stars);
        var total = widths.Values.Sum();
        var available = Math.Max(0, space - StatusMinWidth);
        var scale = total > available && total > 0 ? available / total : 1;
        var applied = shown.ToDictionary(column => column.Id, column => Math.Max(widths[column.Id] * scale, column.MinWidth));
        if (applied.TryGetValue("version", out var version) && VersionFloor > version)
        {
            applied["version"] = Math.Min(VersionFloor, version + Math.Max(0, available - applied.Values.Sum()));
        }

        foreach (var (id, index, _, _, _) in TableColumns)
        {
            var isShown = applied.TryGetValue(id, out var pixels);
            row.ColumnDefinitions[index].Width = new GridLength(isShown ? pixels : 0);
            if (row == TableHeader)
            {
                _grips[id].Visibility = isShown ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        if (row.Tag is IAddonTableRow item)
        {
            item.IsCompact = !applied.ContainsKey(CompactColumnId);
        }

        FitVersion(row);
    }

    private void FitVersion(Grid row)
    {
        if (row.Tag is not AddonRowViewModel item || row.FindName("VersionPair") is not TextBlock pair || row.FindName("ChangelogIcon") is not FontIcon icon)
        {
            return;
        }

        _versionMeasure.FontFamily = pair.FontFamily;
        _versionMeasure.FontSize = pair.FontSize;
        _versionMeasure.Inlines.Clear();
        foreach (var run in pair.Inlines.OfType<Run>())
        {
            var copy = new Run { Text = run.Text };
            if (run.ReadLocalValue(TextElement.FontFamilyProperty) != DependencyProperty.UnsetValue)
            {
                copy.FontFamily = run.FontFamily;
                copy.FontSize = run.FontSize;
            }

            _versionMeasure.Inlines.Add(copy);
        }

        _versionMeasure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var needed = item.VersionPairVisibility == Visibility.Visible
            ? Math.Ceiling(_versionMeasure.DesiredSize.Width) + VersionLineSpacing + (item.HasChangelog ? icon.Width + VersionLineSpacing : 0)
            : 0;
        var floor = VersionFloor;
        _versionNeeds[row] = needed;
        if (VersionFloor != floor)
        {
            ApplyColumnsToAll();
            return;
        }

        item.IsVersionStacked = needed > row.ColumnDefinitions[VersionIndex].Width.Value;
    }

    private void OnVersionSizeChanged(object sender, SizeChangedEventArgs e)
    {
        for (var element = sender as FrameworkElement; element is not null; element = VisualTreeHelper.GetParent(element) as FrameworkElement)
        {
            if (element is Grid row && _tableRows.Contains(row))
            {
                FitVersion(row);
                return;
            }
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
            var floor = VersionFloor;
            _versionNeeds.Remove(row);
            if (VersionFloor != floor)
            {
                ApplyColumnsToAll();
            }
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

        await AppDialogs.ShowAsync(new ChangelogDialog(row.ChangelogTitle, row.Changelog), XamlRoot);
    }

    private async void OnGetAddonsClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CreateGetAddons() is not { } getAddons)
        {
            return;
        }

        var dialog = new GetAddonsDialog(getAddons);
        _ = getAddons.LoadAsync();
        await AppDialogs.ShowAsync(dialog, XamlRoot);
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
