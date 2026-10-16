using Avalonia;
using Avalonia.Controls;

using Steward.App.ViewModels;

namespace Steward.Desktop.Views;

public sealed partial class BannerList : UserControl
{
    public static readonly StyledProperty<IEnumerable<BannerViewModel>?> BannersProperty =
        AvaloniaProperty.Register<BannerList, IEnumerable<BannerViewModel>?>(nameof(Banners));

    public BannerList() => InitializeComponent();

    public IEnumerable<BannerViewModel>? Banners
    {
        get => GetValue(BannersProperty);
        set => SetValue(BannersProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BannersProperty)
        {
            List.ItemsSource = Banners;
        }
    }
}
