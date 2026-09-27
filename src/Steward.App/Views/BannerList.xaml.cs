using Steward.App.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Steward.App.Views;

public sealed partial class BannerList : UserControl
{
    public static readonly DependencyProperty BannersProperty = DependencyProperty.Register(
        nameof(Banners), typeof(object), typeof(BannerList), new PropertyMetadata(null, OnBannersChanged));

    public BannerList() => InitializeComponent();

    public IReadOnlyList<BannerViewModel>? Banners
    {
        get => (IReadOnlyList<BannerViewModel>?)GetValue(BannersProperty);
        set => SetValue(BannersProperty, value);
    }

    private static void OnBannersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((BannerList)d).List.ItemsSource = e.NewValue;
}
