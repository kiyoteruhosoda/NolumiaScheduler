namespace NolumiaScheduler.Presentation.Services;

public interface IWeekAutoScrollService
{
    double ComputeVerticalDelta(double pointerY, double viewportHeight);
}
