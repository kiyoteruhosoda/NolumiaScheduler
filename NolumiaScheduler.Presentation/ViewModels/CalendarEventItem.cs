using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Helpers;
using NolumiaScheduler.Presentation.Resources.Strings;
using Windows.UI;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class CalendarEventItem
{
    public CalendarEventItem(EventOccurrence occ)
    {
        Date = occ.Date.ToDateOnly().ToDateTime(TimeOnly.MinValue);
        EventId = occ.EventId.Value;
        OccurrenceKey = new OccurrenceLocalKey(occ.Date, occ.AllDay ? null : occ.StartTime);
        SeriesKey = occ.SeriesKey;
        Title = occ.Title.Value;
        Location = occ.Location?.Value;
        IsAllDay = occ.AllDay;
        IsMoved = occ.IsMoved;
        IsOverridden = occ.IsOverridden;

        if (occ.AllDay)
        {
            TimeRange = AppResources.AllDay;
            StartMinuteOfDay = 0;
            EndMinuteOfDay = 60;
        }
        else if (occ.StartTime != null && occ.EndTime != null)
        {
            TimeRange = $"{occ.StartTime.Hour:D2}:{occ.StartTime.Minute:D2} – {occ.EndTime.Hour:D2}:{occ.EndTime.Minute:D2}";
            StartMinuteOfDay = (occ.StartTime.Hour * 60) + occ.StartTime.Minute;
            EndMinuteOfDay = (occ.EndTime.Hour * 60) + occ.EndTime.Minute;
            CrossesMidnight = EndMinuteOfDay <= StartMinuteOfDay;
        }
        else
        {
            TimeRange = "";
            StartMinuteOfDay = 0;
            EndMinuteOfDay = 60;
        }

        if (EndMinuteOfDay <= StartMinuteOfDay)
            EndMinuteOfDay = StartMinuteOfDay + 60;

        var badges = new List<string>();
        if (IsMoved) badges.Add(AppResources.BadgeMoved);
        if (IsOverridden) badges.Add(AppResources.BadgeModified);
        BadgeText = badges.Count > 0 ? string.Join("  ", badges) : null;

        DotColor = IsMoved ? WinColors.GCalEventMoved :
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
    public bool IsAllDay { get; }
    public bool IsMoved { get; }
    public bool IsOverridden { get; }
    public int StartMinuteOfDay { get; }
    public int EndMinuteOfDay { get; }
    public bool CrossesMidnight { get; }
    public bool HasLocation => !string.IsNullOrEmpty(Location);
    public bool HasBadge => BadgeText != null;
}
