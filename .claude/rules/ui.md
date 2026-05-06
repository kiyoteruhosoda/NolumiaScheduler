# UI Rules

Applies to all tasks touching `NolumiaScheduler/NolumiaScheduler/Presentation/`.

## Design Tokens

| Token group | Location |
|-------------|---------|
| Colors | `Resources/Styles/Colors.xaml` — use `StaticResource` / `AppThemeBinding` |
| Strings | `Resources/Strings/AppResources.resx` (EN default) + `AppResources.ja.resx` (JA) |

Never hardcode arbitrary color hex codes in XAML or C#. Use the named color keys from `Colors.xaml`.

## Language / Localization

- **Default language is English.** The neutral `.resx` file (`AppResources.resx`) holds English strings.
- Japanese strings live in `AppResources.ja.resx`.
- Every user-visible string must have an entry in both `.resx` files — no hardcoded literals in XAML or ViewModels.
- In XAML use `{x:Static res:AppResources.KeyName}` with `xmlns:res="clr-namespace:NolumiaScheduler.Resources.Strings"`.
- In C# use `AppResources.KeyName` (resolved via `ResourceManager` + current `Culture`).
- Language switching is done by setting `AppResources.Culture` to the desired `CultureInfo` and re-navigating. Locale-sensitive date/time formatting must pass `AppResources.FormatCulture` as the format provider.
- When adding a new string: add to `AppResources.resx` first (English), then `AppResources.ja.resx` (Japanese). Never add to one file only.

## Dark Mode

- Support both light and dark mode via `AppThemeBinding`.
- All theme-switching colors must be defined as named keys in `Colors.xaml` (light and dark variants).

## Layout

- Minimum tap target: **48dp**.
- Use `GridItemsLayout Span="7"` for the weekly calendar grid.

## Anti-Patterns (never do these)

- Hardcoded color hex values in XAML or C# (use named keys from `Colors.xaml`)
- Hardcoded user-visible strings in XAML or C# (use `AppResources`)
- Adding a string to only one `.resx` file
- Setting `AppResources.Culture` to anything other than `null` (follow thread culture) or an explicit `CultureInfo` passed by a language-switch action
