using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Steward.App.ViewModels;
using Steward.Core;
using Steward.Desktop.Services;

namespace Steward.Desktop.Views;

public sealed partial class HomePage : UserControl
{
    private static readonly (string Id, int Index, double MinWidth)[] TableColumns =
    [
        ("version", 2, 64),
        ("channel", 3, 64),
        ("source", 4, 56),
    ];

    private static readonly string[] ShrinkOrder = ["source", "channel", "version"];

    private static readonly HashSet<string> WidthNeutralProperties =
    [
        nameof(AddonRowViewModel.UpdateProgress),
        nameof(AddonRowViewModel.ProgressText),
        nameof(AddonRowViewModel.VersionCellTip),
        nameof(AddonRowViewModel.HasVersionCellTip),
        nameof(AddonRowViewModel.VersionTip),
        nameof(AddonRowViewModel.ReleasedTip),
        nameof(AddonRowViewModel.VersionButtonTip),
        nameof(AddonRowViewModel.VersionPairTip),
        nameof(AddonRowViewModel.SingleChannelTip),
        nameof(AddonRowViewModel.ChangelogTitle),
        nameof(AddonRowViewModel.Changelog),
        nameof(AddonRowViewModel.OutOfDateTip),
        nameof(AddonRowViewModel.LastUpdated),
        nameof(AddonRowViewModel.StatusRank),
        nameof(AddonRowViewModel.CanAutoApply),
        nameof(AddonRowViewModel.IsPendingUpdate),
    ];

    private const int NameIndex = 1;
    private const double NameMinWidth = 80;
    private const int ChannelIndex = 3;
    private const int SourceIndex = 4;
    private const int StatusIndex = 5;
    private const int OverflowIndex = 6;
    private const string CompactColumnId = "channel";
    private const int VersionIndex = 2;
    private const double VersionLineSpacing = 4;
    private const double WidePageMinWidth = 900;

    private readonly Dictionary<Border, INotifyPropertyChanged?> _tableRows = [];
    private readonly TextBlock _versionMeasure = new() { TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock _releasedMeasure = new() { TextWrapping = TextWrapping.NoWrap };
    private readonly Dictionary<Border, double> _versionNeeds = [];
    private double _nameNeed;
    private double _versionCellNeed;
    private double _channelNeed;
    private double _sourceNeed;
    private double _statusWidth;
    private bool _needsQueued;
    private bool _isWidePage = true;

    public HomePage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SizeChanged += (_, _) => UpdateWidePage();
        UpdateCheckedLabel();
    }

    public MainViewModel ViewModel { get; }

    private double VersionFloor => _versionNeeds.Values.DefaultIfEmpty().Max();

    private static Grid Cells(Border row) => (Grid)row.Child!;

    private static double MeasureColumnContent(Border row, int index)
    {
        var need = 0.0;
        foreach (var child in Cells(row).Children)
        {
            if (!child.IsVisible || Grid.GetColumn(child) != index)
            {
                continue;
            }

            IEnumerable<Control> targets = index == NameIndex && child is StackPanel lines ? lines.Children.Take(2) : [child];
            foreach (var target in targets.Where(target => target.IsVisible))
            {
                need = Math.Max(need, MeasureWidth(target));
            }
        }

        return need;
    }

    private static double MeasureWidth(Control control)
    {
        control.Measure(Size.Infinity);
        var width = Math.Ceiling(control.DesiredSize.Width);
        control.InvalidateMeasure();
        return width;
    }

    private double MeasureVersionCell(Border row)
    {
        if (row.DataContext is not AddonRowViewModel item)
        {
            return MeasureColumnContent(row, VersionIndex);
        }

        var need = MeasureColumnContent(row, ChannelIndex);
        if (!item.VersionPairVisibility && Named<Button>(row, "VersionButton") is { } button)
        {
            need = Math.Max(need, MeasureWidth(button));
        }

        if (item.NoReleasesVisibility && Named<TextBlock>(row, "NoReleasesLine") is { } none)
        {
            need = Math.Max(need, MeasureWidth(none));
        }

        if (item.ReleasedText.Length > 0 && !item.NoReleasesVisibility && Named<TextBlock>(row, "ReleasedLine") is { } released)
        {
            _releasedMeasure.FontFamily = released.FontFamily;
            _releasedMeasure.FontSize = released.FontSize;
            _releasedMeasure.Text = item.ReleasedText;
            need = Math.Max(need, MeasureWidth(_releasedMeasure));
        }

        return need;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CheckedStaleVisibility) or null)
        {
            UpdateCheckedLabel();
        }
    }

    private void UpdateWidePage()
    {
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            _isWidePage = top.ClientSize.Width >= WidePageMinWidth;
            UpdateCheckedLabel();
        }
    }

    private void UpdateCheckedLabel() => CheckedLabel.IsVisible = _isWidePage && ViewModel.CheckedStaleVisibility;

    private IEnumerable<Border> AllRows() => _tableRows.Keys.Append(TableHeader);

    private void QueueNeeds()
    {
        if (!_needsQueued)
        {
            _needsQueued = true;
            Dispatcher.UIThread.Post(RefreshNeeds);
        }
    }

    private void RefreshNeeds()
    {
        _needsQueued = false;
        double version = 0, channel = 0, source = 0, status = 0;
        var names = new List<double>();
        foreach (var row in AllRows())
        {
            names.Add(MeasureColumnContent(row, NameIndex));
            version = Math.Max(version, MeasureVersionCell(row));
            channel = Math.Max(channel, MeasureColumnContent(row, ChannelIndex));
            source = Math.Max(source, MeasureColumnContent(row, SourceIndex));
            status = Math.Max(status, MeasureColumnContent(row, StatusIndex));
        }

        var name = ColumnFit.TypicalMax(names);
        if (name != _nameNeed || version != _versionCellNeed || channel != _channelNeed || source != _sourceNeed || status != _statusWidth)
        {
            (_nameNeed, _versionCellNeed, _channelNeed, _sourceNeed, _statusWidth) = (name, version, channel, source, status);
            ApplyColumnsToAll();
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null || !WidthNeutralProperties.Contains(e.PropertyName))
        {
            QueueNeeds();
        }
    }

    private void ApplyColumnsToAll()
    {
        foreach (var row in AllRows().ToList())
        {
            ApplyColumns(row);
        }
    }

    private void ApplyColumns(Border row)
    {
        var cells = Cells(row);
        var columns = cells.ColumnDefinitions;
        var space = TableBody.Bounds.Width - row.Padding.Left - row.Padding.Right - cells.ColumnSpacing * (columns.Count - 1)
            - columns[0].Width.Value - columns[OverflowIndex].Width.Value;
        var needName = Math.Max(_nameNeed, NameMinWidth);
        var shown = TableColumns.ToList();
        while (shown.Count > 1 && needName + _statusWidth + shown.Sum(column => Math.Max(DefaultWidth(column.Id), column.MinWidth)) > space)
        {
            shown.RemoveAt(shown.Count - 1);
        }

        var applied = shown.ToDictionary(column => column.Id, column => Math.Max(DefaultWidth(column.Id), column.MinWidth));
        var needVersion = applied["version"];
        var pool = space - _statusWidth - applied.Where(pair => pair.Key != "version").Sum(pair => pair.Value);
        double name;
        if (needName + needVersion <= pool)
        {
            name = pool / 2 >= needName && pool / 2 >= needVersion ? pool / 2 : needName > pool / 2 ? needName : pool - needVersion;
            applied["version"] = pool - name;
        }
        else
        {
            var budget = Math.Max(0, space - _statusWidth - NameMinWidth);
            foreach (var id in ShrinkOrder.Where(applied.ContainsKey))
            {
                var excess = applied.Values.Sum() - budget;
                if (excess <= 0)
                {
                    break;
                }

                applied[id] = Math.Max(TableColumns.First(column => column.Id == id).MinWidth, applied[id] - excess);
            }

            name = Math.Max(NameMinWidth, space - _statusWidth - applied.Values.Sum());
        }

        columns[NameIndex].MinWidth = NameMinWidth;
        columns[NameIndex].Width = new GridLength(name);
        columns[StatusIndex].Width = new GridLength(_statusWidth);
        foreach (var (id, index, _) in TableColumns)
        {
            columns[index].Width = new GridLength(applied.GetValueOrDefault(id));
        }

        if (row.DataContext is IAddonTableRow item && row != TableHeader)
        {
            item.IsCompact = !applied.ContainsKey(CompactColumnId);
        }

        FitVersion(row);
    }

    private double DefaultWidth(string id) => id switch
    {
        "version" => Math.Max(VersionFloor, _versionCellNeed),
        "channel" => _channelNeed,
        _ => _sourceNeed,
    };

    private static T? Named<T>(Visual root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name);

    private void FitVersion(Border row)
    {
        if (row.DataContext is not AddonRowViewModel item || Named<TextBlock>(row, "VersionPair") is not { } pair || Named<TextBlock>(row, "ChangelogIcon") is not { } icon)
        {
            return;
        }

        _versionMeasure.FontFamily = pair.FontFamily;
        _versionMeasure.FontSize = pair.FontSize;
        _versionMeasure.Inlines ??= [];
        _versionMeasure.Inlines.Clear();
        foreach (var run in pair.Inlines?.OfType<Run>() ?? [])
        {
            var copy = new Run { Text = run.Text };
            if (run.IsSet(TextElement.FontFamilyProperty))
            {
                copy.FontFamily = run.FontFamily;
                copy.FontSize = run.FontSize;
            }

            _versionMeasure.Inlines.Add(copy);
        }

        _versionMeasure.Measure(Size.Infinity);
        var needed = item.VersionPairVisibility
            ? Math.Ceiling(_versionMeasure.DesiredSize.Width) + VersionLineSpacing + (item.HasChangelog ? icon.Width + VersionLineSpacing : 0)
            : 0;
        var floor = VersionFloor;
        _versionNeeds[row] = needed;
        if (VersionFloor != floor)
        {
            ApplyColumnsToAll();
            return;
        }

        item.IsVersionStacked = needed > Cells(row).ColumnDefinitions[VersionIndex].Width.Value;
    }

    private void OnVersionSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        for (var element = sender as Visual; element is not null; element = element.GetVisualParent())
        {
            if (element is Border row && _tableRows.ContainsKey(row))
            {
                FitVersion(row);
                return;
            }
        }
    }

    private void OnManifestIconFailed(object? sender, EventArgs e)
    {
        if (sender is Control { DataContext: AddonRowViewModel row })
        {
            row.ManifestIconFailed = true;
        }
    }

    private void OnRowAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Border row && !_tableRows.ContainsKey(row))
        {
            _tableRows[row] = null;
            Track(row);
        }
    }

    private void OnRowDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Border row || !_tableRows.Remove(row, out var item))
        {
            return;
        }

        if (item is not null)
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

    private void OnRowDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is Border row && _tableRows.ContainsKey(row))
        {
            Track(row);
        }
    }

    private void Track(Border row)
    {
        if (_tableRows[row] is { } previous)
        {
            previous.PropertyChanged -= OnRowPropertyChanged;
        }

        var item = row.DataContext as INotifyPropertyChanged;
        if (item is not null)
        {
            item.PropertyChanged += OnRowPropertyChanged;
        }

        _tableRows[row] = item;
        ApplyColumns(row);
        QueueNeeds();
    }

    private void OnTableSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyColumnsToAll();
        QueueNeeds();
    }

    private TopLevel Owner => TopLevel.GetTopLevel(this)!;

    private T? DataItem<T>(object? sender, string handler)
        where T : class
    {
        if ((sender as Control)?.DataContext is T item)
        {
            return item;
        }

        ViewModel.WarnUi($"{handler} could not resolve its {typeof(T).Name} from the element's DataContext");
        return null;
    }

    private async void OnExtraFoldersClick(object? sender, RoutedEventArgs e)
    {
        if (DataItem<IAddonTableRow>(sender, nameof(OnExtraFoldersClick)) is { } row)
        {
            var dialog = new AddonFoldersDialog(row.DisplayName, row.ExtraFolders, ViewModel.Platform, ViewModel.Logger);
            await AppDialogs.ShowAsync(dialog, Owner);
            while (dialog.ChangelogRequest is { } folder)
            {
                dialog.ClearChangelogRequest();
                await AppDialogs.ShowAsync(new ChangelogDialog(folder.ChangelogTitle ?? folder.Folder, folder.Changelog!), Owner);
                await AppDialogs.ShowAsync(dialog, Owner);
            }
        }
    }

    private async void OnChangelogClick(object? sender, RoutedEventArgs e)
    {
        if (DataItem<AddonRowViewModel>(sender, nameof(OnChangelogClick)) is { } row)
        {
            await AppDialogs.ShowAsync(new ChangelogDialog(row.ChangelogTitle, row.Changelog), Owner);
        }
    }

    private async void OnGetAddonsClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.CreateGetAddons() is not { } getAddons)
        {
            return;
        }

        var dialog = new GetAddonsDialog(getAddons);
        _ = getAddons.LoadAsync();
        await AppDialogs.ShowAsync(dialog, Owner);
    }

    private void OnUpdateAllClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.UpdateAllCommand.CanExecute(null))
        {
            ViewModel.UpdateAllCommand.Execute(null);
        }
        else
        {
            UpdateAllMore.Flyout?.ShowAt(UpdateAllMore);
        }
    }
}
