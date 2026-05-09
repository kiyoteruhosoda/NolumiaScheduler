using NolumiaScheduler.Domain.ValueObjects;
using MauiApplication = Microsoft.Maui.Controls.Application;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class CalendarEventChip
{
    public string Title { get; }
    public Color ChipColor { get; }

    public CalendarEventChip(EventOccurrence occ)
    {
        Title = occ.Title.Value;
        ChipColor = occ.IsMoved      ? ResourceColor("GCalEventMoved") :
                    occ.IsOverridden ? ResourceColor("GCalGreen") :
                                       ResourceColor("GCalBlue");
    }

    private static Color ResourceColor(string key)
    {
        if (MauiApplication.Current?.Resources.TryGetValue(key, out var val) == true && val is Color c)
            return c;
        return key switch
        {
            "GCalBlue"       => Color.FromArgb("#1a73e8"),
            "GCalGreen"      => Color.FromArgb("#1e8e3e"),
            "GCalEventMoved" => Color.FromArgb("#9c27b0"),
            _                => Colors.Gray
        };
    }
}
