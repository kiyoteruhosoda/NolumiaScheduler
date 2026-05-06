namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class DefaultWeekEventLayoutStrategy : IWeekEventLayoutStrategy
{
    public IReadOnlyList<WeekEventBlock> Layout(IReadOnlyList<CalendarEventItem> events)
    {
        var segments = events.Select(e => new Segment(e, e.IsAllDay ? 0 : e.StartMinuteOfDay, e.IsAllDay ? 60 : e.EndMinuteOfDay))
            .OrderBy(s => s.Start).ThenBy(s => s.End).ToList();

        var blocks = new List<WeekEventBlock>();
        var active = new List<Segment>();

        foreach (var seg in segments)
        {
            active.RemoveAll(a => a.End <= seg.Start);
            var used = active.Select(a => a.Column).ToHashSet();
            var col = 0;
            while (used.Contains(col)) col++;
            seg.Column = col;
            active.Add(seg);
            seg.ColumnCount = Math.Max(seg.ColumnCount, active.Max(a => a.Column) + 1);
            foreach (var a in active) a.ColumnCount = Math.Max(a.ColumnCount, seg.ColumnCount);
        }

        foreach (var s in segments)
        {
            var duration = Math.Max(20, s.End - s.Start);
            blocks.Add(new WeekEventBlock
            {
                Title = s.Item.Title,
                TimeLabel = s.Item.TimeRange,
                BackgroundColor = s.Item.DotColor,
                Top = s.Start,
                Height = duration,
                LeftRatio = s.ColumnCount <= 1 ? 0 : (double)s.Column / s.ColumnCount,
                WidthRatio = s.ColumnCount <= 1 ? 1 : 1d / s.ColumnCount,
            });
        }

        return blocks;
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

