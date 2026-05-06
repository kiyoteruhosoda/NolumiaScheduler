namespace NolumiaScheduler.Presentation.Services;

public sealed class WeekInteractionMapper : IWeekInteractionMapper
{
    public DateTime MapToDate(double x, DateTime weekStartDate, double dayColumnWidth)
    {
        var dayOffset = (int)Math.Clamp(Math.Floor(x / Math.Max(1, dayColumnWidth)), 0, 6);
        return weekStartDate.Date.AddDays(dayOffset);
    }

    public int MapToMinute(double y) => SnapToQuarterHour((int)Math.Clamp(Math.Round(y), 0, 1439));

    public double MapToY(int minuteOfDay) => Math.Clamp(minuteOfDay, 0, 1439);

    public double MapToX(DateTime date, DateTime weekStartDate, double dayColumnWidth)
    {
        var diff = (date.Date - weekStartDate.Date).TotalDays;
        return Math.Clamp(diff, 0, 6) * dayColumnWidth;
    }

    public int SnapToQuarterHour(int minuteOfDay) => (int)(Math.Round(minuteOfDay / 15d) * 15);
}
