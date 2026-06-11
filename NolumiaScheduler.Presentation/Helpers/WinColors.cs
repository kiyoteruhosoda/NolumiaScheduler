using NolumiaScheduler.Domain.ValueObjects;
using Windows.UI;

namespace NolumiaScheduler.Presentation.Helpers;

public static class WinColors
{
    public static Color FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte a = 255, r, g, b;
        if (hex.Length == 8)
        {
            a = Convert.ToByte(hex[0..2], 16);
            r = Convert.ToByte(hex[2..4], 16);
            g = Convert.ToByte(hex[4..6], 16);
            b = Convert.ToByte(hex[6..8], 16);
        }
        else
        {
            r = Convert.ToByte(hex[0..2], 16);
            g = Convert.ToByte(hex[2..4], 16);
            b = Convert.ToByte(hex[4..6], 16);
        }
        return Color.FromArgb(a, r, g, b);
    }

    public static Color GCalBlue           => FromHex("#1a73e8");
    public static Color GCalBlueDark       => FromHex("#8ab4f8");
    public static Color GCalBlueLight      => FromHex("#e8f0fe");
    public static Color GCalBlueLightDark  => FromHex("#1e3a5f");
    public static Color GCalRed            => FromHex("#d93025");
    public static Color GCalRedDark        => FromHex("#f28b82");
    public static Color GCalGreen          => FromHex("#1e8e3e");
    public static Color GCalGreenDark      => FromHex("#81c995");
    public static Color GCalTextPrimary    => FromHex("#202124");
    public static Color GCalTextSecondary  => FromHex("#70757a");
    public static Color GCalTextSecondaryDark => FromHex("#9aa0a6");
    public static Color GCalSurface        => FromHex("#ffffff");
    public static Color GCalSurfaceDark    => FromHex("#1e1e1e");
    public static Color GCalSurfaceVariant => FromHex("#f8f9fa");
    public static Color GCalSurfaceVariantDark => FromHex("#2d2d2d");
    public static Color GCalBorder         => FromHex("#e0e0e0");
    public static Color GCalBorderDark     => FromHex("#3c3c3c");
    public static Color GCalSelectedCircle => FromHex("#70757a");
    public static Color GCalEventMoved     => FromHex("#9c27b0");
    public static Color GCalEventMovedDark => FromHex("#ce93d8");
    public static Color GCalOutOfMonthText     => FromHex("#bdbdbd");
    public static Color GCalOutOfMonthTextDark => FromHex("#555555");
    public static Color GCalHolidayBg     => FromHex("#fff0f0");
    public static Color GCalHolidayBgDark => FromHex("#3a1a1a");
    public static Color GCalSundayBg      => FromHex("#fff8f8");
    public static Color GCalSundayBgDark  => FromHex("#2d1a1a");
    public static Color GCalSaturdayBg    => FromHex("#f0f4ff");
    public static Color GCalSaturdayBgDark => FromHex("#1a1a2d");
    // Built from the struct directly: Microsoft.UI.Colors statics require WinRT activation,
    // which is unavailable in unit test processes.
    public static Color White              => Color.FromArgb(255, 255, 255, 255);
    public static Color Transparent        => Color.FromArgb(0, 0, 0, 0);
    public static Color Gray               => FromHex("#808080");

    // Week-grid canvas tokens (mirrored in Colors.xaml ThemeDictionaries)
    public static Color GCalGridLine          => FromHex("#D0D7DE");
    public static Color GCalGridLineDark      => FromHex("#454545");
    public static Color GCalGridHalfLine      => FromHex("#5AD0D7DE");  // alpha 90
    public static Color GCalGridHalfLineDark  => FromHex("#50454545");  // alpha 80
    public static Color GCalCurrentTimeLine   => FromHex("#EA4335");    // same both themes
    public static Color GCalAllDayTint        => FromHex("#301A73E8");  // alpha 48, blue
    public static Color GCalDragGhost         => FromHex("#5A1A73E8");  // alpha 90, blue

    // Event color palette (Google Calendar hues). Saturated enough to carry white
    // chip text in both light and dark themes, so one value serves both.
    public static Color EventTomato     => FromHex("#D50000");
    public static Color EventTangerine  => FromHex("#F4511E");
    public static Color EventBanana     => FromHex("#F6BF26");
    public static Color EventBasil      => FromHex("#0B8043");
    public static Color EventSage       => FromHex("#33B679");
    public static Color EventPeacock    => FromHex("#039BE5");
    public static Color EventBlueberry  => FromHex("#3F51B5");
    public static Color EventLavender   => FromHex("#7986CB");
    public static Color EventGrape      => FromHex("#8E24AA");
    public static Color EventGraphite   => FromHex("#616161");

    /// <summary>Resolves an event's color key; Default falls back to the standard event blue.</summary>
    public static Color ForEventColor(EventColorKey key) => key switch
    {
        EventColorKey.Tomato    => EventTomato,
        EventColorKey.Tangerine => EventTangerine,
        EventColorKey.Banana    => EventBanana,
        EventColorKey.Basil     => EventBasil,
        EventColorKey.Sage      => EventSage,
        EventColorKey.Peacock   => EventPeacock,
        EventColorKey.Blueberry => EventBlueberry,
        EventColorKey.Lavender  => EventLavender,
        EventColorKey.Grape     => EventGrape,
        EventColorKey.Graphite  => EventGraphite,
        _ => GCalBlue
    };

    public static Color Named(string key) => key switch
    {
        "GCalBlue"               => GCalBlue,
        "GCalBlueDark"           => GCalBlueDark,
        "GCalBlueLight"          => GCalBlueLight,
        "GCalBlueLightDark"      => GCalBlueLightDark,
        "GCalRed"                => GCalRed,
        "GCalRedDark"            => GCalRedDark,
        "GCalGreen"              => GCalGreen,
        "GCalGreenDark"          => GCalGreenDark,
        "GCalTextPrimary"        => GCalTextPrimary,
        "GCalTextSecondary"      => GCalTextSecondary,
        "GCalTextSecondaryDark"  => GCalTextSecondaryDark,
        "GCalSurface"            => GCalSurface,
        "GCalSurfaceDark"        => GCalSurfaceDark,
        "GCalSurfaceVariant"     => GCalSurfaceVariant,
        "GCalSurfaceVariantDark" => GCalSurfaceVariantDark,
        "GCalBorder"             => GCalBorder,
        "GCalBorderDark"         => GCalBorderDark,
        "GCalSelectedCircle"     => GCalSelectedCircle,
        "GCalEventMoved"         => GCalEventMoved,
        "GCalEventMovedDark"     => GCalEventMovedDark,
        "GCalOutOfMonthText"     => GCalOutOfMonthText,
        "GCalOutOfMonthTextDark" => GCalOutOfMonthTextDark,
        "GCalHolidayBg"          => GCalHolidayBg,
        "GCalHolidayBgDark"      => GCalHolidayBgDark,
        "GCalSundayBg"           => GCalSundayBg,
        "GCalSundayBgDark"       => GCalSundayBgDark,
        "GCalSaturdayBg"         => GCalSaturdayBg,
        "GCalSaturdayBgDark"     => GCalSaturdayBgDark,
        "GCalGridLine"           => GCalGridLine,
        "GCalGridLineDark"       => GCalGridLineDark,
        "GCalGridHalfLine"       => GCalGridHalfLine,
        "GCalGridHalfLineDark"   => GCalGridHalfLineDark,
        "GCalCurrentTimeLine"    => GCalCurrentTimeLine,
        "GCalAllDayTint"         => GCalAllDayTint,
        "GCalDragGhost"          => GCalDragGhost,
        _ => Gray
    };
}
