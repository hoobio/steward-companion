using Steward.App.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Steward.App.Views;

public sealed partial class AddonTableTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Addon { get; set; }

    public DataTemplate? Local { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item is LocalAddonRowViewModel ? Local : Addon;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
