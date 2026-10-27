using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

using FluentAvalonia.UI.Controls;

using Steward.App.ViewModels;
using Steward.Core;
using Steward.Desktop.Services;

namespace Steward.Desktop.Views;

public sealed partial class AddonSettingsPage : UserControl
{
    private const string CarryOverNote = "Steward writes this character's settings now, so HoobiScripts does not copy settings from a character of the same name on another realm at its first login.";

    public AddonSettingsPage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        viewModel.AddonSettings.PagesChanged += (_, _) => BuildPages();
        Loaded += (_, _) => _ = viewModel.AddonSettings.ActivateAsync();
        Unloaded += (_, _) => viewModel.AddonSettings.Deactivate();
    }

    public MainViewModel ViewModel { get; }

    private AddonSettingsViewModel Settings => ViewModel.AddonSettings;

    private void BuildPages()
    {
        PagesHost.Children.Clear();
        foreach (var page in Settings.Pages)
        {
            PagesHost.Children.Add(new TextBlock { Text = page.Title, Classes = { "BodyStrong" }, Margin = new Thickness(0, 20, 0, 4) });
            if (page.Message is { } message)
            {
                var box = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16, 14),
                    Child = Secondary(new TextBlock { Text = message, Classes = { "Body" }, TextWrapping = TextWrapping.Wrap }),
                };
                box.Bind(Border.BackgroundProperty, this.GetResourceObservable("LayerFillColorDefaultBrush"));
                PagesHost.Children.Add(box);
            }

            foreach (var group in page.Groups)
            {
                if (group.Title is { Length: > 0 } title)
                {
                    PagesHost.Children.Add(Secondary(new TextBlock { Text = title, Classes = { "Caption" }, Margin = new Thickness(0, 8, 0, 4) }));
                }

                var panel = new StackPanel { Classes = { "SettingsGroup" } };
                foreach (var row in group.Rows)
                {
                    panel.Children.Add(Card(row));
                }

                PagesHost.Children.Add(panel);
            }
        }
    }

    private TextBlock Secondary(TextBlock block)
    {
        block.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("TextFillColorSecondaryBrush"));
        return block;
    }

    private SettingsCard Card(SettingRowViewModel row)
    {
        var card = new SettingsCard { Content = Editor(row) };
        if (row is NoteSettingViewModel note)
        {
            card.Description = note.Text;
            card.HeaderGlyph = (string?)this.FindResource("Glyph.Info");
        }
        else
        {
            card.Header = row.Label;
            card.Bind(SettingsCard.DescriptionProperty, Bind(row, nameof(SettingRowViewModel.Description)));
            card.Bind(IsEnabledProperty, Bind(row, nameof(SettingRowViewModel.IsEnabled)));
        }

        card.Bind(IsVisibleProperty, Bind(row, nameof(SettingRowViewModel.IsShown)));
        if (row.IsIndented)
        {
            card.Padding = new Thickness(48, 15.01, 16, 15.01);
        }

        return card;
    }

    private static Binding Bind(object source, string path, BindingMode mode = BindingMode.OneWay) => new(path) { Mode = mode, Source = source };

    private static Control? Editor(SettingRowViewModel row)
    {
        switch (row)
        {
            case ToggleSettingViewModel toggle:
                var on = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
                on.Bind(TextBlock.TextProperty, new Binding(nameof(ToggleSettingViewModel.IsOn)) { Source = toggle, Converter = SettingsPage.OnOff });
                var toggleSwitch = new ToggleSwitch { OnContent = null, OffContent = null };
                toggleSwitch.Bind(ToggleSwitch.IsCheckedProperty, Bind(toggle, nameof(ToggleSettingViewModel.IsOn), BindingMode.TwoWay));
                Avalonia.Automation.AutomationProperties.SetName(toggleSwitch, row.Label);
                return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center, Children = { on, toggleSwitch } };
            case ChoiceSettingViewModel choice:
                var combo = new ComboBox { ItemsSource = choice.Options, MinWidth = 200 };
                combo.Bind(SelectingItemsControl.SelectedIndexProperty, Bind(choice, nameof(ChoiceSettingViewModel.SelectedIndex), BindingMode.TwoWay));
                Avalonia.Automation.AutomationProperties.SetName(combo, row.Label);
                return combo;
            case BitmaskSettingViewModel bitmask:
                var bits = new WrapPanel { ItemSpacing = 16, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Right };
                foreach (var bit in bitmask.Bits)
                {
                    var box = new CheckBox { Content = bit.Label };
                    box.Bind(ToggleButton.IsCheckedProperty, Bind(bit, nameof(BitOptionViewModel.IsChecked), BindingMode.TwoWay));
                    bits.Children.Add(box);
                }

                return bits;
            case TextSettingViewModel text:
                var input = new TextBox { Width = 200, PlaceholderText = text.Placeholder, MaxLength = text.MaxLength };
                input.Bind(TextBox.TextProperty, Bind(text, nameof(TextSettingViewModel.Text), BindingMode.TwoWay));
                Avalonia.Automation.AutomationProperties.SetName(input, row.Label);
                return input;
            case MailTargetSettingViewModel mail:
                var mode = new ComboBox { ItemsSource = MailTargetSettingViewModel.Modes, MinWidth = 120 };
                mode.Bind(SelectingItemsControl.SelectedIndexProperty, Bind(mail, nameof(MailTargetSettingViewModel.ModeIndex), BindingMode.TwoWay));
                Avalonia.Automation.AutomationProperties.SetName(mode, row.Label);
                var name = new TextBox { Width = 180, PlaceholderText = "Character name", MaxLength = 30 };
                name.Bind(TextBox.TextProperty, Bind(mail, nameof(MailTargetSettingViewModel.Name), BindingMode.TwoWay));
                name.Bind(IsEnabledProperty, Bind(mail, nameof(MailTargetSettingViewModel.IsNameEnabled)));
                return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { mode, name } };
            case ReadOnlySettingViewModel fixedValue:
                return new TextBlock { Text = fixedValue.Shown, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            default:
                return null;
        }
    }

    private async void OnNewCharacterClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } owner)
        {
            return;
        }

        var first = new TextBox { PlaceholderText = "First name", MaxLength = 12 };
        var last = new TextBox { PlaceholderText = "Last name", MaxLength = 12 };
        var classes = new ComboBox
        {
            ItemsSource = AddonSettings.ClassTokens.Select(AddonSettings.ClassLabel).ToList(),
            PlaceholderText = "Choose a class",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var realm = new ComboBox { ItemsSource = Settings.KnownRealms.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        error.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("SystemFillColorCriticalBrush"));
        var note = Secondary(new TextBlock { Text = CarryOverNote, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        var dialog = new FAContentDialog
        {
            Title = new TextBlock { Text = "New character", TextWrapping = TextWrapping.Wrap },
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "First name", Classes = { "FieldHeader" }, Margin = new Thickness(0) },
                    first,
                    new TextBlock { Text = "Last name", Classes = { "FieldHeader" }, Margin = new Thickness(0, 4, 0, 0) },
                    last,
                    new TextBlock { Text = "Class", Classes = { "FieldHeader", "ComboBox" }, Margin = new Thickness(0, 4, 0, 0) },
                    classes,
                    new TextBlock { Text = "Realm", Classes = { "FieldHeader", "ComboBox" }, Margin = new Thickness(0, 4, 0, 0) },
                    realm,
                    note,
                    error,
                },
            },
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var classToken = classes.SelectedIndex >= 0 ? AddonSettings.ClassTokens[classes.SelectedIndex] : null;
            if (Settings.CreateCharacter(first.Text ?? "", last.Text ?? "", classToken, realm.SelectedItem as string ?? "") is { } failure)
            {
                error.Text = failure;
                error.IsVisible = true;
                args.Cancel = true;
            }
        };
        await AppDialogs.ShowAsync(dialog, owner);
    }
}
