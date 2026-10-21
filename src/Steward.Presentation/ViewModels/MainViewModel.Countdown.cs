using CommunityToolkit.Mvvm.ComponentModel;

using Steward.App.Services;
using Steward.Core;

namespace Steward.App.ViewModels;

public sealed record ScheduleRow(string Name, string When, string Status);

public sealed partial class MainViewModel
{
    private UiTimer? _countdownTimer;

    [ObservableProperty]
    public partial string? LaunchCountdown { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleVisibility))]
    public partial IReadOnlyList<ScheduleRow> ScheduleRows { get; private set; } = [];

    public bool ScheduleVisibility => ScheduleRows.Count > 0;

    private void StartCountdown()
    {
        UpdateCountdown();
        if (LaunchCountdown is null)
        {
            return;
        }

        _countdownTimer = _platform.CreateTimer();
        _countdownTimer.Interval = TimeSpan.FromSeconds(30);
        _countdownTimer.IsRepeating = true;
        _countdownTimer.Tick += (_, _) => UpdateCountdown();
        _countdownTimer.Start();
    }

    private void UpdateCountdown()
    {
        var now = DateTimeOffset.Now;
        LaunchCountdown = ForeverSchedule.TitleCountdown(now);
        ScheduleRows = [.. ForeverSchedule.Current(now).Select(schedule => new ScheduleRow(
            schedule.Name,
            (now < schedule.Start ? schedule.Start : schedule.End ?? schedule.Start).ToLocalTime().ToString("ddd d MMM, HH:mm", System.Globalization.CultureInfo.CurrentCulture),
            ForeverSchedule.Status(schedule, now)))];
        if (LaunchCountdown is null)
        {
            _countdownTimer?.Stop();
        }
    }
}
