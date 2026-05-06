namespace NolumiaScheduler.Presentation.Services;

public interface IWeekInteractionMapper
{
    DateTime MapToDate(double x, DateTime weekStartDate, double dayColumnWidth);
    int MapToMinute(double y);
    double MapToY(int minuteOfDay);
    double MapToX(DateTime date, DateTime weekStartDate, double dayColumnWidth);
    int SnapToQuarterHour(int minuteOfDay);
}
