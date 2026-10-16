using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAvalonia.UI.Controls;

using Steward.Desktop.Services;

namespace Steward.Desktop.Tests;

public sealed class TextCentringTests(AvaloniaSession session)
{
    private const string Caps = "HXE";
    private const string Descenders = "weakauras gjpqy";

    private static readonly Dictionary<string, Func<string, Sample>> Cases = new()
    {
        ["TextBox"] = text => Self(new TextBox { Height = 32, Width = 240, Text = text }),
        ["TextBox placeholder"] = text => Self(new TextBox { Height = 32, Width = 240, PlaceholderText = text }),
        ["TextBox Get addons search"] = text => Self(new TextBox
        {
            Height = 32,
            Width = 300,
            Text = text,
            InnerRightContent = new TextBlock { Classes = { "Glyph" }, Text = "", FontSize = 12, Margin = new Thickness(0, 0, 10, 0) },
        }),
        ["TextBox Addons filter"] = text => Self(new TextBox { Height = 32, Width = 220, Text = text, Padding = new Thickness(32, 6, 10, 6), VerticalContentAlignment = VerticalAlignment.Center }),
        ["TextBox Addons filter placeholder"] = text => Self(new TextBox { Height = 32, Width = 220, PlaceholderText = text, Padding = new Thickness(32, 6, 10, 6), VerticalContentAlignment = VerticalAlignment.Center }),
        ["TextBox RestedXP sign-in"] = text => Self(new TextBox { MinHeight = 32, Width = 300, FontSize = 13.5, Text = text }),
        ["TextBox RestedXP code"] = text => Self(new TextBox
        {
            Width = 186,
            Height = 38,
            FontFamily = Resource<FontFamily>("MonoFontFamily"),
            FontSize = 17,
            LetterSpacing = 2.04,
            TextAlignment = TextAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = text,
        }),
        ["TextBox sign-in link"] = text => Self(new TextBox
        {
            Width = 300,
            IsReadOnly = true,
            FontFamily = Resource<FontFamily>("MonoFontFamily"),
            FontSize = 12,
            BorderThickness = new Thickness(0),
            MinHeight = 0,
            Padding = new Thickness(10, 8),
            Text = text,
        }),
        ["Button"] = text => Self(new Button { Content = text }),
        ["Button accent"] = text => Self(new Button { Content = text, Theme = Resource<ControlTheme>("AccentButtonStyle") }),
        ["ToggleButton"] = text => Self(new ToggleButton { Content = text }),
        ["HyperlinkButton"] = text => Self(new HyperlinkButton { Content = text }),
        ["ComboBox"] = text => Self(new ComboBox { Height = 32, Width = 150, ItemsSource = new[] { text }, SelectedIndex = 0 }),
        ["ComboBoxItem"] = text =>
        {
            var item = new ComboBoxItem { Content = text, Width = 150 };
            return new Sample(item, () => Part<Border>(item, "LayoutRoot"));
        },
        ["Segmented item"] = text =>
        {
            var item = new ListBoxItem { Content = text };
            return new Sample(new ListBox { Theme = Resource<ControlTheme>("SegmentedStyle"), Items = { item } }, () => item);
        },
        ["CheckBox"] = text =>
        {
            var check = new CheckBox { Content = text };
            return new Sample(check, () => Part<Border>(check, "NormalRectangle"));
        },
        ["RadioButton"] = text =>
        {
            var radio = new RadioButton { Content = text };
            return new Sample(radio, () => radio.GetVisualDescendants().OfType<Ellipse>().First());
        },
        ["ToggleSwitch label"] = text =>
        {
            var toggle = new ToggleSwitch { OnContent = text, OffContent = text };
            return new Sample(toggle, () => toggle.GetVisualDescendants().OfType<Control>().First(part => part.Name == "OuterBorder"));
        },
        ["MenuItem"] = text =>
        {
            var item = new MenuItem { Header = text, Width = 200 };
            return new Sample(item, () => Part<Border>(item, "PART_LayoutRoot"));
        },
        ["ToolTip"] = text => Self(new ToolTip { Content = text }),
        ["InfoBar title"] = text =>
        {
            var bar = new FAInfoBar { IsOpen = true, IsClosable = false, Title = text, Width = 400 };
            return new Sample(bar, () => Part<Ellipse>(bar, "IconBackground"));
        },
        ["InfoBar title and message"] = text =>
        {
            var bar = new FAInfoBar { IsOpen = true, IsClosable = false, Title = text, Message = "Body", Width = 400 };
            return new Sample(bar, () => Part<Ellipse>(bar, "IconBackground"));
        },
        ["TextBlock in a box"] = text => InBox(new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center }),
        ["SelectableTextBlock in a box"] = text => InBox(new SelectableTextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center }),
    };

    public static TheoryData<string> Names => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(Names))]
    public void Text_ink_is_centred_in_its_box(string name) => session.Run(() =>
    {
        var caps = Measure(name, Caps);
        var descenders = Measure(name, Descenders);
        Report(name, caps, descenders);

        Assert.True(Math.Abs(caps.Above - caps.Below) <= 1, $"{name} {Caps}: {caps}");
        Assert.True(Math.Abs(caps.CentreOffset) <= 1, $"{name} {Caps}: {caps}");
    });

    [Fact]
    public void Placeholder_text_sits_where_typed_text_sits() => session.Run(() =>
    {
        Assert.Equal(Measure("TextBox", Caps).InkTop, Measure("TextBox placeholder", Caps).InkTop);
        Assert.Equal(Measure("TextBox Addons filter", Caps).InkTop, Measure("TextBox Addons filter placeholder", Caps).InkTop);
    });

    [Fact]
    public void The_caret_and_selection_sit_on_the_text() => session.Run(() =>
    {
        var ink = Measure("TextBox", Caps);
        var box = new TextBox { Height = 32, Width = 240, Text = Caps, SelectionStart = 0, SelectionEnd = 2 };
        var window = Show(box);
        try
        {
            var presenter = box.GetVisualDescendants().OfType<TextPresenter>().Single();
            var caret = presenter.TextLayout.HitTestTextPosition(0);
            var top = presenter.TranslatePoint(caret.TopLeft, window)!.Value.Y - box.TranslatePoint(default, window)!.Value.Y;
            var centre = top + (caret.Height / 2);
            var selection = presenter.TextLayout.HitTestTextRange(0, 2).Single();

            Assert.True(Math.Abs(centre - ((ink.Above + 32 - ink.Below) / 2d)) <= 1, $"caret centre {centre}, ink {ink}");
            Assert.Equal(caret.Top, selection.Top, 3);
            Assert.Equal(caret.Height, selection.Height, 3);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Accent_button_text_is_semibold_in_a_dialog_and_on_a_page() => session.Run(() =>
    {
        var accent = Resource<ControlTheme>("AccentButtonStyle");
        var page = new Button { Content = Caps, Theme = accent };
        var window = Show(page);
        var dialog = new FAContentDialog { Title = Caps, Content = Caps, PrimaryButtonText = "Save", CloseButtonText = "Close" };
        var closeOnly = new FAContentDialog { Title = Caps, CloseButtonText = "Close", DefaultButton = FAContentDialogButton.Close };
        try
        {
            Assert.Equal(FontWeight.SemiBold, Label(page).FontWeight);
            foreach (var shown in new[] { dialog, closeOnly })
            {
                var task = AppDialogs.ShowAsync(shown, window);
                try
                {
                    AvaloniaSession.WaitFor(() => shown.IsLoaded);
                    Dispatcher.UIThread.RunJobs();
                    var accented = shown.GetVisualDescendants().OfType<Button>()
                        .Where(button => button.IsVisible && (button.Theme == accent || button.Classes.Contains("accent")))
                        .ToList();
                    Assert.NotEmpty(accented);
                    Assert.All(accented, button => Assert.Equal(FontWeight.SemiBold, Label(button).FontWeight));
                }
                finally
                {
                    AvaloniaSession.CloseDialog(shown, task);
                }
            }
        }
        finally
        {
            window.Close();
        }
    });

    private static TextBlock Label(Button button) => button.GetVisualDescendants().OfType<TextBlock>().First(text => !string.IsNullOrEmpty(text.Text));

    private static Gaps Measure(string name, string text)
    {
        var sample = Cases[name](text);
        var window = Show(sample.Root);
        try
        {
            var box = sample.Box();
            var origin = box.TranslatePoint(default, window)!.Value;
            var top = (int)Math.Round(origin.Y);
            var bottom = (int)Math.Round(origin.Y + box.Bounds.Height);
            var part = TextPart(sample.Root, text);
            var left = (int)Math.Ceiling(part.TranslatePoint(default, window)!.Value.X);
            var right = (int)Math.Floor(part.TranslatePoint(new Point(part.Bounds.Width, 0), window)!.Value.X);

            using var frame = window.CaptureRenderedFrame()!;
            using var locked = frame.Lock();
            var pixels = new byte[locked.RowBytes * locked.Size.Height];
            Marshal.Copy(locked.Address, pixels, 0, pixels.Length);
            uint Pixel(int x, int y) => BitConverter.ToUInt32(pixels, (y * locked.RowBytes) + (x * 4));

            var fill = Enumerable.Range(top, bottom - top)
                .SelectMany(y => Enumerable.Range(left, right - left).Select(x => Pixel(x, y)))
                .GroupBy(colour => colour).MaxBy(group => group.Count())!.Key;
            var inkRows = Enumerable.Range(top, bottom - top)
                .Select(y => (y, count: Enumerable.Range(left, right - left).Count(x => Distance(Pixel(x, y), fill) > 48)))
                .Where(row => row.count > 0 && row.count < (right - left) * 0.9)
                .Select(row => row.y)
                .ToList();

            if (Environment.GetEnvironmentVariable("STEWARD_TEXT_CENTRING_PNG_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                frame.Save(System.IO.Path.Combine(directory, $"{name} {text}.png"), PngBitmapEncoderOptions.Default);
            }

            Assert.NotEmpty(inkRows);
            return new Gaps(top, bottom, inkRows.Min(), inkRows.Max() + 1);
        }
        finally
        {
            window.Close();
        }
    }

    private static Window Show(Control root)
    {
        root.HorizontalAlignment = HorizontalAlignment.Left;
        root.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window
        {
            Width = 480,
            Height = 120,
            Background = Resource<IBrush>("WindowGroundBrush"),
            Content = new Border { Padding = new Thickness(20), Child = root },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Control TextPart(Control root, string text) =>
        root.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(found => found.Text == text && found.IsEffectivelyVisible && found.Bounds.Height > 0)
        ?? (Control?)root.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault(found => found.Text == text)
        ?? (root is TextBlock or SelectableTextBlock ? root : ((Decorator)root).Child!);

    private static int Distance(uint a, uint b) =>
        Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF)) + Math.Abs((int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF)) + Math.Abs((int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF));

    private static void Report(string name, Gaps caps, Gaps descenders)
    {
        TestContext.Current.TestOutputHelper?.WriteLine($"{name}: {Caps} {caps}; {Descenders} {descenders}");
        if (Environment.GetEnvironmentVariable("STEWARD_TEXT_CENTRING_PNG_DIR") is { Length: > 0 } directory)
        {
            File.AppendAllText(System.IO.Path.Combine(directory, "gaps.txt"), string.Create(CultureInfo.InvariantCulture, $"{name}\t{Caps} {caps}\t{Descenders} {descenders}\n"), Encoding.UTF8);
        }
    }

    private static Sample Self(Control control) => new(control, () => control);

    private static Sample InBox(Control text)
    {
        var box = new Border { Height = 32, Width = 200, Padding = new Thickness(10, 0), Background = Resource<IBrush>("TextControlBackground"), Child = text };
        return new Sample(box, () => box);
    }

    private static T Part<T>(Control root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(found => found.Name == name);

    private static T Resource<T>(string key) =>
        Application.Current!.TryGetResource(key, ThemeVariant.Dark, out var value) && value is T typed ? typed : throw new KeyNotFoundException(key);

    private sealed record Sample(Control Root, Func<Control> Box);

    private sealed record Gaps(int BoxTop, int BoxBottom, int InkTop, int InkBottom)
    {
        public int Above => InkTop - BoxTop;

        public int Below => BoxBottom - InkBottom;

        public double CentreOffset => ((InkTop + InkBottom) / 2d) - ((BoxTop + BoxBottom) / 2d);

        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"box {BoxBottom - BoxTop}px, above {Above}, below {Below}, centre offset {CentreOffset:+0.0;-0.0;0}");
    }
}
