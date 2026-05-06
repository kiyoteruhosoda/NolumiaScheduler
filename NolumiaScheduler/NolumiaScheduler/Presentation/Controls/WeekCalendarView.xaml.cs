using Microsoft.Extensions.DependencyInjection;
using System.Collections;
using System.Collections.ObjectModel;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaScheduler.Presentation.Controls;

public partial class WeekCalendarView : ContentView
{
    private readonly IWeekInteractionMapper _mapper;
    private WeekInteractionState _interactionState = WeekInteractionState.Idle;
    private WeekEventBlock? _activeBlock;
    private Point _pressStartPoint;
    private Point _lastPoint;

    public WeekInteractionPreview InteractionPreview { get; private set; } = new() { IsVisible = false };

    public event EventHandler<WeekEmptySlotTappedEventArgs>? EmptySlotTapped;
    public event EventHandler<WeekEventBlockTappedEventArgs>? EventBlockTapped;
    public event EventHandler<WeekEventDragCompletedEventArgs>? EventDragCompleted;
    public event EventHandler<WeekEventResizeCompletedEventArgs>? EventResizeCompleted;

    public WeekCalendarView()
    {
        InitializeComponent();
        _mapper = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services?.GetService<IWeekInteractionMapper>() ?? new WeekInteractionMapper();
    }

    protected override async void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        await ScrollToCurrentTimeAsync();
    }

    public async Task ScrollToCurrentTimeAsync()
    {
        var y = Math.Max(0, CurrentTimeLineTop - 240);
        await WeekScroll.ScrollToAsync(0, y, false);
    }


    private void TransitionTo(WeekInteractionState next, WeekEventBlock? block = null, Point? point = null)
    {
        _interactionState = next;
        _activeBlock = block ?? _activeBlock;
        if (point != null)
        {
            _lastPoint = point.Value;
            if (next == WeekInteractionState.Pressed || next == WeekInteractionState.LongPressPending)
                _pressStartPoint = point.Value;
        }
    }

    private bool ShouldTreatAsTap(Point point)
        => Math.Abs(point.X - _pressStartPoint.X) < 6 && Math.Abs(point.Y - _pressStartPoint.Y) < 6;

    private void UpdatePreview(string eventId, DateTime date, int startMinute, int endMinute)
    {
        InteractionPreview = new WeekInteractionPreview
        {
            IsVisible = true,
            EventId = eventId,
            Date = date,
            StartMinute = startMinute,
            EndMinute = endMinute
        };
    }

    private void ClearPreview() => InteractionPreview = new WeekInteractionPreview { IsVisible = false };

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

    public string? SelectedEventId { get => (string?)GetValue(SelectedEventIdProperty); set => SetValue(SelectedEventIdProperty, value); }
    public static readonly BindableProperty SelectedEventIdProperty = BindableProperty.Create(nameof(SelectedEventId), typeof(string), typeof(WeekCalendarView), null);

    private void OnEventBlockTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border b && b.BindingContext is WeekEventBlock block)
        {
            var point = new Point(block.LeftRatio, block.Top);
            TransitionTo(WeekInteractionState.Pressed, block, point);
            if (!ShouldTreatAsTap(point)) return;

            SelectedEventId = block.EventId;
            UpdateSelectionState();
            TransitionTo(WeekInteractionState.Idle, null, point);
            EventBlockTapped?.Invoke(this, new WeekEventBlockTappedEventArgs
            {
                EventId = block.EventId,
                Date = block.Date,
                StartMinute = block.StartMinute,
                OccurrenceKey = block.OccurrenceKey
            });
        }
    }


    private void UpdateSelectionState()
    {
        if (WeekDayColumns is not IEnumerable cols) return;
        foreach (var c in cols)
        {
            if (c is WeekDayColumn day)
            {
                foreach (var b in day.EventBlocks)
                    b.IsSelected = b.EventId == SelectedEventId;
            }
        }
    }


    // Resize本実装前の統合ポイント: 将来下端ドラッグから呼ぶ
    public void CompleteResize(string eventId, DateTime date, int startMinute, double previewHeight)
    {
        TransitionTo(WeekInteractionState.DraggingResize, _activeBlock, new Point(0, previewHeight));
        var endMinute = Math.Max(startMinute + 15, _mapper.HeightToMinute(previewHeight));
        UpdatePreview(eventId, date, startMinute, endMinute);
        EventResizeCompleted?.Invoke(this, new WeekEventResizeCompletedEventArgs
        {
            EventId = eventId,
            Date = date,
            StartMinute = startMinute,
            EndMinute = endMinute
        });
        TransitionTo(WeekInteractionState.Idle);
        ClearPreview();
    }

    // Drag本実装前の統合ポイント: 将来Pan/LongPressから呼ぶ
    public void CompleteDrag(string eventId, Point point, DateTime weekStartDate)
    {
        TransitionTo(WeekInteractionState.DraggingMove, _activeBlock, point);
        var dt = _mapper.MapToDateTime(point, weekStartDate, WeekDayColumnWidth);
        UpdatePreview(eventId, dt.Date, dt.Hour * 60 + dt.Minute, dt.Hour * 60 + dt.Minute + 60);
        EventDragCompleted?.Invoke(this, new WeekEventDragCompletedEventArgs
        {
            EventId = eventId,
            TargetDateTime = dt,
            TargetStartMinute = dt.Hour * 60 + dt.Minute
        });
        TransitionTo(WeekInteractionState.Idle);
        ClearPreview();
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
