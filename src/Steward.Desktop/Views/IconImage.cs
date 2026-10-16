using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using Steward.Desktop.Services;

namespace Steward.Desktop.Views;

public sealed class IconImage : Image
{
    public event EventHandler? Failed;

    protected override Type StyleKeyOverride => typeof(Image);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);

        base.OnPropertyChanged(change);
        if (change.Property != SourceProperty)
        {
            return;
        }

        if (change.OldValue is PlatformImage previous)
        {
            previous.Failed -= OnSourceFailed;
        }

        if (change.NewValue is PlatformImage image)
        {
            image.Failed += OnSourceFailed;
            if (image.HasFailed)
            {
                Dispatcher.UIThread.Post(() => OnSourceFailed(image, EventArgs.Empty));
            }
        }
    }

    private void OnSourceFailed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, Source))
        {
            Failed?.Invoke(this, EventArgs.Empty);
        }
    }
}
