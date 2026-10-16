using Avalonia.Media;

namespace Steward.Desktop.Services;

public sealed class PlatformImage : DrawingImage
{
    public bool HasFailed { get; private set; }

    public event EventHandler? Failed;

    internal void MarkFailed()
    {
        HasFailed = true;
        Failed?.Invoke(this, EventArgs.Empty);
    }
}
