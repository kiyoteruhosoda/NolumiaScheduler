using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaScheduler.Presentation.Services;

public sealed class DefaultWeekAllDayLayoutStrategy : IWeekAllDayLayoutStrategy
{
    public IReadOnlyList<WeekAllDayEventBlock> Layout(IReadOnlyList<CalendarEventItem> events, DateTime weekStartDate)
    {
        var weekEnd = weekStartDate.Date.AddDays(6);
        var spans = events
            .Where(e => e.IsAllDay)
            .Select(e => new SpanItem(e, e.Date.Date, e.Date.Date))
            .Where(s => s.EndDate >= weekStartDate.Date && s.StartDate <= weekEnd)
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.EndDate)
            .ToList();

        var rows = new List<List<SpanItem>>();
        foreach (var span in spans)
        {
            var clampedStart = span.StartDate < weekStartDate.Date ? weekStartDate.Date : span.StartDate;
            var clampedEnd = span.EndDate > weekEnd ? weekEnd : span.EndDate;
            span.LeftColumn = (int)(clampedStart - weekStartDate.Date).TotalDays;
            span.WidthColumns = Math.Max(1, (int)(clampedEnd - clampedStart).TotalDays + 1);

            var row = 0;
            while (row < rows.Count && rows[row].Any(x => IsOverlap(x, span))) row++;
            if (row == rows.Count) rows.Add([]);
            rows[row].Add(span);
            span.Row = row;
        }

        return [.. spans.Select(s => new WeekAllDayEventBlock
        {
            EventId = s.Item.EventId,
            OccurrenceKey = s.Item.OccurrenceKey,
            StartDate = s.StartDate,
            EndDate = s.EndDate,
            LeftColumn = s.LeftColumn,
            WidthColumns = s.WidthColumns,
            Row = s.Row,
            Title = s.Item.Title,
            BackgroundColor = s.Item.DotColor,
            LeftRatio = s.LeftColumn / 7d,
            WidthRatio = s.WidthColumns / 7d,
            IsRecurring = s.Item.IsRecurring,
        })];
    }

    private static bool IsOverlap(SpanItem a, SpanItem b)
        => a.LeftColumn < b.LeftColumn + b.WidthColumns && b.LeftColumn < a.LeftColumn + a.WidthColumns;

    private sealed class SpanItem(CalendarEventItem item, DateTime startDate, DateTime endDate)
    {
        public CalendarEventItem Item { get; } = item;
        public DateTime StartDate { get; } = startDate;
        public DateTime EndDate { get; } = endDate;
        public int LeftColumn { get; set; }
        public int WidthColumns { get; set; }
        public int Row { get; set; }
    }
}
