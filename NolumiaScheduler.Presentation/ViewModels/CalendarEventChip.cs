using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Helpers;
using Windows.UI;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class CalendarEventChip(EventOccurrence occ)
{
    private const int MinutesPerDay = 24 * 60;

    public string Title { get; } = occ.Title.Value;
    public string ToolTipText { get; } =
        occ.StartMinuteOfDay == 0 && occ.DurationMinutes == MinutesPerDay
            ? occ.Title.Value
            : $"{occ.Title.Value}\n{occ.StartTime.Hour:D2}:{occ.StartTime.Minute:D2} – {EndLabel(occ)}";
    // An explicitly assigned event color wins over the moved/overridden tints
    // (those states stay visible through their badges in the detail views).
    public Color ChipColor { get; } =
        occ.ColorKey != EventColorKey.Default ? WinColors.ForEventColor(occ.ColorKey) :
        occ.IsMoved      ? WinColors.GCalEventMoved :
        occ.IsOverridden ? WinColors.GCalGreen :
                           WinColors.GCalBlue;

    // End-of-day label derived from start + duration; a block ending at the day boundary reads 24:00.
    private static string EndLabel(EventOccurrence o)
    {
        var endFromStart = o.StartMinuteOfDay + o.DurationMinutes;
        if (endFromStart == MinutesPerDay) return "24:00";
        var endMinute = ((endFromStart % MinutesPerDay) + MinutesPerDay) % MinutesPerDay;
        return $"{(endMinute / 60):D2}:{(endMinute % 60):D2}";
    }
}
