using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaScheduler.Presentation.Services;

public sealed class DefaultWeekEventLayoutStrategy : IWeekEventLayoutStrategy
{
    private const int MinimumEventHeight = 24;

    public IReadOnlyList<WeekEventBlock> Layout(IReadOnlyList<CalendarEventItem> events)
    {
        var segments = events
            .Where(e => !e.IsAllDay)
            .Select(e => new Segment(e, e.StartMinuteOfDay, e.EndMinuteOfDay))
            .OrderBy(s => s.Start)
            .ThenBy(s => s.End)
            .ToList();

        var grouped = GroupOverlaps(segments);
        foreach (var group in grouped)
        {
            AssignColumns(group);
        }

        return segments.Select(ToBlock).ToList();
    }

    private static List<List<Segment>> GroupOverlaps(IReadOnlyList<Segment> ordered)
    {
        var groups = new List<List<Segment>>();
        if (ordered.Count == 0) return groups;

        var current = new List<Segment>();
        var currentMaxEnd = -1;
        foreach (var segment in ordered)
        {
            if (current.Count == 0 || segment.Start < currentMaxEnd)
            {
                current.Add(segment);
                currentMaxEnd = Math.Max(currentMaxEnd, segment.End);
                continue;
            }

            groups.Add(current);
            current = [segment];
            currentMaxEnd = segment.End;
        }

        if (current.Count > 0) groups.Add(current);
        return groups;
    }

    private static void AssignColumns(List<Segment> group)
    {
        var active = new List<Segment>();
        var maxColumn = 0;

        foreach (var segment in group.OrderBy(s => s.Start).ThenBy(s => s.End))
        {
            active.RemoveAll(a => a.End <= segment.Start);
            var used = active.Select(a => a.Column).ToHashSet();

            var column = 0;
            while (used.Contains(column)) column++;

            segment.Column = column;
            maxColumn = Math.Max(maxColumn, column);
            active.Add(segment);
        }

        var columnCount = maxColumn + 1;
        foreach (var segment in group)
        {
            segment.ColumnCount = columnCount;
        }
    }

    private static WeekEventBlock ToBlock(Segment segment)
    {
        var duration = Math.Max(MinimumEventHeight, segment.End - segment.Start);

        return new WeekEventBlock
        {
            EventId = segment.Item.EventId,
            OccurrenceKey = segment.Item.OccurrenceKey,
            Date = segment.Item.OccurrenceKey.Date.ToDateOnly().ToDateTime(TimeOnly.MinValue),
            StartMinute = segment.Item.StartMinuteOfDay,
            EndMinute = segment.Item.EndMinuteOfDay,
            Title = segment.Item.Title,
            TimeLabel = segment.Item.TimeRange,
            BackgroundColor = segment.Item.DotColor,
            Top = segment.Start,
            Height = duration,
            LeftRatio = segment.ColumnCount <= 1 ? 0 : (double)segment.Column / segment.ColumnCount,
            WidthRatio = segment.ColumnCount <= 1 ? 1 : 1d / segment.ColumnCount,
            Bounds = new Rect(
                segment.ColumnCount <= 1 ? 0 : (double)segment.Column / segment.ColumnCount,
                segment.Start,
                segment.ColumnCount <= 1 ? 1 : 1d / segment.ColumnCount,
                duration),
        };
    }

    private sealed class Segment(CalendarEventItem item, int start, int end)
    {
        public CalendarEventItem Item { get; } = item;
        public int Start { get; } = start;
        public int End { get; } = Math.Max(start + 1, end);
        public int Column { get; set; }
        public int ColumnCount { get; set; } = 1;
    }
}
