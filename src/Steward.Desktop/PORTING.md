# Porting a WinUI view to Steward.Desktop

Each page and dialog in `src/Steward.App/Views` has a placeholder of the same name in `src/Steward.Desktop/Views`. The placeholder has the WinUI constructor shape, sets `DataContext` and `x:DataType`, and is registered (pages) or constructed by the shell (dialogs) already. Fill the `.axaml` body and port the code-behind. Do not change the constructor shape, the shell, `App.axaml` or the files under `Styles/` without asking the main loop.

## Shared controls and their owners

| Control | Owner | Use by others |
| --- | --- | --- |
| `BannerList` (`Banners` property) | Home page agent | `<views:BannerList Banners="{Binding Banners}" />` where the page's `DataContext` is `MainViewModel`, `{Binding Main.Banners}` on Sync |
| `ChangelogView` (new, `Views/ChangelogView.cs`) | ChangelogDialog agent | ReleaseChannelDialog |
| `MatchHighlight` (new, `Views/MatchHighlight.cs`) | GetAddonsDialog agent | none |
| `Spinner` (`Views/Spinner.cs`, done) | shell | `views:Spinner.IsSpinning="{Binding IsBusy}"` on any `Visual` |
| The error bar and the app-update InfoBar | Home page agent | they live in WinUI `HomePage.xaml`, not the window |

The Home page must show a loading state, never the "No World of Warcraft installs found" empty state, before the first install discovery has run. `MainViewModel.NoInstallsVisibility` is `Installs.Count == 0 && !IsBusy`, and on a signed-in start `InitializeAsync` awaits the banner fetch before `LoadAsync` discovers installs, so that property reads true for the first frames. No view model property says that discovery has finished yet; adding one is the main loop's decision, so port the empty state as WinUI has it until then.

## Bindings

- Bindings are compiled (`AvaloniaUseCompiledBindingsByDefault`). Every `DataTemplate` sets `x:DataType`. Write `{Binding Path}`; `OneWay` is the default, so `Mode=OneWay` is not needed.
- A WinUI `Visibility` binding is `IsVisible="{Binding SomethingVisibility}"`: the view model properties are already `bool`.
- `{x:Bind views:BindConvert.X(Path)}` becomes `{Binding Path, Converter={x:Static views:BindConvert.X}}`:

| WinUI `BindConvert` | Avalonia `BindConvert` | Input to output |
| --- | --- | --- |
| `Brush` | `Brush` | resource key string (null is transparent) to `IBrush` |
| `Style` | `Theme` | resource key string to `ControlTheme`, bound to `Theme` |
| `ColorBrush` | `ColorBrush` | ARGB `uint` to `SolidColorBrush` |
| `TopHairline` | `TopHairline` | `bool` to `Thickness` 0,1,0,0 |
| `Image` | `Image` | `object` to `IImage`, always use it for `Image.Source` |
| `Glyph` | `Glyph` | glyph name (`Glyphs.Info`) to the character |
| (PersonPicture) | `Initials` | display name to one or two initials |

- An image on a view model is an Avalonia `Bitmap` (assets, bytes, pixels) or a `PlatformImage` (a `DrawingImage`) that fills in when its download finishes (URIs), or is marked failed. Where the WinUI view handles `ImageFailed`, use `views:IconImage` and its `Failed` event. `ImageBrush` takes neither a `DrawingImage` nor an `object`, so a rounded image is a `Border` with `CornerRadius` and `ClipToBounds="True"` around an `Image`, as the shell's avatar does.

## Glyphs

Never write a code point in a view. A `FontIcon` is a `TextBlock` with the `Glyph` class (icon font, centred) and a key from `Styles/Glyphs.axaml`:

```xml
<TextBlock Classes="Glyph" Text="{StaticResource Glyph.Edit}" FontSize="13" />
<TextBlock Classes="Glyph" Text="{Binding NoticeGlyph, Converter={x:Static views:BindConvert.Glyph}}" />
```

A glyph that is not in `Glyphs.axaml` yet is added there under its Segoe Fluent Icons name with the code point of the matching `ic_fluent_<name>_20_regular` icon from `FluentSystemIcons-Resizable.json` (fluentui-system-icons commit `a563cf9`), recorded in AGENTS.md (Desktop resources), then used by key; `FontTests` fails when a key's code point is not in the font.

## Resources and styles

- Every brush, colour, thickness and radius key from WinUI `App.xaml` has the same key in `Styles/Resources.axaml`. Use `{DynamicResource}` for brushes and `{StaticResource}` for sizes.
- Text styles are classes: `Classes="Caption"` (12), `Classes="Body"` (14), `Classes="BodyStrong"` (14 SemiBold). `ContentControlThemeFontFamily` is the bundled Hanken Grotesk; set `FontFamily="{StaticResource DisplayFontFamily}"` on text of 20px or more, and `{StaticResource MonoFontFamily}` (the bundled Cascadia Mono) where WinUI used Cascadia Mono or Consolas. Never name a system font such as Segoe UI, Cascadia Mono or Consolas directly, and never write a literal icon code point.
- Button themes, set with `Theme="{StaticResource ...}"`: `DefaultButtonStyle`, `AccentButtonStyle`, `CriticalButtonStyle`, `CautionButtonStyle`, `SubtleButtonStyle` (clear rest, `SubtleFillColor*` hover and press) and `MenuRowButtonStyle` (a full-width subtle row for a flyout list).
- A per-button override of hover or press colours goes in `Button.Styles` against the template part: `Selector="Button:pointerover /template/ ContentPresenter#PART_ContentPresenter"`. A clear rest background is `SubtleFillColorTransparentBrush` (white at 0 alpha) against `SubtleFillColor*`, or `ControlFillColorTransparentBrush` (`#001A1F2B`) against an opaque fill, so the brush transition never passes through white.
- Card groups: `StackPanel Spacing="1"` with `GroupTopCornerRadius`, `GroupMiddleCornerRadius` and `GroupBottomCornerRadius` on the cards. No borders on cards, pills, alerts or flyouts. Text in a filled box: fixed `Height="32"` and `VerticalAlignment="Center"`. Mixed fonts on one line: one `TextBlock` of `Run`s.
- Keep every `AutomationProperties.Name` the WinUI view has, on the equivalent control.

## Dialogs

A dialog subclasses `FAContentDialog` (the placeholder already does, with `StyleKeyOverride`). Set `Title`, `PrimaryButtonText`, `SecondaryButtonText`, `CloseButtonText` and `DefaultButton` as on WinUI. Show it only through `AppDialogs.ShowAsync(dialog, TopLevel.GetTopLevel(this)!)`, which applies the Steward look (the `Steward` class: 8px corners, `ContentDialogBackground`, the accent primary button, the dialog hover brushes), closes open flyouts first and queues a dialog behind one already open. A `PrimaryButtonClick` that does work takes `args.GetDeferral()`, as `MainWindow.ShowForeverCopyAsync` does. The scrim, the gap above the buttons and the dialog text box and combo box look come from `Styles/Dialogs.axaml`, and `AppDialogs` focuses the first focusable control in the content, so a dialog sets none of these itself. Two WinUI header metrics need porting by hand: a `ComboBox` `Header` has `LineHeight` 20, so its `FieldHeader` takes `Margin="0,0,0,10"`, and `RadioButtons` keeps an empty header with an 8px bottom margin and spaces its rows 8px, so its port is an `ItemsControl` with `Margin="0,8,0,0"` over a one-column `UniformGrid` with `RowSpacing="8"`.

## Flyouts and menus

- A command list is an Avalonia `MenuFlyout` of `MenuItem`s. Its presenter gets the tinted look globally.
- A custom dropdown is a `Flyout` with `FlyoutPresenterClasses="Tinted"` (add `Flush` for no padding) and an explicit `Placement`. Close it from a row's `Click` with `button.Flyout?.Hide()`.
- A flyout or menu anchored to a button is that button's `Button.Flyout`, so a second click closes it. Never open one from a `Click` handler or through `FlyoutBase.AttachedFlyout`.
- A style in a button's own `Button.Styles` also reaches its flyout's content, so its selector names the button (`Button#InstallPicker:pointerover`).
- `MenuFlyoutPresenter` and `FlyoutPresenter.Tinted` get WinUI's tinted acrylic from `Styles/Acrylic`, chosen by capability: real blur under the tint and luminosity layers when the popup window reports `AcrylicBlur` or `Blur`, a mock acrylic (the same layers over `FlyoutAcrylicFallbackColor` with WinUI's noise) for an overlay popup or a platform without blur, and the solid fallback colour while the window is inactive. Set no `Background` on a presenter; AGENTS.md (Avalonia desktop app) has the recipe and measurements.
- Tooltips open above the pointer and ComboBox lists open over the box globally; set no `ToolTip.Placement` in a view.
- A hover submenu follows `MainWindow.AttachSubmenu` (`Native.SubmenuDelay()`, open on enter, close on leave or on another row).

## WinUI constructs

| WinUI | Avalonia |
| --- | --- |
| `InfoBar`, `InfoBadge` | `ui:FAInfoBar`, `ui:FAInfoBadge` |
| `ProgressRing IsActive` | `views:ProgressRing IsActive` (never `ui:FAProgressRing`, whose stroke is half WinUI's) |
| `SettingsCard`, `SettingsExpander` | `ui:FASettingsExpander` and `ui:FASettingsExpanderItem`, or a `Border` row where the metrics differ |
| `Segmented` | a `ListBox` with `Theme="{StaticResource SegmentedStyle}"` (`Styles/Controls.axaml`): equal-width items in a `UniformGrid` on `SegmentedTrackBrush` (`#19000000`, the toolkit's track as measured in the baseline) at 4px corners, items 29px high at 13px with 11px side padding, hover and press on an inset `PART_Hover` with the subtle fills, the selected item on `ControlFillColorDefaultBrush` with a 16x3 accent pill at the bottom, all as the CommunityToolkit `SegmentedItem` template; an item selects on release over it, not on press (`Styles/SegmentedSelection`, switched on by the theme) |
| `TextBox Header`, `ComboBox Header` | a `TextBlock` with `Classes="FieldHeader"` (13px, `TextControlHeaderForeground`, 8px below) above the control, `Classes="FieldHeader ComboBox"` for a `ComboBox`; set `FontSize` when the WinUI control sets its own, since WinUI's header inherits it |
| `ItemsRepeater` | `ItemsControl` for short lists; `ListBox` with a `VirtualizingStackPanel` (item container theme stripped of selection visuals) for the addon table |
| `DataTemplateSelector` | a class implementing `IDataTemplate` (`Match` on the item type), or one `DataTemplate` per `x:DataType` in `ItemsControl.DataTemplates` |
| `VisualStateManager`, `Storyboard` | pseudo-class and class styles (`:pointerover`, `:pressed`, `:disabled`, `Classes.Busy="{Binding IsBusy}"`), `Transitions` for property fades, `Animation` for keyframes |
| `PersonPicture` | `Border` with `CornerRadius` half its size, `ClipToBounds`, the `Initials` text and an `Image` |
| `HyperlinkButton NavigateUri` | `HyperlinkButton NavigateUri` (not underlined, by a global style, as on WinUI) |
| `x:Load`, `x:Phase` | `IsVisible` |
| `TextBlock.TextLineBounds` | none; every `TextBlock` and its subclasses (`AccessText`, `SelectableTextBlock`) and every `TextBox` already take the calibrated line box from the global styles (AGENTS.md, Avalonia desktop app), so set no `LineHeight`, `Padding` or `RenderTransform` on text to move it; `TextCentring.Verify` in the page and dialog render tests checks the ink centring; a line stacked in a vertical `StackPanel` takes WinUI's baseline by itself, and a line centred in a row rather than a box of its own takes `styles:SegoeMetrics.IsFree="True"` (the install picker's label) |
| a `Padding` or `Margin` 1px or 2px heavier at the bottom to centre Segoe's ink | symmetric padding in a control style, since boxed text already centres its capitals on the line box; a view keeps WinUI's own padding (the install picker's `11,0,10,2`) |
| `ScrollViewer` mouse-wheel animation | the global `SmoothScroll` style; nothing to set |

## Checks

Build with `dotnet build Steward.slnx -c Debug` (0 warnings), run `dotnet test --project tests/Steward.Desktop.Tests/Steward.Desktop.Tests.csproj`, and look at your page in a headless frame: set `STEWARD_SHELL_PNG` to a scratch path and run the `Shell_renders_a_frame_at_1280_by_820` test after navigating to your page in a test of your own. Do not open a real window on the desktop.
