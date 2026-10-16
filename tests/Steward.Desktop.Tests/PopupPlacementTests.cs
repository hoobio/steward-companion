using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Steward.Desktop.Tests;

public sealed class PopupPlacementTests(AvaloniaSession session)
{
    private static readonly string[] Sources = ["All sources", "CurseForge"];

    [Theory]
    [InlineData(400, true)]
    [InlineData(4, false)]
    public void A_tooltip_opens_20px_above_the_pointer_or_below_it_without_covering_it(double top, bool above) => session.Run(() =>
    {
        var target = new Button { Content = "Target", Margin = new Thickness(300, top, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        ToolTip.SetTip(target, "Tip");
        var window = new Window { Width = 800, Height = 800, Content = target };
        window.Show();
        Pump();
        var pointer = new Point(330, top + 10);
        window.MouseMove(pointer);
        ToolTip.SetIsOpen(target, true);
        Pump();

        var tip = window.OpenedPopups.Select(popup => popup.Child).OfType<ToolTip>().Single();
        var bounds = new Rect(tip.TranslatePoint(default, window)!.Value, tip.Bounds.Size);
        Assert.True(Math.Abs(pointer.X - bounds.Center.X) <= 0.5, $"tooltip {bounds}, pointer {pointer}");
        Assert.True(Math.Abs((above ? pointer.Y - 20 : pointer.Y + 20) - (above ? bounds.Bottom : bounds.Top)) <= 0.5, $"tooltip {bounds}, pointer {pointer}");

        window.Close();
    });

    [Fact]
    public void A_click_on_a_button_under_an_open_overlay_tooltip_reaches_the_button() => session.Run(() =>
    {
        var target = new Button { Content = "Target" };
        ToolTip.SetTip(target, "A tooltip wide enough to cover the button under it");
        ToolTip.SetShouldUseOverlayLayer(target, true);
        var under = new Button { Content = "Under", Width = 80, Height = 30 };
        var canvas = new Canvas { Children = { target, under } };
        Canvas.SetLeft(target, 300);
        Canvas.SetTop(target, 400);
        Canvas.SetLeft(under, 290);
        Canvas.SetTop(under, 365);
        var clicks = 0;
        under.Click += (_, _) => clicks++;
        var window = new Window { Width = 800, Height = 800, Content = canvas };
        window.Show();
        Pump();
        window.MouseMove(new Point(330, 410));
        ToolTip.SetIsOpen(target, true);
        Pump();

        var tip = window.OpenedPopups.Select(popup => popup.Child).OfType<ToolTip>().Single();
        var point = new Point(330, 380);
        Assert.True(new Rect(tip.TranslatePoint(default, window)!.Value, tip.Bounds.Size).Contains(point));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Pump();

        Assert.Equal(1, clicks);
        Assert.False(ToolTip.GetIsOpen(target));
        window.Close();
    });

    [Fact]
    public void A_tooltip_popup_is_not_hit_testable_so_a_native_popup_window_passes_input_through() => session.Run(() =>
    {
        var target = new Button { Content = "Target" };
        ToolTip.SetTip(target, "Tip");
        var window = new Window { Width = 800, Height = 800, Content = target };
        window.Show();
        Pump();
        ToolTip.SetIsOpen(target, true);
        Pump();

        var host = window.OpenedPopups.Single(popup => popup.Child is ToolTip);
        Assert.False(((Control)host).IsHitTestVisible);
        Assert.False(host.Child!.FindLogicalAncestorOfType<Popup>()!.IsHitTestVisible);
        window.Close();
    });

    [Fact]
    public void A_combo_box_list_opens_with_the_selected_item_over_the_box() => session.Run(() =>
    {
        var combo = new ComboBox { Width = 150, ItemsSource = Sources, SelectedIndex = 1, Margin = new Thickness(100, 300, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 800, Height = 800, Content = combo };
        window.Show();
        Pump();
        combo.IsDropDownOpen = true;
        Pump();

        var item = combo.GetVisualDescendants().OfType<Popup>().Single().Child!.GetVisualDescendants().OfType<ComboBoxItem>().ElementAt(1);
        var itemCentre = item.TranslatePoint(new Point(0, item.Bounds.Height / 2), window)!.Value.Y;
        var boxCentre = combo.TranslatePoint(new Point(0, combo.Bounds.Height / 2), window)!.Value.Y;
        Assert.Equal(boxCentre, itemCentre, 1);
        combo.IsDropDownOpen = false;
        window.Close();
    });

    private static void Pump()
    {
        for (var i = 0; i < 6; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
}
