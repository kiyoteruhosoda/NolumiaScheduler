using NolumiaScheduler.Presentation.ViewModels;
using Windows.Foundation;

namespace NolumiaScheduler.Presentation.Services;

public sealed class DefaultWeekEventLayoutStrategy : IWeekEventLayoutStrategy
{
    // Events render at their true duration (1px == 1 minute), so a 15-minute event is 15px
    // tall. The floor only guards against zero/negative durations.
    private const int MinimumEventHeight = 15;
    private const double ColumnGapRatio = 0.015;

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

        return [.. segments.Select(ToBlock)];
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
        var columns = new List<List<Segment>>();

        foreach (var segment in group.OrderBy(s => s.Start).ThenBy(s => s.End))
        {
            var column = 0;
            while (column < columns.Count && columns[column].Any(existing => Overlaps(existing, segment)))
            {
                column++;
            }

            if (column == columns.Count)
            {
                columns.Add([]);
            }

            segment.Column = column;
            columns[column].Add(segment);
        }

        // Every event in a connected group shares the same column count (the maximum number
        // of simultaneously overlapping events) so the columns tile consistently. Using a
        // per-event overlap count instead makes long events that touch several
        // non-overlapping neighbours too narrow and misaligned with those neighbours.
        var groupColumns = columns.Count;
        foreach (var segment in group)
        {
            segment.GroupColumns = groupColumns;
            segment.ColumnSpan = ResolveExpandableSpan(segment, columns);
        }
    }

    private static int ResolveExpandableSpan(Segment pivot, IReadOnlyList<List<Segment>> columns)
    {
        var span = 1;
        for (var nextColumn = pivot.Column + 1; nextColumn < columns.Count; nextColumn++)
        {
            if (columns[nextColumn].Any(other => Overlaps(pivot, other)))
                break;

            span++;
        }

        return Math.Max(1, span);
    }

    private static bool Overlaps(Segment left, Segment right)
        => left.Start < right.End && right.Start < left.End;

    private static WeekEventBlock ToBlock(Segment segment)
    {
        var duration = Math.Max(MinimumEventHeight, segment.End - segment.Start);
        double leftRatio, widthRatio;
        if (segment.GroupColumns <= 1)
        {
            leftRatio  = 0d;
            widthRatio = 1d;
        }
        else
        {
            // 列幅はグループ共通の列数で割り、右側に空き列があれば ColumnSpan の分だけ拡張する
            var totalColumns = segment.GroupColumns;
            var baseWidthRatio = (1d - ColumnGapRatio * (totalColumns - 1)) / totalColumns;
            widthRatio = baseWidthRatio * segment.ColumnSpan + ColumnGapRatio * (segment.ColumnSpan - 1);
            leftRatio  = segment.Column * (baseWidthRatio + ColumnGapRatio);
        }

        return new WeekEventBlock
        {
            EventId = segment.Item.EventId,
            OccurrenceKey = segment.Item.OccurrenceKey,
            MoveKey = segment.Item.MoveKey,
            Date = segment.Item.OccurrenceKey.Date.ToDateOnly().ToDateTime(TimeOnly.MinValue),
            StartMinute = segment.Item.StartMinuteOfDay,
            EndMinute = segment.Item.EndMinuteOfDay,
            Title = segment.Item.Title,
            TimeLabel = segment.Item.TimeRange,
            BackgroundColor = segment.Item.DotColor,
            Top = segment.Start,
            Height = duration,
            LeftRatio = leftRatio,
            WidthRatio = widthRatio,
            Bounds = new Windows.Foundation.Rect(leftRatio, segment.Start, widthRatio, duration),
            ResizeHandleBounds = new Windows.Foundation.Rect(
                leftRatio,
                Math.Max(segment.Start, segment.Start + duration - 16),
                widthRatio,
                16),
        };
    }

    private sealed class Segment(CalendarEventItem item, int start, int end)
    {
        public CalendarEventItem Item { get; } = item;
        public int Start { get; } = start;
        public int End { get; } = Math.Max(start + 1, end);
        public int Column { get; set; }
        public int GroupColumns { get; set; } = 1;
        public int ColumnSpan { get; set; } = 1;
    }
}
