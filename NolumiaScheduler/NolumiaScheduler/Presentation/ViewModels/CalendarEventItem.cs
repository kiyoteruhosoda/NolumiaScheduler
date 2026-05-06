using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Resources.Strings;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class CalendarEventItem
{
    public CalendarEventItem(EventOccurrence occ)
    {
        Title = occ.Title.Value;
        Location = occ.Location?.Value;
        IsAllDay = occ.AllDay;
        IsMoved = occ.IsMoved;
        IsOverridden = occ.IsOverridden;

        if (occ.AllDay)
        {
            TimeRange = AppResources.AllDay;
        }
        else if (occ.StartTime != null && occ.EndTime != null)
        {
            TimeRange = $"{occ.StartTime.Hour:D2}:{occ.StartTime.Minute:D2} – {occ.EndTime.Hour:D2}:{occ.EndTime.Minute:D2}";
        }
        else
        {
            TimeRange = "";
        }

        var badges = new List<string>();
        if (IsMoved) badges.Add(AppResources.BadgeMoved);
        if (IsOverridden) badges.Add(AppResources.BadgeModified);
        BadgeText = badges.Count > 0 ? string.Join("  ", badges) : null;

        DotColor = IsMoved ? Color.FromArgb("#9c27b0") :
                   IsOverridden ? Color.FromArgb("#1e8e3e") :
                   Color.FromArgb("#1a73e8");
    }

    public string Title { get; }
    public string? Location { get; }
    public string TimeRange { get; }
    public string? BadgeText { get; }
    public Color DotColor { get; }
    public bool IsAllDay { get; }
    public bool IsMoved { get; }
    public bool IsOverridden { get; }
    public bool HasLocation => !string.IsNullOrEmpty(Location);
    public bool HasBadge => BadgeText != null;
}
