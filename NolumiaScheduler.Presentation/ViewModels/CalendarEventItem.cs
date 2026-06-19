using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Helpers;
using NolumiaScheduler.Presentation.Resources.Strings;
using Windows.UI;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class CalendarEventItem
{
    private const int MinutesPerDay = 24 * 60;

    public CalendarEventItem(EventOccurrence occ)
    {
        Date = occ.Date.ToDateOnly().ToDateTime(TimeOnly.MinValue);
        EventId = occ.EventId.Value;
        // All-day is no longer a stored concept: it is derived as a midnight start spanning a full
        // day (docs/time-model.md). The end-of-day is start + duration; 24:00 is shown rather than
        // 00:00 when a block ends exactly at the day boundary.
        IsAllDay = occ.StartMinuteOfDay == 0 && occ.DurationMinutes == MinutesPerDay;
        // The key's time is the occurrence start (00:00 for all-day, not null), matching the key
        // the expander uses for exceptions/moves — otherwise skip/override of an all-day recurring
        // occurrence would never match and the occurrence would stay visible.
        OccurrenceKey = new OccurrenceLocalKey(occ.Date, occ.StartTime);
        SeriesKey = occ.SeriesKey;
        Title = occ.Title.Value;
        Location = occ.Location?.Value;
        IsMoved = occ.IsMoved;
        IsOverridden = occ.IsOverridden;

        StartMinuteOfDay = occ.StartMinuteOfDay;
        var endFromStart = occ.StartMinuteOfDay + occ.DurationMinutes;
        CrossesMidnight = endFromStart > MinutesPerDay;
        EndMinuteOfDay = CrossesMidnight ? endFromStart - MinutesPerDay : endFromStart;

        if (IsAllDay)
        {
            TimeRange = AppResources.AllDay;
        }
        else
        {
            var startLabel = $"{occ.StartTime.Hour:D2}:{occ.StartTime.Minute:D2}";
            // A block ending exactly at the day boundary reads as 24:00, not 00:00.
            var endLabel = !CrossesMidnight && endFromStart == MinutesPerDay
                ? "24:00"
                : $"{(EndMinuteOfDay / 60):D2}:{(EndMinuteOfDay % 60):D2}";
            TimeRange = $"{startLabel} – {endLabel}";
        }

        if (EndMinuteOfDay <= StartMinuteOfDay)
            EndMinuteOfDay = StartMinuteOfDay + 60;

        var badges = new List<string>();
        if (IsMoved) badges.Add(AppResources.BadgeMoved);
        if (IsOverridden) badges.Add(AppResources.BadgeModified);
        BadgeText = badges.Count > 0 ? string.Join("  ", badges) : null;

        ColorKey = occ.ColorKey;
        // An explicitly assigned event color wins over the moved/overridden tints;
        // those states remain visible through their badges.
        DotColor = ColorKey != EventColorKey.Default ? WinColors.ForEventColor(ColorKey) :
                   IsMoved ? WinColors.GCalEventMoved :
                   IsOverridden ? WinColors.GCalGreen :
                   WinColors.GCalBlue;
    }

    public string EventId { get; }
    public DateTime Date { get; }
    public OccurrenceLocalKey OccurrenceKey { get; }

    // Original recurrence key for moves/resizes; falls back to the displayed key.
    public OccurrenceLocalKey? SeriesKey { get; }
    public OccurrenceLocalKey MoveKey => SeriesKey ?? OccurrenceKey;
    public string Title { get; }
    public string? Location { get; }
    public string TimeRange { get; }
    public string? BadgeText { get; }
    public Color DotColor { get; }
    public EventColorKey ColorKey { get; }
    public bool IsAllDay { get; }
    public bool IsMoved { get; }
    public bool IsOverridden { get; }
    public int StartMinuteOfDay { get; }
    public int EndMinuteOfDay { get; }
    public bool CrossesMidnight { get; }
    public bool HasLocation => !string.IsNullOrEmpty(Location);
    public bool HasBadge => BadgeText != null;
}
