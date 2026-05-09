using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaScheduler.Presentation.Controls;

public sealed class WeekGridBackgroundView : GraphicsView
{
    public static readonly BindableProperty IsTodayProperty = BindableProperty.Create(nameof(IsToday), typeof(bool), typeof(WeekGridBackgroundView), false, propertyChanged: Redraw);
    public static readonly BindableProperty IsCurrentWeekProperty = BindableProperty.Create(nameof(IsCurrentWeek), typeof(bool), typeof(WeekGridBackgroundView), false, propertyChanged: Redraw);
    public static readonly BindableProperty CurrentTimeLineTopProperty = BindableProperty.Create(nameof(CurrentTimeLineTop), typeof(double), typeof(WeekGridBackgroundView), 0d, propertyChanged: Redraw);

    public bool IsToday { get => (bool)GetValue(IsTodayProperty); set => SetValue(IsTodayProperty, value); }
    public bool IsCurrentWeek { get => (bool)GetValue(IsCurrentWeekProperty); set => SetValue(IsCurrentWeekProperty, value); }
    public double CurrentTimeLineTop { get => (double)GetValue(CurrentTimeLineTopProperty); set => SetValue(CurrentTimeLineTopProperty, value); }

    public WeekGridBackgroundView() => Drawable = new DrawableHost(this);

    private static void Redraw(BindableObject b, object o, object n) => ((WeekGridBackgroundView)b).Invalidate();

    private sealed class DrawableHost(WeekGridBackgroundView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            for (var h = 0; h < 24; h++)
            {
                var y = h * 60;
                canvas.StrokeColor = Color.FromArgb("#D0D7DE");
                canvas.StrokeSize = 1.3f;
                canvas.DrawLine(0, y, dirtyRect.Width, y);

                canvas.StrokeColor = Color.FromArgb("#D0D7DE").WithAlpha(0.35f);
                canvas.StrokeSize = 1f;
                canvas.DrawLine(0, y + 30, dirtyRect.Width, y + 30);
            }

            if (owner.IsCurrentWeek)
            {
                var y = (float)owner.CurrentTimeLineTop;
                canvas.StrokeColor = Color.FromArgb("#EA4335");
                canvas.StrokeSize = 2;
                canvas.DrawLine(0, y, dirtyRect.Width, y);
            }

            if (!owner.IsToday) return;

            canvas.StrokeColor = Color.FromArgb("#1A73E8");
            canvas.StrokeSize = 2f;
            const float inset = 1.5f;
            var rect = new RectF(
                inset,
                inset,
                Math.Max(0, dirtyRect.Width - inset * 2),
                Math.Max(0, dirtyRect.Height - inset * 2));
            canvas.DrawRoundedRectangle(rect, 6);
        }
    }
}
