using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using Windows.System;

namespace Steward.App.Views;

public sealed partial class ColumnResizeGrip : ContentControl
{
    private const double KeyStep = 8;

    private readonly Border _line;
    private double? _dragStartX;
    private double _dragStartWidth;
    private bool _pointerOver;

    public ColumnResizeGrip()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        Width = 8;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        _line = new Border
        {
            Width = 1,
            Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = (Brush)Application.Current.Resources["TextFillColorDisabledBrush"],
        };
        Content = new Grid { Background = new SolidColorBrush(Colors.Transparent), Children = { _line } };

        PointerEntered += (_, _) => SetPointerOver(true);
        PointerExited += (_, _) => SetPointerOver(false);
        GotFocus += (_, _) => UpdateLine();
        LostFocus += (_, _) => UpdateLine();
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += (_, e) => EndDrag(e.Pointer);
        PointerCaptureLost += (_, e) => EndDrag(e.Pointer);
        DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            ResetRequested?.Invoke(this, EventArgs.Empty);
        };
        KeyDown += OnKeyDown;
    }

    public Func<(double Width, double Minimum, double Maximum)>? ColumnRange { get; set; }

    public event EventHandler<double>? WidthRequested;

    public event EventHandler? WidthCommitted;

    public event EventHandler? ResetRequested;

    internal (double Width, double Minimum, double Maximum) Range => ColumnRange?.Invoke() ?? (0, 0, 0);

    internal void RequestWidth(double width)
    {
        var (_, minimum, maximum) = Range;
        WidthRequested?.Invoke(this, Math.Clamp(width, minimum, Math.Max(minimum, maximum)));
    }

    internal void Commit() => WidthCommitted?.Invoke(this, EventArgs.Empty);

    protected override AutomationPeer OnCreateAutomationPeer() => new GripAutomationPeer(this);

    private void SetPointerOver(bool value)
    {
        _pointerOver = value;
        UpdateLine();
    }

    private void UpdateLine() =>
        _line.Opacity = _pointerOver || _dragStartX is not null || FocusState != FocusState.Unfocused ? 1 : 0;

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !CapturePointer(e.Pointer))
        {
            return;
        }

        _dragStartX = e.GetCurrentPoint(null).Position.X;
        _dragStartWidth = Range.Width;
        UpdateLine();
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragStartX is { } startX)
        {
            RequestWidth(_dragStartWidth + e.GetCurrentPoint(null).Position.X - startX);
            e.Handled = true;
        }
    }

    private void EndDrag(Pointer pointer)
    {
        if (_dragStartX is null)
        {
            return;
        }

        _dragStartX = null;
        ReleasePointerCapture(pointer);
        UpdateLine();
        Commit();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var step = e.Key switch
        {
            VirtualKey.Left => -KeyStep,
            VirtualKey.Right => KeyStep,
            _ => 0,
        };
        if (step == 0)
        {
            return;
        }

        RequestWidth(Range.Width + step);
        Commit();
        e.Handled = true;
    }

    private sealed partial class GripAutomationPeer(ColumnResizeGrip owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        public bool IsReadOnly => false;

        public double LargeChange => KeyStep * 4;

        public double SmallChange => KeyStep;

        public double Maximum => owner.Range.Maximum;

        public double Minimum => owner.Range.Minimum;

        public double Value => owner.Range.Width;

        public void SetValue(double value)
        {
            owner.RequestWidth(value);
            owner.Commit();
        }

        protected override object GetPatternCore(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.RangeValue ? this : base.GetPatternCore(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Thumb;

        protected override string GetClassNameCore() => nameof(ColumnResizeGrip);
    }
}
