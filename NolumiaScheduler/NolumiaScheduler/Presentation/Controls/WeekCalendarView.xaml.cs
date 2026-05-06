using Microsoft.Extensions.DependencyInjection;
using System.Collections;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaScheduler.Presentation.Controls;

public partial class WeekCalendarView : ContentView
{
    private readonly IWeekInteractionMapper _mapper;

    public event EventHandler<WeekEmptySlotTappedEventArgs>? EmptySlotTapped;
    public event EventHandler<WeekEventBlockTappedEventArgs>? EventBlockTapped;

    public WeekCalendarView()
    {
        InitializeComponent();
        _mapper = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services?.GetService<IWeekInteractionMapper>() ?? new WeekInteractionMapper();
    }

    public IEnumerable? WeekTimeSlots { get => (IEnumerable?)GetValue(WeekTimeSlotsProperty); set => SetValue(WeekTimeSlotsProperty, value); }
    public static readonly BindableProperty WeekTimeSlotsProperty = BindableProperty.Create(nameof(WeekTimeSlots), typeof(IEnumerable), typeof(WeekCalendarView));
    public IEnumerable? WeekDayColumns { get => (IEnumerable?)GetValue(WeekDayColumnsProperty); set => SetValue(WeekDayColumnsProperty, value); }
    public static readonly BindableProperty WeekDayColumnsProperty = BindableProperty.Create(nameof(WeekDayColumns), typeof(IEnumerable), typeof(WeekCalendarView));
    public double WeekCanvasHeight { get => (double)GetValue(WeekCanvasHeightProperty); set => SetValue(WeekCanvasHeightProperty, value); }
    public static readonly BindableProperty WeekCanvasHeightProperty = BindableProperty.Create(nameof(WeekCanvasHeight), typeof(double), typeof(WeekCalendarView), 1440d);
    public double WeekDayColumnWidth { get => (double)GetValue(WeekDayColumnWidthProperty); set => SetValue(WeekDayColumnWidthProperty, value); }
    public static readonly BindableProperty WeekDayColumnWidthProperty = BindableProperty.Create(nameof(WeekDayColumnWidth), typeof(double), typeof(WeekCalendarView), 120d);
    public bool IsCurrentWeek { get => (bool)GetValue(IsCurrentWeekProperty); set => SetValue(IsCurrentWeekProperty, value); }
    public static readonly BindableProperty IsCurrentWeekProperty = BindableProperty.Create(nameof(IsCurrentWeek), typeof(bool), typeof(WeekCalendarView), false);
    public double CurrentTimeLineTop { get => (double)GetValue(CurrentTimeLineTopProperty); set => SetValue(CurrentTimeLineTopProperty, value); }
    public static readonly BindableProperty CurrentTimeLineTopProperty = BindableProperty.Create(nameof(CurrentTimeLineTop), typeof(double), typeof(WeekCalendarView), 0d);

    private void OnEventBlockTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border b && b.BindingContext is WeekEventBlock block)
            EventBlockTapped?.Invoke(this, new WeekEventBlockTappedEventArgs { EventId = block.EventId, OccurrenceKey = block.OccurrenceKey });
    }

    private void OnEmptySlotTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not AbsoluteLayout layout || layout.BindingContext is not WeekDayColumn dayColumn) return;
        var point = e.GetPosition(layout);
        if (point is null) return;

        var startMinute = _mapper.MapToMinute(point.Value.Y);
        EmptySlotTapped?.Invoke(this, new WeekEmptySlotTappedEventArgs { Date = dayColumn.Date, StartMinute = startMinute });
    }
}
