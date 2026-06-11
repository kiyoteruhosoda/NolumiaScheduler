namespace NolumiaScheduler.Domain.ValueObjects;

/// <summary>
/// Visual theme preference. <see cref="System"/> follows the OS setting;
/// the others force the corresponding theme regardless of the OS.
/// </summary>
public enum ThemeMode
{
    System,
    Light,
    Dark
}
