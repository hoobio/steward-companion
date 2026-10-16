using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Steward.Desktop.Styles;

// WinUI lays text out on Segoe UI (ascent 2210, descent 514 of 2048) and icons on Segoe Fluent Icons (ascent 1 em, no descent); the bundled fonts' metrics differ.
public static class SegoeMetrics
{
    private static readonly Dictionary<string, Metrics> Calibrations = new()
    {
        ["Hanken Grotesk"] = new(Ascent: 1.000, Descent: 0.303, TargetAscent: 2210d / 2048, TargetDescent: 514d / 2048, CapHeight: 0.697),
        ["FluentSystemIcons-Resizable"] = new(Ascent: 1.000, Descent: 0.000, TargetAscent: 1.000, TargetDescent: 0.000, InkScale: 5d / 4),
    };

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(SegoeMetrics));

    public static readonly AttachedProperty<bool> IsFreeProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsFree", typeof(SegoeMetrics));

    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);

    public static void SetIsFree(Control control, bool value) => control.SetValue(IsFreeProperty, value);

    public static bool GetIsFree(Control control) => control.GetValue(IsFreeProperty);

    public static readonly IMultiValueConverter LineHeightConverter = new FuncMultiValueConverter<object?, double>(values =>
        Calibrated(values) is var (size, family) ? LineHeight(family, size) : double.NaN);

    static SegoeMetrics()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.GetNewValue<bool>())
            {
                control.AttachedToVisualTree += OnAttached;
                control.DetachedFromVisualTree += OnDetached;
                control.PropertyChanged += OnPropertyChanged;
                Attach(control);
            }
            else
            {
                control.AttachedToVisualTree -= OnAttached;
                control.DetachedFromVisualTree -= OnDetached;
                control.PropertyChanged -= OnPropertyChanged;
                Detach(control);
            }
        });
    }

    public static double LineHeight(string family, double fontSize)
    {
        var metrics = Calibrations[family];
        return (metrics.TargetAscent + metrics.TargetDescent) * fontSize;
    }

    // WinUI rounds Segoe's ascent and descent to device pixels, centres them in the line box rounded up and snaps the baseline half up; hinted Segoe capitals ink a row higher than Hanken's on that baseline, so boxed text centres its capitals, at most one device pixel above it.
    public static double BaselineShift(string family, double fontSize, double scale, bool free = true)
    {
        var metrics = Calibrations[family];
        var lineHeight = LineHeight(family, fontSize);
        var natural = (metrics.Ascent * fontSize + Math.Max(0, lineHeight - (metrics.Ascent + metrics.Descent) * fontSize) / 2) * scale;
        var box = Math.Ceiling(Math.Round(lineHeight * scale, 8, MidpointRounding.ToZero));
        var ascent = Math.Round(metrics.TargetAscent * fontSize * scale, MidpointRounding.AwayFromZero);
        var descent = Math.Round(metrics.TargetDescent * fontSize * scale, MidpointRounding.AwayFromZero);
        var baseline = Math.Floor((box - ascent - descent) / 2 + ascent + 0.5);
        if (!free && metrics.CapHeight is { } cap)
        {
            baseline = Math.Clamp(Math.Round((box + cap * fontSize * scale) / 2, MidpointRounding.AwayFromZero), baseline - 1, baseline);
        }

        return (baseline - natural) / scale;
    }

    public static ITransform? Transform(string family, double fontSize, double scale, bool free) =>
        Calibrations[family].InkScale is var ink and not 1 ? new ScaleTransform(ink, ink)
        : BaselineShift(family, fontSize, scale, free) is var shift and not 0 ? new TranslateTransform(0, shift) : null;

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) => Attach((Control)sender!);

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Detach((Control)sender!);

    private static readonly Dictionary<Control, TopLevel> Hosts = [];

    private static void Attach(Control control)
    {
        if (TopLevel.GetTopLevel(control) is not { } top)
        {
            return;
        }

        Detach(control);
        Hosts[control] = top;
        top.ScalingChanged += OnScalingChanged;
        Update(control);
    }

    private static void Detach(Control control)
    {
        if (Hosts.Remove(control, out var top))
        {
            top.ScalingChanged -= OnScalingChanged;
        }
    }

    private static void OnScalingChanged(object? sender, EventArgs e)
    {
        foreach (var (control, top) in Hosts.ToList())
        {
            if (top == sender)
            {
                Update(control);
            }
        }
    }

    private static void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name is "FontSize" or "FontFamily" || e.Property == IsFreeProperty)
        {
            Update((Control)sender!);
        }
    }

    private static void Update(Control control)
    {
        var (size, family) = control switch
        {
            TextBlock text => (text.FontSize, text.FontFamily),
            TextPresenter presenter => (presenter.FontSize, presenter.FontFamily),
            _ => (double.NaN, FontFamily.Default),
        };
        var scale = Hosts.TryGetValue(control, out var top) ? top.RenderScaling : 1;
        var name = Resolve(family);
        control.SetValue(Visual.RenderTransformProperty, name is null || double.IsNaN(size) ? null : Transform(name, size, scale, GetIsFree(control)), BindingPriority.Style);
    }

    private static (double Size, string Family)? Calibrated(IEnumerable<object?> values) =>
        values.ToList() is [double size, FontFamily family] && Resolve(family) is { } name ? (size, name) : null;

    // FAContentDialog's string title names the family "Default", which renders the app's default family, so the rendered typeface decides.
    private static string? Resolve(FontFamily family) =>
        Calibrations.ContainsKey(family.Name) ? family.Name
        : FontManager.Current.TryGetGlyphTypeface(new Typeface(family), out var typeface) && Calibrations.ContainsKey(typeface.FamilyName) ? typeface.FamilyName
        : null;

    // InkScale: Fluent System Icons' 20px set inks 0.8 em (16 of 20 units; globe E99B 800 of 1000) where Segoe Fluent Icons inks the full em (globe E774 1000 of 1000).
    private sealed record Metrics(double Ascent, double Descent, double TargetAscent, double TargetDescent, double? CapHeight = null, double InkScale = 1);
}
