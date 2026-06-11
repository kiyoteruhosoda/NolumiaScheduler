using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Helpers;
using Windows.UI;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class CalendarEventChip(EventOccurrence occ)
{
    public string Title { get; } = occ.Title.Value;
    // An explicitly assigned event color wins over the moved/overridden tints
    // (those states stay visible through their badges in the detail views).
    public Color ChipColor { get; } =
        occ.ColorKey != EventColorKey.Default ? WinColors.ForEventColor(occ.ColorKey) :
        occ.IsMoved      ? WinColors.GCalEventMoved :
        occ.IsOverridden ? WinColors.GCalGreen :
                           WinColors.GCalBlue;
}
