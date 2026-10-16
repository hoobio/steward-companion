using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Steward.Desktop.Styles;

public static class PopupPlacement
{
    // WinUI centres a mouse tooltip on the pointer with its bottom edge this far above it (the version, status and lock tooltips of the baseline).
    private const double ToolTipGap = 20;

    private static WeakReference<TopLevel>? _pointerTop;
    private static Point _pointer;

    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        ToolTip.TipProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            control.SetValue(ToolTip.PlacementProperty, PlacementMode.Custom, BindingPriority.Style);
            control.SetValue(ToolTip.CustomPopupPlacementCallbackProperty, PlaceToolTip, BindingPriority.Style);
        });
        // WinUI tooltips take no pointer input; a non-hit-testable Popup passes that to its overlay host and to a native popup window (HTTRANSPARENT on Win32, an empty input shape on X11).
        Popup.ChildProperty.Changed.AddClassHandler<Popup>((popup, e) =>
        {
            if (e.NewValue is ToolTip)
            {
                popup.IsHitTestVisible = false;
            }
        });
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>(TrackPointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        ComboBox.IsDropDownOpenProperty.Changed.AddClassHandler<ComboBox>((combo, _) =>
        {
            if (combo.IsDropDownOpen)
            {
                OverlayDropDown(combo);
            }
        });
    }

    private static void TrackPointer(TopLevel top, PointerEventArgs e)
    {
        _pointerTop = new WeakReference<TopLevel>(top);
        _pointer = e.GetPosition(top);
    }

    private static void PlaceToolTip(CustomPopupPlacement placement)
    {
        var target = placement.Target;
        placement.Anchor = PopupAnchor.Top;
        placement.Gravity = PopupGravity.Top;
        placement.Offset = default;
        if (_pointerTop?.TryGetTarget(out var top) == true && TopLevel.GetTopLevel(target) == top && placement.AnchorRectangle.Inflate(1).Contains(_pointer))
        {
            placement.AnchorRectangle = new Rect(_pointer.X - 0.5, _pointer.Y - ToolTipGap, 1, 2 * ToolTipGap);
        }
    }

    private static void OverlayDropDown(ComboBox combo)
    {
        if (combo.GetVisualDescendants().OfType<Popup>().FirstOrDefault(popup => popup.Name == "PART_Popup") is not { } popup)
        {
            return;
        }

        popup.Placement = PlacementMode.AnchorAndGravity;
        popup.PlacementAnchor = PopupAnchor.TopLeft;
        popup.PlacementGravity = PopupGravity.BottomRight;
        Dispatcher.UIThread.Post(() =>
        {
            if (combo.IsDropDownOpen
                && combo.ContainerFromIndex(Math.Max(0, combo.SelectedIndex)) is Control item
                && popup.Child is { } child
                && item.TranslatePoint(default, child) is { } at)
            {
                popup.VerticalOffset = (combo.Bounds.Height - item.Bounds.Height) / 2 - at.Y;
            }
        }, DispatcherPriority.Loaded);
    }
}
