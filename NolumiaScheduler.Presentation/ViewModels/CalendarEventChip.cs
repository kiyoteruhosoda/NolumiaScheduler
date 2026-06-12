using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Helpers;
using Windows.UI;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class CalendarEventChip(EventOccurrence occ)
{
    public string Title { get; } = occ.Title.Value;
    public string ToolTipText { get; } = occ.IsAllDay
        ? occ.Title.Value
        : $"{occ.Title.Value}\n{occ.StartTime.Hour:D2}:{occ.StartTime.Minute:D2} – {occ.EndTime.Hour:D2}:{occ.EndTime.Minute:D2}";
    // An explicitly assigned event color wins over the moved/overridden tints
    // (those states stay visible through their badges in the detail views).
    public Color ChipColor { get; } =
        occ.ColorKey != EventColorKey.Default ? WinColors.ForEventColor(occ.ColorKey) :
        occ.IsMoved      ? WinColors.GCalEventMoved :
        occ.IsOverridden ? WinColors.GCalGreen :
                           WinColors.GCalBlue;
}
