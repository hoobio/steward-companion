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
    private static readonly (string Id, int Index, double MinWidth, double MinTableWidth)[] TableColumns =
    [
        ("version", 2, 64, 0),
        ("channel", 3, 64, 760),
        ("source", 4, 56, 840),
    ];

    private static readonly string[] ShrinkOrder = ["source", "channel", "version"];

    private const int NameIndex = 1;
    private const double NameMinWidth = 80;
    private const int ChannelIndex = 3;
    private const int SourceIndex = 4;
    private const int StatusIndex = 5;
    private const int OverflowIndex = 6;
    private const string CompactColumnId = "channel";
    private const int VersionIndex = 2;
    private const double VersionLineSpacing = 4;

    private readonly HashSet<Grid> _tableRows = [];
    private readonly TextBlock _versionMeasure = new() { TextWrapping = TextWrapping.NoWrap };
    private readonly Dictionary<Grid, double> _versionNeeds = [];
    private double _channelNeed;
    private double _sourceNeed;
    private double _statusWidth;
    private bool _needsQueued;

    public HomePage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }

    private double VersionFloor => _versionNeeds.Values.DefaultIfEmpty().Max();

    private static double MeasureColumnContent(Grid row, int index)
    {
        var need = 0.0;
        foreach (var child in row.Children)
        {
            if (child.Visibility != Visibility.Visible || Grid.GetColumn((FrameworkElement)child) != index)
            {
                continue;
            }

            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            need = Math.Max(need, Math.Ceiling(child.DesiredSize.Width));
            child.InvalidateMeasure();
        }

        return need;
    }

    private void QueueNeeds()
    {
        if (!_needsQueued && DispatcherQueue.TryEnqueue(RefreshNeeds))
        {
            _needsQueued = true;
        }
    }

    private void RefreshNeeds()
    {
        _needsQueued = false;
        double channel = 0, source = 0, status = 0;
        foreach (var row in _tableRows.Append(TableHeader))
        {
            channel = Math.Max(channel, MeasureColumnContent(row, ChannelIndex));
            source = Math.Max(source, MeasureColumnContent(row, SourceIndex));
            status = Math.Max(status, MeasureColumnContent(row, StatusIndex));
        }

        if (channel != _channelNeed || source != _sourceNeed || status != _statusWidth)
        {
            (_channelNeed, _sourceNeed, _statusWidth) = (channel, source, status);
            ApplyColumnsToAll();
        }
    }

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => QueueNeeds();

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
        var budget = Math.Max(0, space - _statusWidth - NameMinWidth);
        var applied = shown.ToDictionary(column => column.Id, column => Math.Max(DefaultWidth(column.Id), column.MinWidth));
        applied["version"] = Math.Max(applied["version"], VersionFloor);
        foreach (var id in ShrinkOrder.Where(applied.ContainsKey))
        {
            var excess = applied.Values.Sum() - budget;
            if (excess <= 0)
            {
                break;
            }

            applied[id] = Math.Max(TableColumns.First(column => column.Id == id).MinWidth, applied[id] - excess);
        }

        row.ColumnDefinitions[NameIndex].MinWidth = NameMinWidth;
        row.ColumnDefinitions[NameIndex].Width = new GridLength(1, GridUnitType.Star);
        row.ColumnDefinitions[StatusIndex].Width = new GridLength(_statusWidth);
        foreach (var (id, index, _, _) in TableColumns)
        {
            row.ColumnDefinitions[index].Width = new GridLength(applied.GetValueOrDefault(id));
        }

        if (row.Tag is IAddonTableRow item)
        {
            item.IsCompact = !applied.ContainsKey(CompactColumnId);
        }

        FitVersion(row);
    }

    private double DefaultWidth(string id) => id switch
    {
        "version" => VersionFloor,
        "channel" => _channelNeed,
        _ => _sourceNeed,
    };

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

    private void OnTableSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyColumnsToAll();
        QueueNeeds();
    }

    private void OnTableRowPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is Grid row)
        {
            _tableRows.Add(row);
            if (row.Tag is System.ComponentModel.INotifyPropertyChanged item)
            {
                item.PropertyChanged += OnRowPropertyChanged;
            }

            ApplyColumns(row);
            QueueNeeds();
        }
    }

    private void OnTableRowClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is Grid row)
        {
            _tableRows.Remove(row);
            if (row.Tag is System.ComponentModel.INotifyPropertyChanged item)
            {
                item.PropertyChanged -= OnRowPropertyChanged;
            }

            QueueNeeds();
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
