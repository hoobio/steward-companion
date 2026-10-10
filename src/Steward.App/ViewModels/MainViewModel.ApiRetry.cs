using Steward.Core;
using Steward.Core.Diagnostics;

using Microsoft.UI.Dispatching;

namespace Steward.App.ViewModels;

public sealed partial class MainViewModel
{
    private const string ApiUnavailableMessage = "Steward APIs are temporarily unavailable.";

    private readonly Dictionary<string, Func<Task<bool>>> _apiRetries = [];
    private DispatcherQueueTimer? _apiRetryTimer;
    private DispatcherQueueTimer? _apiGraceTimer;
    private int _apiRetryAttempt;
    private bool _isRetryingApi;

    private bool HasApiRetry => _apiRetries.Count > 0;

    private void ReportFailure(Exception ex, string retryKey, Func<Task<bool>> retry)
    {
        if (!TransientHttp.IsRetryable(ex))
        {
            StatusMessage = ex.Message;
            return;
        }

        _apiRetries[retryKey] = retry;
        StartApiGrace();
        NotifyStatusAction();
        if (!_isRetryingApi)
        {
            ScheduleApiRetry();
        }
    }

    private void StartApiGrace()
    {
        if (StatusMessage == ApiUnavailableMessage || _apiGraceTimer is { IsRunning: true })
        {
            return;
        }

        if (_apiGraceTimer is null)
        {
            _apiGraceTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _apiGraceTimer.IsRepeating = false;
            _apiGraceTimer.Interval = TimeSpan.FromSeconds(5);
            _apiGraceTimer.Tick += (_, _) =>
            {
                if (HasApiRetry)
                {
                    StatusMessage = ApiUnavailableMessage;
                    NotifyStatusAction();
                }
            };
        }

        _apiGraceTimer.Start();
    }

    private void RetryApiNow()
    {
        if (HasApiRetry)
        {
            _ = RetryApiAsync();
        }
    }

    private void ResolveApiRetry(string retryKey)
    {
        if (_apiRetries.Remove(retryKey) && !_isRetryingApi)
        {
            FinishApiRetries();
        }
    }

    private void ScheduleApiRetry()
    {
        if (_apiRetryTimer is null)
        {
            _apiRetryTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _apiRetryTimer.IsRepeating = false;
            _apiRetryTimer.Tick += (_, _) => _ = RetryApiAsync();
        }

        _apiRetryTimer.Interval = TransientHttp.Backoff(_apiRetryAttempt);
        _apiRetryTimer.Start();
    }

    private async Task RetryApiAsync()
    {
        if (_isRetryingApi)
        {
            return;
        }

        _apiRetryTimer?.Stop();
        _isRetryingApi = true;
        try
        {
            foreach (var (key, retry) in _apiRetries.ToList())
            {
                bool succeeded;
                try
                {
                    succeeded = await retry().ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Retry of {key} failed");
                    succeeded = false;
                }

                if (succeeded)
                {
                    _apiRetries.Remove(key);
                }
            }
        }
        finally
        {
            _isRetryingApi = false;
        }

        _apiRetryAttempt++;
        FinishApiRetries();
    }

    private void FinishApiRetries()
    {
        if (HasApiRetry)
        {
            ScheduleApiRetry();
        }
        else
        {
            _apiRetryAttempt = 0;
            _apiRetryTimer?.Stop();
            _apiGraceTimer?.Stop();
            if (StatusMessage == ApiUnavailableMessage && Failure == GateFailure.None)
            {
                StatusMessage = null;
            }
        }

        NotifyStatusAction();
    }

    private void NotifyStatusAction()
    {
        OnPropertyChanged(nameof(StatusActionVisibility));
        OnPropertyChanged(nameof(StatusActionLabel));
    }
}
