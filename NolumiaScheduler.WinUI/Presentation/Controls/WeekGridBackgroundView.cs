using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace NolumiaScheduler.Presentation.Controls;

public sealed class WeekGridBackgroundView : UserControl
{
    private readonly Canvas _canvas = new();

    public static readonly DependencyProperty IsTodayProperty =
        DependencyProperty.Register(nameof(IsToday), typeof(bool), typeof(WeekGridBackgroundView),
            new PropertyMetadata(false, (d, _) => ((WeekGridBackgroundView)d).Redraw()));

    public static readonly DependencyProperty IsCurrentWeekProperty =
        DependencyProperty.Register(nameof(IsCurrentWeek), typeof(bool), typeof(WeekGridBackgroundView),
            new PropertyMetadata(false, (d, _) => ((WeekGridBackgroundView)d).Redraw()));

    public static readonly DependencyProperty CurrentTimeLineTopProperty =
        DependencyProperty.Register(nameof(CurrentTimeLineTop), typeof(double), typeof(WeekGridBackgroundView),
            new PropertyMetadata(0d, (d, _) => ((WeekGridBackgroundView)d).Redraw()));

    public bool IsToday
    {
        get => (bool)GetValue(IsTodayProperty);
        set => SetValue(IsTodayProperty, value);
    }

    public bool IsCurrentWeek
    {
        get => (bool)GetValue(IsCurrentWeekProperty);
        set => SetValue(IsCurrentWeekProperty, value);
    }

    public double CurrentTimeLineTop
    {
        get => (double)GetValue(CurrentTimeLineTopProperty);
        set => SetValue(CurrentTimeLineTopProperty, value);
    }

    public WeekGridBackgroundView()
    {
        Content = _canvas;
        SizeChanged += (_, _) => Redraw();
    }

    private void Redraw()
    {
        _canvas.Children.Clear();

        var width = ActualWidth;
        if (width <= 0) width = 120;

        // Hour and half-hour grid lines
        for (var h = 0; h < 24; h++)
        {
            var hourLine = new Line
            {
                X1 = 0, Y1 = h * 60,
                X2 = width, Y2 = h * 60,
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 208, 215, 222)),
                StrokeThickness = 1.3
            };
            _canvas.Children.Add(hourLine);

            var halfLine = new Line
            {
                X1 = 0, Y1 = h * 60 + 30,
                X2 = width, Y2 = h * 60 + 30,
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(90, 208, 215, 222)),
                StrokeThickness = 1.0
            };
            _canvas.Children.Add(halfLine);
        }

        // Current time line (only for current week)
        if (IsCurrentWeek)
        {
            var y = CurrentTimeLineTop;
            var timeLine = new Line
            {
                X1 = 0, Y1 = y,
                X2 = width, Y2 = y,
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 234, 67, 53)),
                StrokeThickness = 2
            };
            _canvas.Children.Add(timeLine);
        }

        // Today highlight border
        if (IsToday)
        {
            var height = ActualHeight > 0 ? ActualHeight : 1440;
            var rect = new Rectangle
            {
                Width = Math.Max(0, width - 3),
                Height = Math.Max(0, height - 3),
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 115, 232)),
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                RadiusX = 6,
                RadiusY = 6
            };
            Canvas.SetLeft(rect, 1.5);
            Canvas.SetTop(rect, 1.5);
            _canvas.Children.Add(rect);
        }
    }
}
