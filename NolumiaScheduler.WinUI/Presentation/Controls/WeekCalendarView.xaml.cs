using System.Collections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.Presentation.ViewModels;
using Windows.Foundation;

namespace NolumiaScheduler.Presentation.Controls;

public sealed partial class WeekCalendarView : UserControl
{
    private readonly IWeekInteractionMapper _mapper;
    private readonly IWeekGestureArbitrationService _gestureArbitrationService;
    private readonly IWeekAutoScrollService _autoScrollService;
    private WeekInteractionState _interactionState = WeekInteractionState.Idle;
    private WeekEventBlock? _activeBlock;
    private Point _pressStartPoint;
    private Point _lastPoint;
    #pragma warning disable CS0414 // Reserved for future resize support
        private bool _isResizing;
    #pragma warning restore CS0414
    private DateTime _suppressTapUntilUtc;
    private DateTime _lastInteractionFrameUtc = DateTime.MinValue;
    private double _weekDayColumnWidth = 120;
    private bool _initialScrollDone;

    public event EventHandler<WeekEmptySlotTappedEventArgs>? EmptySlotTapped;
    public event EventHandler<WeekEventBlockTappedEventArgs>? EventBlockTapped;
    public event EventHandler<WeekEventDragCompletedEventArgs>? EventDragCompleted;
    public event EventHandler<WeekEventResizeCompletedEventArgs>? EventResizeCompleted;

    // DependencyProperties
    public static readonly DependencyProperty WeekTimeSlotsProperty =
        DependencyProperty.Register(nameof(WeekTimeSlots), typeof(IEnumerable), typeof(WeekCalendarView),
            new PropertyMetadata(null, (d, _) => ((WeekCalendarView)d).BuildTimeSlots()));

    public static readonly DependencyProperty WeekDayColumnsProperty =
        DependencyProperty.Register(nameof(WeekDayColumns), typeof(IEnumerable), typeof(WeekCalendarView),
            new PropertyMetadata(null, (d, e) =>
            {
                var view = (WeekCalendarView)d;
                if (e.OldValue is System.Collections.Specialized.INotifyCollectionChanged oldCol)
                    oldCol.CollectionChanged -= view.OnWeekDayColumnsChanged;
                if (e.NewValue is System.Collections.Specialized.INotifyCollectionChanged newCol)
                    newCol.CollectionChanged += view.OnWeekDayColumnsChanged;
                view.BuildWeekColumns();
            }));

    public static readonly DependencyProperty WeekAllDayEventBlocksProperty =
        DependencyProperty.Register(nameof(WeekAllDayEventBlocks), typeof(IEnumerable), typeof(WeekCalendarView),
            new PropertyMetadata(null, (d, _) => ((WeekCalendarView)d).BuildWeekColumns()));

    public static readonly DependencyProperty WeekAllDayLaneHeightProperty =
        DependencyProperty.Register(nameof(WeekAllDayLaneHeight), typeof(double), typeof(WeekCalendarView),
            new PropertyMetadata(28d, (d, e) => ((WeekCalendarView)d).WeekAllDayGrid.MinHeight = (double)e.NewValue));

    public static readonly DependencyProperty WeekCanvasHeightProperty =
        DependencyProperty.Register(nameof(WeekCanvasHeight), typeof(double), typeof(WeekCalendarView),
            new PropertyMetadata(1440d));

    public static readonly DependencyProperty IsCurrentWeekProperty =
        DependencyProperty.Register(nameof(IsCurrentWeek), typeof(bool), typeof(WeekCalendarView),
            new PropertyMetadata(false, (d, e) =>
            {
                var view = (WeekCalendarView)d;
                view._initialScrollDone = false;
                view.UpdateBackgroundViews();
                _ = view.ScrollToCurrentTimeAsync();
            }));

    public static readonly DependencyProperty CurrentTimeLineTopProperty =
        DependencyProperty.Register(nameof(CurrentTimeLineTop), typeof(double), typeof(WeekCalendarView),
            new PropertyMetadata(0d, (d, _) => ((WeekCalendarView)d).UpdateBackgroundViews()));

    public static readonly DependencyProperty WeekStartDateProperty =
        DependencyProperty.Register(nameof(WeekStartDate), typeof(DateTime), typeof(WeekCalendarView),
            new PropertyMetadata(DateTime.Today));

    public IEnumerable? WeekTimeSlots { get => (IEnumerable?)GetValue(WeekTimeSlotsProperty); set => SetValue(WeekTimeSlotsProperty, value); }
    public IEnumerable? WeekDayColumns { get => (IEnumerable?)GetValue(WeekDayColumnsProperty); set => SetValue(WeekDayColumnsProperty, value); }
    public IEnumerable? WeekAllDayEventBlocks { get => (IEnumerable?)GetValue(WeekAllDayEventBlocksProperty); set => SetValue(WeekAllDayEventBlocksProperty, value); }
    public double WeekAllDayLaneHeight { get => (double)GetValue(WeekAllDayLaneHeightProperty); set => SetValue(WeekAllDayLaneHeightProperty, value); }
    public double WeekCanvasHeight { get => (double)GetValue(WeekCanvasHeightProperty); set => SetValue(WeekCanvasHeightProperty, value); }
    public bool IsCurrentWeek { get => (bool)GetValue(IsCurrentWeekProperty); set => SetValue(IsCurrentWeekProperty, value); }
    public double CurrentTimeLineTop { get => (double)GetValue(CurrentTimeLineTopProperty); set => SetValue(CurrentTimeLineTopProperty, value); }
    public DateTime WeekStartDate { get => (DateTime)GetValue(WeekStartDateProperty); set => SetValue(WeekStartDateProperty, value); }
    public int InteractionFrameThrottleMs { get; set; } = 20;

    private string? _selectedEventId;

    public WeekCalendarView()
    {
        InitializeComponent();

        var services = NolumiaScheduler.WinUI.App.Services;
        _mapper = services?.GetService<IWeekInteractionMapper>() ?? new WeekInteractionMapper();
        _gestureArbitrationService = services?.GetService<IWeekGestureArbitrationService>() ?? new WeekGestureArbitrationService();
        _autoScrollService = services?.GetService<IWeekAutoScrollService>() ?? new WeekAutoScrollService();

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ScrollToCurrentTimeAsync();
    }

    public async Task ScrollToCurrentTimeAsync()
    {
        if (_initialScrollDone) return;
        const double nineAmPx = 9 * 60;
        var anchor = CurrentTimeLineTop > 0 && IsCurrentWeek ? CurrentTimeLineTop - 240 : nineAmPx;
        var y = Math.Max(0, anchor);

        // Wait until the ScrollViewer has been laid out and has a valid extent
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await Task.Delay(50);
            if (WeekScroll.ExtentHeight > 0) break;
        }

        WeekScroll.ChangeView(null, y, null, disableAnimation: true);
        _initialScrollDone = true;
    }

    public void RequestScroll()
    {
        _initialScrollDone = false;
        _ = ScrollToCurrentTimeAsync();
    }

    private void UpdateBackgroundViews()
    {
        foreach (var canvas in WeekBodyGrid.Children.OfType<Canvas>())
        {
            foreach (var bg in canvas.Children.OfType<WeekGridBackgroundView>())
            {
                bg.IsCurrentWeek = IsCurrentWeek;
                bg.CurrentTimeLineTop = CurrentTimeLineTop;
            }
        }
    }

    private void OnWeekBodyGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (WeekBodyGrid.ActualWidth <= 0) return;
        if (WeekDayColumns is not IEnumerable cols) return;
        var count = cols.OfType<WeekDayColumn>().Take(7).Count();
        if (count == 0) return;
        _weekDayColumnWidth = (WeekBodyGrid.ActualWidth - 1.0 * (count - 1)) / count;
        UpdateEventBlockLayoutBounds();
    }

    private void OnWeekDayColumnsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        BuildWeekColumns();
    }

    private void OnWeekScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        var scrollY = WeekScroll.VerticalOffset;
        var viewportHeight = WeekScroll.ViewportHeight > 0 ? WeekScroll.ViewportHeight : 600;
        var visStart = Math.Clamp((int)scrollY, 0, 24 * 60);
        var visEnd = Math.Clamp((int)(scrollY + viewportHeight), 0, 24 * 60);
        if (WeekDayColumns is IEnumerable cols)
        {
            foreach (var col in cols.OfType<WeekDayColumn>())
                col.UpdateVisibleRange(visStart, visEnd);
        }
    }

    private void BuildTimeSlots()
    {
        TimeSlotList.Items?.Clear();
        if (WeekTimeSlots == null) return;
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        foreach (var slot in WeekTimeSlots.OfType<WeekTimeSlot>())
        {
            panel.Children.Add(new TextBlock
            {
                Text = slot.HourLabel,
                FontSize = 10,
                Height = 60,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(2, 0, 0, 0)
            });
        }
        TimeSlotList.Items?.Clear();
        foreach (var child in panel.Children.ToList())
        {
            panel.Children.Remove(child);
            TimeSlotList.Items?.Add(child);
        }
    }

    private void BuildWeekColumns()
    {
        if (WeekDayColumns is not IEnumerable cols) return;
        var days = cols.OfType<WeekDayColumn>().Take(7).ToList();
        if (days.Count == 0) return;

        WeekHeaderGrid.ColumnDefinitions.Clear();
        WeekHeaderGrid.Children.Clear();
        WeekAllDayGrid.ColumnDefinitions.Clear();
        WeekAllDayGrid.Children.Clear();
        WeekBodyGrid.ColumnDefinitions.Clear();
        WeekBodyGrid.Children.Clear();

        if (WeekBodyGrid.ActualWidth > 0)
            _weekDayColumnWidth = (WeekBodyGrid.ActualWidth - 1.0 * (days.Count - 1)) / days.Count;
        UpdateEventBlockLayoutBounds();

        for (var i = 0; i < days.Count; i++)
        {
            WeekHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            WeekAllDayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            WeekBodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var day = days[i];

            // Header
            var headerBorder = new Border
            {
                Background = new SolidColorBrush(WeekDayColumn.HeaderBackgroundColor),
                Padding = new Thickness(0, 2, 0, 6)
            };
            var headerText = new TextBlock
            {
                Text = day.Header,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(day.HeaderTextColor),
                FontWeight = day.IsToday ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal
            };
            headerBorder.Child = headerText;
            Grid.SetColumn(headerBorder, i);
            WeekHeaderGrid.Children.Add(headerBorder);

            // All-day lane
            var allDayCanvas = BuildAllDayLane(day.Date);
            Grid.SetColumn(allDayCanvas, i);
            WeekAllDayGrid.Children.Add(allDayCanvas);

            // Day body lane
            var lane = new Canvas
            {
                Height = WeekCanvasHeight,
                Background = new SolidColorBrush(day.DayBackgroundColor),
                Tag = day
            };

            // Background grid
            var bg = new WeekGridBackgroundView { Height = WeekCanvasHeight };
            bg.SetBinding(WeekGridBackgroundView.IsTodayProperty, new Microsoft.UI.Xaml.Data.Binding
            {
                Source = day,
                Path = new PropertyPath(nameof(WeekDayColumn.IsToday))
            });
            bg.IsCurrentWeek = IsCurrentWeek;
            bg.CurrentTimeLineTop = CurrentTimeLineTop;
            Canvas.SetLeft(bg, 0);
            Canvas.SetTop(bg, 0);
            bg.Width = double.NaN;
            lane.Children.Add(bg);
            lane.SizeChanged += (_, e) => { bg.Width = e.NewSize.Width; };

            // Event blocks (will be populated via day.VisibleEventBlocks)
            foreach (var block in day.VisibleEventBlocks)
            {
                var chip = CreateEventBlockChip(block);
                lane.Children.Add(chip);
            }

            // Listen for changes
            day.VisibleEventBlocks.CollectionChanged += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(() => RefreshLaneEvents(lane, day));
            };

            // Overlay
            var overlay = new WeekInteractionOverlayView
            {
                Height = WeekCanvasHeight,
                IsHitTestVisible = false,
                Preview = InteractionPreview
            };
            Canvas.SetLeft(overlay, 0);
            Canvas.SetTop(overlay, 0);
            lane.Children.Add(overlay);

            // Tap on empty slot
            lane.Tapped += OnLaneTapped;
            lane.PointerPressed += OnLanePointerPressed;

            Grid.SetColumn(lane, i);
            WeekBodyGrid.Children.Add(lane);
        }

        // Rebuild time slots
        BuildTimeSlots();
    }

    private void RefreshLaneEvents(Canvas lane, WeekDayColumn day)
    {
        // Remove existing event chips (keep bg and overlay)
        var toRemove = lane.Children.OfType<Border>().ToList();
        foreach (var c in toRemove) lane.Children.Remove(c);

        foreach (var block in day.VisibleEventBlocks)
        {
            var chip = CreateEventBlockChip(block);
            lane.Children.Add(chip);
        }
    }

    private Canvas BuildAllDayLane(DateTime day)
    {
        var canvas = new Canvas { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        var blocks = (WeekAllDayEventBlocks as IEnumerable)?
            .OfType<WeekAllDayEventBlock>()
            .Where(b => b.StartDate.Date <= day.Date && b.EndDate.Date >= day.Date)
            .ToList() ?? [];

        foreach (var block in blocks)
        {
            var chip = new Border
            {
                Background = new SolidColorBrush(block.BackgroundColor),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Height = 20,
                Opacity = 0.82,
                Tag = block
            };
            var label = new TextBlock
            {
                Text = block.Title,
                FontSize = 10,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
            };
            chip.Child = label;
            chip.Tapped += OnAllDayBlockTapped;
            Canvas.SetLeft(chip, 0);
            Canvas.SetTop(chip, block.Top + 1);
            canvas.Children.Add(chip);
        }

        return canvas;
    }

    private Border CreateEventBlockChip(WeekEventBlock block)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(block.BackgroundColor),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(2, 0, 2, 0),
            Tag = block
        };

        Canvas.SetLeft(border, block.LayoutBounds.X > 0 ? block.LayoutBounds.X : block.LeftRatio * _weekDayColumnWidth);
        Canvas.SetTop(border, block.Top);
        border.Width = block.LayoutBounds.Width > 0 ? block.LayoutBounds.Width : block.WidthRatio * _weekDayColumnWidth;
        border.Height = Math.Max(16, block.Height);

        var title = new TextBlock
        {
            Text = block.Title,
            FontSize = 10,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            VerticalAlignment = VerticalAlignment.Top,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        border.Child = title;

        border.Tapped += OnEventBlockTapped;
        border.ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY;
        border.ManipulationDelta += OnEventBlockManipulationDelta;
        border.ManipulationCompleted += OnEventBlockManipulationCompleted;

        return border;
    }

    private WeekInteractionPreview _interactionPreview = new() { IsVisible = false };
    public WeekInteractionPreview InteractionPreview
    {
        get => _interactionPreview;
        private set => _interactionPreview = value;
    }

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

    private void UpdateSelectionState()
    {
        if (WeekDayColumns is not IEnumerable cols) return;
        foreach (var day in cols.OfType<WeekDayColumn>())
            foreach (var b in day.EventBlocks)
                b.IsSelected = b.EventId == _selectedEventId;
    }

    private void UpdateEventBlockLayoutBounds()
    {
        if (WeekDayColumns is not IEnumerable cols) return;
        foreach (var block in cols.OfType<WeekDayColumn>().SelectMany(d => d.EventBlocks))
            block.UpdateLayoutBounds(_weekDayColumnWidth);
    }

    private void OnLaneTapped(object sender, TappedRoutedEventArgs e)
    {
        if (DateTime.UtcNow < _suppressTapUntilUtc) return;
        if (sender is not Canvas canvas || canvas.Tag is not WeekDayColumn dayColumn) return;
        var pos = e.GetPosition(canvas);
        var startMinute = _mapper.MapToMinute(pos.Y);
        EmptySlotTapped?.Invoke(this, new WeekEmptySlotTappedEventArgs { Date = dayColumn.Date, StartMinute = startMinute });
    }

    private void OnLanePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Used for future drag support; empty for now
    }

    private void OnEventBlockTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (DateTime.UtcNow < _suppressTapUntilUtc) return;
        if (sender is not Border b || b.Tag is not WeekEventBlock block) return;

        _selectedEventId = block.EventId;
        UpdateSelectionState();
        EventBlockTapped?.Invoke(this, new WeekEventBlockTappedEventArgs
        {
            EventId = block.EventId,
            Date = block.Date,
            StartMinute = block.StartMinute,
            OccurrenceKey = block.OccurrenceKey
        });
    }

    private void OnAllDayBlockTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border b && b.Tag is WeekAllDayEventBlock block)
        {
            _selectedEventId = block.EventId;
            EventBlockTapped?.Invoke(this, new WeekEventBlockTappedEventArgs
            {
                EventId = block.EventId,
                Date = block.StartDate,
                StartMinute = 0,
                OccurrenceKey = block.OccurrenceKey
            });
        }
    }

    private void OnEventBlockManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (sender is not Border b || b.Tag is not WeekEventBlock block) return;
        var now = DateTime.UtcNow;
        if ((now - _lastInteractionFrameUtc).TotalMilliseconds < InteractionFrameThrottleMs) return;
        _lastInteractionFrameUtc = now;

        _activeBlock = block;
        var newPoint = new Point(
            block.LeftRatio * _weekDayColumnWidth + e.Cumulative.Translation.X,
            block.Top + e.Cumulative.Translation.Y);
        var dt = _mapper.MapToDateTime(newPoint, WeekStartDate, _weekDayColumnWidth);
        UpdatePreview(block.EventId, dt.Date, dt.Hour * 60 + dt.Minute,
            dt.Hour * 60 + dt.Minute + Math.Max(15, block.EndMinute - block.StartMinute));
        TransitionTo(WeekInteractionState.DraggingMove, block, newPoint);
        _suppressTapUntilUtc = now.AddMilliseconds(300);
    }

    private void OnEventBlockManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        if (sender is not Border b || b.Tag is not WeekEventBlock block || _activeBlock == null)
        {
            CancelCurrentInteraction();
            return;
        }

        var finalPoint = new Point(
            block.LeftRatio * _weekDayColumnWidth + e.Cumulative.Translation.X,
            block.Top + e.Cumulative.Translation.Y);

        var decision = _gestureArbitrationService.Decide(
            true, false, _pressStartPoint, finalPoint, TimeSpan.FromMilliseconds(350));

        if (decision == WeekGestureDecision.Drag || decision == WeekGestureDecision.LongPress)
        {
            var dt = _mapper.MapToDateTime(finalPoint, WeekStartDate, _weekDayColumnWidth);
            EventDragCompleted?.Invoke(this, new WeekEventDragCompletedEventArgs
            {
                EventId = block.EventId,
                TargetDateTime = dt,
                TargetStartMinute = dt.Hour * 60 + dt.Minute
            });
        }

        TransitionTo(WeekInteractionState.Idle);
        ClearPreview();
        _activeBlock = null;
    }

    public void CancelCurrentInteraction()
    {
        TransitionTo(WeekInteractionState.Canceled);
        ClearPreview();
        TransitionTo(WeekInteractionState.Idle);
        _activeBlock = null;
        _isResizing = false;
    }
}

// Extension to convert Vector2 to TimeSpan (approximate gesture duration)
internal static class Vector2Extensions
{
    public static TimeSpan ToTimeSpan(this System.Numerics.Vector2 _)
        => TimeSpan.FromMilliseconds(350);
}
