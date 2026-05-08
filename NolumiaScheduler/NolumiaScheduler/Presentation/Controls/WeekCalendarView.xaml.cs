using Microsoft.Extensions.DependencyInjection;
using System.Collections;
using System.Collections.ObjectModel;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaScheduler.Presentation.Controls;

public partial class WeekCalendarView : ContentView
{
    private readonly IWeekInteractionMapper _mapper;
    private readonly IWeekGestureArbitrationService _gestureArbitrationService;
    private readonly IWeekAutoScrollService _autoScrollService;
    private WeekInteractionState _interactionState = WeekInteractionState.Idle;
    private WeekEventBlock? _activeBlock;
    private Point _pressStartPoint;
    private Point _lastPoint;
    private bool _isResizing;
    private DateTime _suppressTapUntilUtc;
    private DateTime _lastInteractionFrameUtc = DateTime.MinValue;

    public WeekInteractionPreview InteractionPreview
    {
        get => (WeekInteractionPreview)GetValue(InteractionPreviewProperty);
        private set => SetValue(InteractionPreviewProperty, value);
    }
    public static readonly BindableProperty InteractionPreviewProperty =
        BindableProperty.Create(nameof(InteractionPreview), typeof(WeekInteractionPreview), typeof(WeekCalendarView), new WeekInteractionPreview { IsVisible = false });

    public event EventHandler<WeekEmptySlotTappedEventArgs>? EmptySlotTapped;
    public event EventHandler<WeekEventBlockTappedEventArgs>? EventBlockTapped;
    public event EventHandler<WeekEventDragCompletedEventArgs>? EventDragCompleted;
    public event EventHandler<WeekEventResizeCompletedEventArgs>? EventResizeCompleted;

    public WeekCalendarView()
    {
        InitializeComponent();
        _mapper = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services?.GetService<IWeekInteractionMapper>() ?? new WeekInteractionMapper();
        _gestureArbitrationService = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services?.GetService<IWeekGestureArbitrationService>() ?? new WeekGestureArbitrationService();
        _autoScrollService = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services?.GetService<IWeekAutoScrollService>() ?? new WeekAutoScrollService();
        BindableLayout.SetItemTemplate(WeekAllDayEventsLayer, CreateAllDayEventTemplate());
        SizeChanged += (_, _) => BuildWeekColumns();
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
            EndMinute = endMinute,
            LeftRatio = _activeBlock?.LeftRatio ?? 0,
            WidthRatio = _activeBlock?.WidthRatio ?? 1
        };
    }

    private void ClearPreview() => InteractionPreview = new WeekInteractionPreview { IsVisible = false };

    public IEnumerable? WeekTimeSlots { get => (IEnumerable?)GetValue(WeekTimeSlotsProperty); set => SetValue(WeekTimeSlotsProperty, value); }
    public static readonly BindableProperty WeekTimeSlotsProperty = BindableProperty.Create(nameof(WeekTimeSlots), typeof(IEnumerable), typeof(WeekCalendarView));
    public IEnumerable? WeekDayColumns { get => (IEnumerable?)GetValue(WeekDayColumnsProperty); set => SetValue(WeekDayColumnsProperty, value); }
    public static readonly BindableProperty WeekDayColumnsProperty = BindableProperty.Create(
        nameof(WeekDayColumns),
        typeof(IEnumerable),
        typeof(WeekCalendarView),
        propertyChanged: (bindable, _, _) => { var view = (WeekCalendarView)bindable; view.UpdateVirtualizedRanges(); view.BuildWeekColumns(); });
    public IEnumerable? WeekAllDayEventBlocks { get => (IEnumerable?)GetValue(WeekAllDayEventBlocksProperty); set => SetValue(WeekAllDayEventBlocksProperty, value); }
    public static readonly BindableProperty WeekAllDayEventBlocksProperty = BindableProperty.Create(nameof(WeekAllDayEventBlocks), typeof(IEnumerable), typeof(WeekCalendarView), propertyChanged: (_, _, _) => { });
    public double WeekAllDayLaneHeight { get => (double)GetValue(WeekAllDayLaneHeightProperty); set => SetValue(WeekAllDayLaneHeightProperty, value); }
    public static readonly BindableProperty WeekAllDayLaneHeightProperty = BindableProperty.Create(nameof(WeekAllDayLaneHeight), typeof(double), typeof(WeekCalendarView), 28d);
    public double WeekCanvasHeight { get => (double)GetValue(WeekCanvasHeightProperty); set => SetValue(WeekCanvasHeightProperty, value); }
    public static readonly BindableProperty WeekCanvasHeightProperty = BindableProperty.Create(nameof(WeekCanvasHeight), typeof(double), typeof(WeekCalendarView), 1440d);
    public double WeekDayColumnWidth { get => (double)GetValue(WeekDayColumnWidthProperty); set => SetValue(WeekDayColumnWidthProperty, value); }
    public static readonly BindableProperty WeekDayColumnWidthProperty = BindableProperty.Create(nameof(WeekDayColumnWidth), typeof(double), typeof(WeekCalendarView), 120d);
    public bool IsCurrentWeek { get => (bool)GetValue(IsCurrentWeekProperty); set => SetValue(IsCurrentWeekProperty, value); }
    public static readonly BindableProperty IsCurrentWeekProperty = BindableProperty.Create(nameof(IsCurrentWeek), typeof(bool), typeof(WeekCalendarView), false);
    public double CurrentTimeLineTop { get => (double)GetValue(CurrentTimeLineTopProperty); set => SetValue(CurrentTimeLineTopProperty, value); }
    public static readonly BindableProperty CurrentTimeLineTopProperty = BindableProperty.Create(nameof(CurrentTimeLineTop), typeof(double), typeof(WeekCalendarView), 0d);
    public DateTime WeekStartDate { get => (DateTime)GetValue(WeekStartDateProperty); set => SetValue(WeekStartDateProperty, value); }
    public static readonly BindableProperty WeekStartDateProperty = BindableProperty.Create(nameof(WeekStartDate), typeof(DateTime), typeof(WeekCalendarView), DateTime.Today);
    public int VisibleStartMinute { get => (int)GetValue(VisibleStartMinuteProperty); private set => SetValue(VisibleStartMinuteProperty, value); }
    public static readonly BindableProperty VisibleStartMinuteProperty = BindableProperty.Create(nameof(VisibleStartMinute), typeof(int), typeof(WeekCalendarView), 0);
    public int VisibleEndMinute { get => (int)GetValue(VisibleEndMinuteProperty); private set => SetValue(VisibleEndMinuteProperty, value); }
    public static readonly BindableProperty VisibleEndMinuteProperty = BindableProperty.Create(nameof(VisibleEndMinute), typeof(int), typeof(WeekCalendarView), 24 * 60);
    public int InteractionFrameThrottleMs { get; set; } = 20;

    public string? SelectedEventId { get => (string?)GetValue(SelectedEventIdProperty); set => SetValue(SelectedEventIdProperty, value); }
    public static readonly BindableProperty SelectedEventIdProperty = BindableProperty.Create(nameof(SelectedEventId), typeof(string), typeof(WeekCalendarView), null);


    private void BuildWeekColumns()
    {
        if (WeekDayColumns is not IEnumerable cols) return;
        var days = cols.OfType<WeekDayColumn>().Take(7).ToList();
        if (days.Count == 0) return;

        WeekHeaderGrid.ColumnDefinitions.Clear();
        WeekHeaderGrid.Children.Clear();
        WeekBodyGrid.ColumnDefinitions.Clear();
        WeekBodyGrid.Children.Clear();

        WeekDayColumnWidth = WeekBodyGrid.Width > 0 ? WeekBodyGrid.Width / 7d : WeekDayColumnWidth;

        for (var i = 0; i < days.Count; i++)
        {
            WeekHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            WeekBodyGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            var day = days[i];
            var header = new Border { StrokeThickness = 0, BackgroundColor = day.HeaderBackgroundColor, Content = new Label { Text = day.Header, FontSize = 11, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center, TextColor = day.HeaderTextColor } };
            Grid.SetColumn(header, i);
            WeekHeaderGrid.Children.Add(header);

            var lane = new AbsoluteLayout { BindingContext = day, HeightRequest = WeekCanvasHeight, BackgroundColor = day.DayBackgroundColor };
            var laneTap = new TapGestureRecognizer();
            laneTap.Tapped += OnEmptySlotTapped;
            lane.GestureRecognizers.Add(laneTap);

            var bg = new WeekGridBackgroundView();
            bg.SetBinding(WeekGridBackgroundView.IsTodayProperty, nameof(WeekDayColumn.IsToday));
            bg.SetBinding(WeekGridBackgroundView.IsCurrentWeekProperty, new Binding(nameof(IsCurrentWeek), source: this));
            bg.SetBinding(WeekGridBackgroundView.CurrentTimeLineTopProperty, new Binding(nameof(CurrentTimeLineTop), source: this));
            bg.SetBinding(HeightRequestProperty, new Binding(nameof(WeekCanvasHeight), source: this));
            AbsoluteLayout.SetLayoutBounds(bg, new Rect(0, 0, 1, 1));
            AbsoluteLayout.SetLayoutFlags(bg, AbsoluteLayoutFlags.All);
            lane.Children.Add(bg);

            var eventsLayer = new AbsoluteLayout();
            BindableLayout.SetItemsSource(eventsLayer, day.VisibleEventBlocks);
            BindableLayout.SetItemTemplate(eventsLayer, CreateWeekEventTemplate());
            AbsoluteLayout.SetLayoutBounds(eventsLayer, new Rect(0, 0, 1, 1));
            AbsoluteLayout.SetLayoutFlags(eventsLayer, AbsoluteLayoutFlags.All);
            lane.Children.Add(eventsLayer);

            var overlay = new WeekInteractionOverlayView { ZIndex = 999 };
            overlay.SetBinding(WeekInteractionOverlayView.PreviewProperty, new Binding(nameof(InteractionPreview), source: this));
            overlay.SetBinding(HeightRequestProperty, new Binding(nameof(WeekCanvasHeight), source: this));
            AbsoluteLayout.SetLayoutBounds(overlay, new Rect(0, 0, 1, 1));
            AbsoluteLayout.SetLayoutFlags(overlay, AbsoluteLayoutFlags.All);
            lane.Children.Add(overlay);

            Grid.SetColumn(lane, i);
            WeekBodyGrid.Children.Add(lane);
        }
    }




    private static DataTemplate CreateAllDayEventTemplate()
        => new(() =>
        {
            var border = new Border
            {
                Padding = new Thickness(6, 2),
                StrokeThickness = 1,
                Stroke = Color.FromArgb("#FFFFFF80"),
                StrokeShape = new RoundRectangle { CornerRadius = 6 }
            };
            border.SetBinding(BackgroundColorProperty, nameof(WeekAllDayEventBlock.BackgroundColor));
            border.SetBinding(AbsoluteLayout.LayoutBoundsProperty, nameof(WeekAllDayEventBlock.LayoutBounds));
            AbsoluteLayout.SetLayoutFlags(border, AbsoluteLayoutFlags.XProportional | AbsoluteLayoutFlags.WidthProportional);

            var tap = new TapGestureRecognizer();
            tap.Tapped += OnAllDayBlockTapped;
            border.GestureRecognizers.Add(tap);

            var label = new Label { FontSize = 10, TextColor = Colors.White, LineBreakMode = LineBreakMode.TailTruncation };
            label.SetBinding(Label.TextProperty, nameof(WeekAllDayEventBlock.Title));
            border.Content = label;

            return border;
        });
    private DataTemplate CreateWeekEventTemplate()
        => new(() =>
        {
            var border = new Border
            {
                Padding = new Thickness(6, 4),
                StrokeThickness = 1,
                Stroke = Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                MinimumHeightRequest = 44
            };
            border.SetBinding(BackgroundColorProperty, nameof(WeekEventBlock.BackgroundColor));
            border.SetBinding(AbsoluteLayout.LayoutBoundsProperty, nameof(WeekEventBlock.Bounds));
            AbsoluteLayout.SetLayoutFlags(border, AbsoluteLayoutFlags.XProportional | AbsoluteLayoutFlags.WidthProportional);

            var tap = new TapGestureRecognizer();
            tap.Tapped += OnEventBlockTapped;
            border.GestureRecognizers.Add(tap);
            var blockPan = new PanGestureRecognizer();
            blockPan.PanUpdated += OnEventBlockPanUpdated;
            border.GestureRecognizers.Add(blockPan);

            var grid = new Grid { RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Star), new RowDefinition(14) } };
            var title = new Label { FontSize = 10, TextColor = Colors.White, LineBreakMode = LineBreakMode.TailTruncation };
            title.SetBinding(Label.TextProperty, nameof(WeekEventBlock.Title));
            var time = new Label { FontSize = 9, TextColor = Colors.White };
            time.SetBinding(Label.TextProperty, nameof(WeekEventBlock.TimeLabel));
            grid.Add(new VerticalStackLayout { Spacing = 0, Children = { time, title } });

            var resizeHandle = new BoxView { Opacity = 0.001, BackgroundColor = Colors.Transparent };
            var resizePan = new PanGestureRecognizer();
            resizePan.PanUpdated += OnResizeHandlePanUpdated;
            resizeHandle.GestureRecognizers.Add(resizePan);
            Grid.SetRow(resizeHandle, 1);
            grid.Add(resizeHandle);

            border.Content = grid;
            return border;
        });

    private void OnEventBlockTapped(object? sender, TappedEventArgs e)
    {
        if (DateTime.UtcNow < _suppressTapUntilUtc) return;
        if (_interactionState is WeekInteractionState.DraggingMove or WeekInteractionState.DraggingResize) return;
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
        var decision = _gestureArbitrationService.Decide(true, true, _pressStartPoint, new Point(_pressStartPoint.X, previewHeight), TimeSpan.FromMilliseconds(350));
        if (decision == WeekGestureDecision.Cancel) { CancelCurrentInteraction(); return; }

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
        var decision = _gestureArbitrationService.Decide(true, false, _pressStartPoint, point, TimeSpan.FromMilliseconds(350));
        if (decision != WeekGestureDecision.Drag && decision != WeekGestureDecision.LongPress) return;

        TransitionTo(WeekInteractionState.DraggingMove, _activeBlock, point);
        var dt = _mapper.MapToDateTime(point, weekStartDate, WeekDayColumnWidth);
        var dy = _autoScrollService.ComputeVerticalDelta(point.Y, WeekScroll.Height);
        if (Math.Abs(dy) > 0.01)
            WeekScroll.ScrollToAsync(WeekScroll.ScrollX, Math.Max(0, WeekScroll.ScrollY + dy), false);
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

    public void CancelCurrentInteraction()
    {
        TransitionTo(WeekInteractionState.Canceled);
        ClearPreview();
        TransitionTo(WeekInteractionState.Idle);
        _activeBlock = null;
        _isResizing = false;
    }

    private void OnEventBlockPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (sender is not Border b || b.BindingContext is not WeekEventBlock block) { CancelCurrentInteraction(); return; }
        HandlePan(block, e, isResizeHandle: false);
    }

    private void OnResizeHandlePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (sender is not BoxView box || box.BindingContext is not WeekEventBlock block) { CancelCurrentInteraction(); return; }
        HandlePan(block, e, isResizeHandle: true);
    }

    private void HandlePan(WeekEventBlock block, PanUpdatedEventArgs e, bool isResizeHandle)
    {
        if (e.StatusType == GestureStatus.Running)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastInteractionFrameUtc).TotalMilliseconds < InteractionFrameThrottleMs) return;
            _lastInteractionFrameUtc = now;
        }

        var point = new Point(block.LeftRatio * WeekDayColumnWidth + e.TotalX, block.Top + e.TotalY);
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _activeBlock = block;
                _isResizing = isResizeHandle;
                TransitionTo(isResizeHandle ? WeekInteractionState.DraggingResize : WeekInteractionState.Pressed, block, point);
                break;
            case GestureStatus.Running:
                if (_activeBlock == null) { CancelCurrentInteraction(); return; }
                if (_isResizing || isResizeHandle)
                {
                    var endMinute = _mapper.MapToMinute(block.EndMinute + e.TotalY);
                    endMinute = Math.Max(block.StartMinute + 15, endMinute);
                    UpdatePreview(block.EventId, block.Date, block.StartMinute, endMinute);
                    TransitionTo(WeekInteractionState.DraggingResize, block, point);
                }
                else
                {
                    var dt = _mapper.MapToDateTime(new Point(block.LeftRatio * WeekDayColumnWidth + e.TotalX, block.Top + e.TotalY), WeekStartDate, WeekDayColumnWidth);
                    UpdatePreview(block.EventId, dt.Date, dt.Hour * 60 + dt.Minute, dt.Hour * 60 + dt.Minute + Math.Max(15, block.EndMinute - block.StartMinute));
                    TransitionTo(WeekInteractionState.DraggingMove, block, point);
                }
                _suppressTapUntilUtc = DateTime.UtcNow.AddMilliseconds(300);
                break;
            case GestureStatus.Completed:
                if (_activeBlock == null) { CancelCurrentInteraction(); return; }
                if (_isResizing || isResizeHandle)
                    CompleteResize(block.EventId, block.Date, block.StartMinute, block.Height + e.TotalY);
                else
                    CompleteDrag(block.EventId, new Point(block.LeftRatio * WeekDayColumnWidth + e.TotalX, block.Top + e.TotalY), WeekStartDate);
                _activeBlock = null;
                _isResizing = false;
                break;
            case GestureStatus.Canceled:
                CancelCurrentInteraction();
                break;
        }
    }

    private void OnWeekScrollScrolled(object? sender, ScrolledEventArgs e)
    {
        VisibleStartMinute = Math.Clamp((int)e.ScrollY, 0, 24 * 60);
        var viewportHeight = WeekScroll.Height <= 0 ? 600 : WeekScroll.Height;
        VisibleEndMinute = Math.Clamp((int)(e.ScrollY + viewportHeight), 0, 24 * 60);
        UpdateVirtualizedRanges();
    }

    private void UpdateVirtualizedRanges()
    {
        if (WeekDayColumns is IEnumerable cols)
        {
            foreach (var col in cols.OfType<WeekDayColumn>())
                col.UpdateVisibleRange(VisibleStartMinute, VisibleEndMinute);
        }

    }

    private void OnEmptySlotTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not AbsoluteLayout layout || layout.BindingContext is not WeekDayColumn dayColumn) return;
        var point = e.GetPosition(layout) ?? new Point(0,0);
        var startMinute = _mapper.MapToMinute(point.Y);
        EmptySlotTapped?.Invoke(this, new WeekEmptySlotTappedEventArgs { Date = dayColumn.Date, StartMinute = startMinute });
    }

    private void OnAllDayBlockTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border b && b.BindingContext is WeekAllDayEventBlock block)
        {
            SelectedEventId = block.EventId;
            EventBlockTapped?.Invoke(this, new WeekEventBlockTappedEventArgs
            {
                EventId = block.EventId,
                Date = block.StartDate,
                StartMinute = 0,
                OccurrenceKey = block.OccurrenceKey
            });
        }
    }
}
