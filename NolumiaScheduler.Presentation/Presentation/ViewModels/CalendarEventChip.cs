using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Helpers;
using Windows.UI;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class CalendarEventChip(EventOccurrence occ)
{
    public string Title { get; } = occ.Title.Value;
    public Color ChipColor { get; } = occ.IsMoved ? WinColors.GCalEventMoved :
                    occ.IsOverridden ? WinColors.GCalGreen :
                                       WinColors.GCalBlue;
}
