# UI Rules

paths:
  - lib/presentation/**

## Figma is the source of truth

- Never define UI only in code. All UI originates from Figma.
- UI change order: **Figma → Design review → Flutter implementation**. Never reverse.

## Design Tokens

Implement these in `shared/` or `presentation/`:

| Token group | Flutter class |
|-------------|--------------|
| Colors | `AppColors` |
| Spacing | `AppSpacing` |
| Border radius | `AppRadius` |
| Typography | `AppTextStyles` |

Spacing uses a 4px grid: `4, 8, 12, 16, 24, 32, 48`.
Never hardcode arbitrary spacing values or random color hex codes.

## Component Mapping

| Figma component | Flutter widget |
|-----------------|---------------|
| Primary Button | `AppPrimaryButton` |
| Text Field | `AppTextField` |
| Card | `AppCard` |

Reusable widgets live in `presentation/widgets/ui/`.

## UI States

Every screen must handle all four states:
- `Loading`
- `Empty`
- `Error`
- `Normal`

## Layout

- Map Figma Auto Layout → `Row`, `Column`, or `Flex`.
- Minimum tap target: **48dp**.

## Icons

- Use Material Icons or Material Symbols only.
- Common sizes: `16, 20, 24, 32`.

## Dark Mode

- Support both light and dark mode.
- Theme must be defined in both Figma and Flutter.

## Anti-Patterns (never do these)

- Hardcoded padding values not from `AppSpacing`
- Arbitrary color values not from `AppColors`
- Duplicated widget trees instead of extracting a shared widget
- Implementing UI without a corresponding Figma design
