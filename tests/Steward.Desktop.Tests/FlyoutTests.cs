using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Steward.App.Services;
using Steward.Desktop.Views;

namespace Steward.Desktop.Tests;

public sealed class FlyoutTests(AvaloniaSession session)
{
    private static readonly Color Ground = Color.Parse("#FF1F2430");
    private static readonly string[] Pages = ["sync", "guides", "settings", "addons"];
    private static readonly string[] Items = ["One", "Two"];

    [Theory]
    [InlineData("InstallPicker")]
    [InlineData("AccountButton")]
    public void A_second_click_on_a_flyout_button_closes_its_flyout(string name) => session.Run(() =>
    {
        using var host = session.NewHost();
        var window = ShowShell(host);
        var button = window.FindControl<Button>(name)!;
        button.IsVisible = true;
        Pump();
        var flyout = button.Flyout!;
        var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;

        Click(window, centre);
        Assert.True(flyout.IsOpen);

        Click(window, centre);
        Assert.False(flyout.IsOpen);

        Click(window, centre);
        Assert.True(flyout.IsOpen);
        flyout.Hide();
        window.Hide();
    });

    [Fact]
    public void No_hover_or_press_fade_passes_through_a_lighter_colour() => session.Run(() =>
    {
        using var host = session.NewHost();
        var window = ShowShell(host);
        var flashes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var tag in Pages)
        {
            window.ViewModel.NavigateToPageTag!(tag);
            Pump();
            Probe(window, flashes);
        }

        var picker = window.FindControl<Button>("InstallPicker")!;
        picker.IsVisible = true;
        Pump();
        foreach (var button in new[] { picker, window.FindControl<Button>("AccountButton")! })
        {
            button.Flyout!.ShowAt(button);
            Pump();
            ProbePopups(window, flashes);
            button.Flyout.Hide();
        }

        var gallery = new Window { Width = 800, Height = 900, Content = Gallery() };
        gallery.Show();
        Pump();
        Probe(gallery, flashes);
        var menuHost = gallery.GetVisualDescendants().OfType<Button>().First(button => button.Flyout is MenuFlyout);
        menuHost.Flyout!.ShowAt(menuHost);
        Pump();
        ProbePopups(gallery, flashes);
        menuHost.Flyout.Hide();
        var combo = gallery.GetVisualDescendants().OfType<ComboBox>().First();
        combo.IsDropDownOpen = true;
        Pump();
        ProbePopups(gallery, flashes);
        combo.IsDropDownOpen = false;
        gallery.Close();
        window.Hide();

        Assert.True(flashes.Count == 0, string.Join(Environment.NewLine, flashes));
    });

    private static MainWindow ShowShell(IHost host)
    {
        var window = host.Services.GetRequiredService<MainWindow>();
        LiveUpdatesPreview.Apply(window.ViewModel);
        window.Width = 1280;
        window.Height = 820;
        window.Show();
        Pump();
        return window;
    }

    private static StackPanel Gallery()
    {
        Button Themed(string key) => new() { Content = key, Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource(key)! };

        var list = new ListBox { ItemsSource = Items, SelectedIndex = 0 };
        var segmented = new ListBox { ItemsSource = new[] { "All", "Updates" }, SelectedIndex = 0, Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("SegmentedStyle")! };
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuItem { Header = "Item" });
        menu.Items.Add(new MenuItem { Header = "Parent", Items = { new MenuItem { Header = "Child" } } });
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new Button { Content = "Default" },
                Themed("DefaultButtonStyle"),
                Themed("AccentButtonStyle"),
                Themed("CriticalButtonStyle"),
                Themed("CautionButtonStyle"),
                Themed("SubtleButtonStyle"),
                Themed("MenuRowButtonStyle"),
                new Button { Content = "Menu", Flyout = menu },
                new RepeatButton { Content = "Repeat" },
                new ToggleButton { Content = "Toggle" },
                new ToggleButton { Content = "Toggled", IsChecked = true },
                new HyperlinkButton { Content = "Link" },
                new RadioButton { Content = "Radio" },
                new RadioButton { Content = "Radio on", IsChecked = true, GroupName = "on" },
                new CheckBox { Content = "Check" },
                new CheckBox { Content = "Checked", IsChecked = true },
                new ToggleSwitch(),
                new ToggleSwitch { IsChecked = true },
                new ComboBox { ItemsSource = Items, SelectedIndex = 0 },
                new TextBox { Text = "Text" },
                list,
                segmented,
                new SplitButton { Content = "Split" },
                new DropDownButton { Content = "Drop" },
                new FAInfoBar { IsOpen = true, IsClosable = true, Message = "Info" },
                new SettingsCard { Header = "Card", IsClickEnabled = true },
            },
        };
    }

    private static void ProbePopups(TopLevel top, SortedSet<string> flashes)
    {
        foreach (var popup in top.OpenedPopups)
        {
            if (popup.Child is { } child)
            {
                Probe(child, flashes);
            }
        }
    }

    private static void Probe(Visual root, SortedSet<string> flashes)
    {
        foreach (var control in root.GetSelfAndVisualDescendants().OfType<TemplatedControl>().Where(control => control.IsEffectivelyVisible).ToList())
        {
            var targets = Subtree(control).SelectMany(element => (element.Transitions ?? []).OfType<BrushTransition>().Select(transition => (element, transition.Property!))).ToList();
            if (targets.Count == 0)
            {
                continue;
            }

            var pseudo = (IPseudoClasses)control.Classes;
            var rest = Read(targets);
            pseudo.Add(":pointerover");
            var over = Read(targets);
            pseudo.Add(":pressed");
            var pressed = Read(targets);
            pseudo.Remove(":pressed");
            pseudo.Remove(":pointerover");

            for (var i = 0; i < targets.Count; i++)
            {
                var (element, property) = targets[i];
                var where = $"{control.GetType().Name}[{string.Join(' ', control.Classes)}] {element.GetType().Name}#{(element as StyledElement)?.Name}.{property.Name}";
                Check(flashes, where, "rest", rest[i], "pointerover", over[i]);
                Check(flashes, where, "pointerover", over[i], "pressed", pressed[i]);
                Check(flashes, where, "rest", rest[i], "pressed", pressed[i]);
            }
        }
    }

    private static IEnumerable<Animatable> Subtree(TemplatedControl control)
    {
        var pending = new Stack<Visual>([control]);
        while (pending.TryPop(out var visual))
        {
            if (visual is Animatable animatable)
            {
                yield return animatable;
            }

            foreach (var child in visual.GetVisualChildren())
            {
                if (child is not TemplatedControl)
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static Color?[] Read(List<(Animatable Element, AvaloniaProperty Property)> targets) =>
        targets.Select(target => (target.Element.GetBaseValue(target.Property) as ISolidColorBrush)?.Color).ToArray();

    private static void Check(SortedSet<string> flashes, string where, string fromState, Color? from, string toState, Color? to)
    {
        if (from is not { } a || to is not { } b || a == b)
        {
            return;
        }

        var ends = Math.Max(Luma(a), Luma(b));
        for (var step = 1; step < 20; step++)
        {
            if (Luma(Interpolate(step / 20d, a, b)) > ends + 2)
            {
                flashes.Add($"{where}: {fromState} {a} to {toState} {b}");
                return;
            }
        }
    }

    private static double Luma(Color color)
    {
        var alpha = color.A / 255d;
        double Over(byte channel, byte ground) => channel * alpha + ground * (1 - alpha);
        return 0.2126 * Over(color.R, Ground.R) + 0.7152 * Over(color.G, Ground.G) + 0.0722 * Over(color.B, Ground.B);
    }

    // Mirrors Avalonia's ColorAnimator: channels lerp in linear light with straight, not premultiplied, alpha.
    private static Color Interpolate(double progress, Color from, Color to)
    {
        static double Linear(byte value) => value / 255d <= 0.04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + 0.055) / 1.055, 2.4);
        static byte Srgb(double linear) => (byte)Math.Round((linear <= 0.0031308 ? linear * 12.92 : Math.Pow(linear, 5d / 12d) * 1.055 - 0.055) * 255);
        byte Channel(byte a, byte b) => Srgb(Linear(a) + progress * (Linear(b) - Linear(a)));
        return new Color((byte)Math.Round(from.A + progress * (to.A - from.A)), Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
    }

    private static void Click(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Pump();
    }

    private static void Pump()
    {
        for (var pass = 0; pass < 5; pass++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }
}
