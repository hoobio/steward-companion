using System.Text.Json;

using CommunityToolkit.Mvvm.ComponentModel;

using Steward.App.Services;
using Steward.Core;
using Steward.Core.Diagnostics;

namespace Steward.App.ViewModels;

public sealed record ScheduleRow(string Name, string When, string Status);

public sealed partial class MainViewModel
{
    private UiTimer? _countdownTimer;
    private IReadOnlyList<ProductOverride> _productOverrides = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountdownVisibility))]
    public partial string? LaunchCountdown { get; internal set; }

    public bool CountdownVisibility => LaunchCountdown is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleVisibility))]
    public partial IReadOnlyList<ScheduleRow> ScheduleRows { get; private set; } = [];

    public bool ScheduleVisibility => ScheduleRows.Count > 0;

    [ObservableProperty]
    public partial string? ScheduleTitle { get; private set; }

    private async Task SyncProductsAsync()
    {
        IReadOnlyList<ProductOverride> products;
        try
        {
            products = await _stewardClient.GetProductsAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.Warn(ex, "Product fetch failed, keeping the last known products");
            return;
        }

        if (JsonSerializer.Serialize([.. products], CompanionJsonContext.Default.ListProductOverride)
            == JsonSerializer.Serialize([.. _productOverrides], CompanionJsonContext.Default.ListProductOverride))
        {
            return;
        }

        _stateStore.Save(_stateStore.Load() with { ProductOverrides = products.Count > 0 ? [.. products] : null });
        ApplyProducts(products);
    }

    internal void ApplyProducts(IReadOnlyList<ProductOverride> products)
    {
        _productOverrides = products;
        _supportedProducts = AppProducts.Names(_bundledProducts, products);
        ProductIcon.Urls = products
            .Where(product => product.IconUrl is not null && _bundledProducts.ContainsKey(product.Code))
            .ToDictionary(product => product.Code, product => product.IconUrl!);
        foreach (var install in Installs)
        {
            install.RefreshProduct(install.Install.ProductCode is { } code ? _supportedProducts.GetValueOrDefault(code) : null);
        }

        RenumberInstalls();
        OnPropertyChanged(nameof(GameVersions));
        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        var now = DateTimeOffset.Now;
        var product = SelectedInstall?.Install.ProductCode;
        var events = AppProducts.Schedule(product, _productOverrides);
        LaunchCountdown = ForeverSchedule.TitleCountdown(events, now);
        ScheduleRows = [.. ForeverSchedule.Current(events, now).Select(schedule => new ScheduleRow(
            schedule.Name,
            (now < schedule.Start ? schedule.Start : schedule.End ?? schedule.Start).ToLocalTime().ToString("ddd d MMM, HH:mm", System.Globalization.CultureInfo.CurrentCulture),
            ForeverSchedule.Status(schedule, now)))];
        ScheduleTitle = product is null ? null : _supportedProducts.GetValueOrDefault(product);
        if (LaunchCountdown is null && ScheduleRows.Count == 0)
        {
            _countdownTimer?.Stop();
            return;
        }

        if (_countdownTimer is null)
        {
            _countdownTimer = _platform.CreateTimer();
            _countdownTimer.Interval = TimeSpan.FromSeconds(30);
            _countdownTimer.IsRepeating = true;
            _countdownTimer.Tick += (_, _) => UpdateCountdown();
        }

        if (!_countdownTimer.IsRunning)
        {
            _countdownTimer.Start();
        }
    }
}
