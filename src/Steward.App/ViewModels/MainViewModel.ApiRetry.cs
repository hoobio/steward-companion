using System.Net;

using Steward.Core.Diagnostics;

using Microsoft.UI.Dispatching;

namespace Steward.App.ViewModels;

public sealed partial class MainViewModel
{
    private const string ApiUnavailableMessage = "Steward APIs are temporarily unavailable.";

    private static readonly TimeSpan ApiRetryFirstDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ApiRetryMaxDelay = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, Func<Task<bool>>> _apiRetries = [];
    private DispatcherQueueTimer? _apiRetryTimer;
    private int _apiRetryAttempt;
    private bool _isRetryingApi;

    private bool HasApiRetry => _apiRetries.Count > 0;

    private static bool IsServerError(Exception ex) =>
        ex is HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError };

    private static string Describe(Exception ex) => IsServerError(ex) ? ApiUnavailableMessage : ex.Message;

    private void ReportFailure(Exception ex, string retryKey, Func<Task<bool>> retry)
    {
        StatusMessage = Describe(ex);
        if (!IsServerError(ex))
        {
            return;
        }

        _apiRetries[retryKey] = retry;
        NotifyStatusAction();
        if (!_isRetryingApi)
        {
            ScheduleApiRetry();
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

        var delay = ApiRetryFirstDelay * Math.Pow(2, Math.Min(_apiRetryAttempt, 10));
        _apiRetryTimer.Interval = delay < ApiRetryMaxDelay ? delay : ApiRetryMaxDelay;
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
