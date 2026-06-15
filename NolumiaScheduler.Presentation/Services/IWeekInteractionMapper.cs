using Windows.Foundation;

namespace NolumiaScheduler.Presentation.Services;

public interface IWeekInteractionMapper
{
    DateTime MapToDate(double x, DateTime weekStartDate, double dayColumnWidth);
    int MapToMinute(double y);
    int MapToHalfHourMinute(double y);
    double MapToY(int minuteOfDay);
    double MapToX(DateTime date, DateTime weekStartDate, double dayColumnWidth);
    int SnapToQuarterHour(int minuteOfDay);
    int SnapToHalfHour(int minuteOfDay);
    DateTime MapToDateTime(Point point, DateTime weekStartDate, double dayColumnWidth);
    Point MapToPoint(DateTime dateTime, DateTime weekStartDate, double dayColumnWidth);
    double MinuteToHeight(int minute);
    int HeightToMinute(double height);
    int SnapMinute(int minute);
}
