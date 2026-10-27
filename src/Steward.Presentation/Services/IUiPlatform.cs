using Steward.Core;

namespace Steward.App.Services;

public interface IUiPlatform
{
    nint OwnerWindowHandle { get; }

    bool HasThreadAccess { get; }

    bool TryEnqueue(Action action);

    UiTimer CreateTimer();

    object ImageFromUri(Uri uri, int? decodeWidth);

    object ImageFromAsset(string assetPath, int? decodeWidth);

    bool HasAsset(string assetPath);

    object ImageFromBytes(byte[] encoded, int decodeWidth, Action onFailed);

    Task<object> ImageFromPremultipliedBgraAsync(DecodedImage image);

    Task<DecodedImage> DecodeBgraAsync(byte[] encoded, int? squareSize);

    void SetClipboardText(string text);

    Task<string?> GetClipboardTextAsync();

    Task<string?> PickFolderAsync();
}

public abstract class UiTimer
{
    public abstract TimeSpan Interval { get; set; }

    public abstract bool IsRepeating { get; set; }

    public abstract bool IsRunning { get; }

    public event EventHandler? Tick;

    public void Start() => OnStart();

    public void Stop() => OnStop();

    protected abstract void OnStart();

    protected abstract void OnStop();

    protected void RaiseTick() => Tick?.Invoke(this, EventArgs.Empty);
}
