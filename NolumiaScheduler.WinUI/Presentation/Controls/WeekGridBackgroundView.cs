using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NolumiaScheduler.Presentation.Helpers;

namespace NolumiaScheduler.WinUI.Presentation.Controls;

public partial class WeekGridBackgroundView : UserControl
{
    private readonly Canvas _canvas = new();

    public static readonly DependencyProperty HasAllDayEventsProperty =
        DependencyProperty.Register(nameof(HasAllDayEvents), typeof(bool), typeof(WeekGridBackgroundView),
            new PropertyMetadata(false, (d, _) => ((WeekGridBackgroundView)d).Redraw()));

    public bool HasAllDayEvents
    {
        get => (bool)GetValue(HasAllDayEventsProperty);
        set => SetValue(HasAllDayEventsProperty, value);
    }

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
        ActualThemeChanged += (_, _) => Redraw();
    }

    private bool IsDark => ActualTheme == ElementTheme.Dark;

    private void Redraw()
    {
        _canvas.Children.Clear();

        var width = ActualWidth;
        if (width <= 0) width = 120;

        var isDark = IsDark;

        if (HasAllDayEvents)
        {
            var height = ActualHeight > 0 ? ActualHeight : 1440;
            var tint = new Rectangle
            {
                Width = width,
                Height = height,
                Fill = new SolidColorBrush(WinColors.GCalAllDayTint)
            };
            _canvas.Children.Add(tint);
        }

        var hourStroke   = new SolidColorBrush(isDark ? WinColors.GCalGridLineDark     : WinColors.GCalGridLine);
        var halfStroke   = new SolidColorBrush(isDark ? WinColors.GCalGridHalfLineDark : WinColors.GCalGridHalfLine);

        for (var h = 0; h < 24; h++)
        {
            _canvas.Children.Add(new Line
            {
                X1 = 0, Y1 = h * 60, X2 = width, Y2 = h * 60,
                Stroke = hourStroke, StrokeThickness = 1.3
            });
            _canvas.Children.Add(new Line
            {
                X1 = 0, Y1 = h * 60 + 30, X2 = width, Y2 = h * 60 + 30,
                Stroke = halfStroke, StrokeThickness = 1.0
            });
        }

        // Vertical divider on the left edge so each day column is separated by a visible line.
        var gridHeight = ActualHeight > 0 ? ActualHeight : 1440;
        _canvas.Children.Add(new Line
        {
            X1 = 0, Y1 = 0, X2 = 0, Y2 = gridHeight,
            Stroke = hourStroke, StrokeThickness = 1.3
        });

        if (IsCurrentWeek)
        {
            _canvas.Children.Add(new Line
            {
                X1 = 0, Y1 = CurrentTimeLineTop, X2 = width, Y2 = CurrentTimeLineTop,
                Stroke = new SolidColorBrush(WinColors.GCalCurrentTimeLine),
                StrokeThickness = 2
            });
        }

        if (IsToday)
        {
            var height = ActualHeight > 0 ? ActualHeight : 1440;
            // Bottom of the today column frame: left + right + bottom borders, open at the top so
            // it continues down from the all-day lane above. Rounded bottom corners only.
            var todayFrame = new Border
            {
                Width = width,
                Height = height,
                BorderBrush = new SolidColorBrush(WinColors.GCalBlue),
                BorderThickness = new Thickness(2, 0, 2, 2),
                CornerRadius = new CornerRadius(0, 0, 6, 6)
            };
            Canvas.SetLeft(todayFrame, 0);
            Canvas.SetTop(todayFrame, 0);
            _canvas.Children.Add(todayFrame);
        }
    }
}
