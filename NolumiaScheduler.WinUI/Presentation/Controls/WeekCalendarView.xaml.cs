using System.Collections;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NolumiaScheduler.Presentation.Controls;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.Presentation.ViewModels;
using Windows.Foundation;

namespace NolumiaScheduler.WinUI.Presentation.Controls;

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
    const int BASE_COLUMN_SIZE = 120;
    private double _weekDayColumnWidth = BASE_COLUMN_SIZE;
    private bool _initialScrollDone;
    private enum ResizeEdge { None, Top, Bottom }
    private ResizeEdge _activeResizeEdge = ResizeEdge.None;
    private int _resizeOriginalStartMinute;
    private int _resizeOriginalEndMinute;
    private Canvas? _dragCreateLane;
    private WeekDayColumn? _dragCreateDay;
    private int _dragCreateStartMinute;
    private int _dragCreateCurrentMinute;

    // Win32 cursor control
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);
    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);
    private static readonly IntPtr CursorArrow  = LoadCursor(IntPtr.Zero, 32512); // IDC_ARROW
    private static readonly IntPtr CursorSizeNS  = LoadCursor(IntPtr.Zero, 32645); // IDC_SIZENS
    private static readonly IntPtr CursorSizeAll = LoadCursor(IntPtr.Zero, 32646); // IDC_SIZEALL

    private Border? _activeMoveChip;
    // Horizontal offset of the grab point from the chip's left edge, captured when a move
    // begins, so the landing day follows the cursor rather than the chip's left edge.
    private double _moveGrabOffsetX;
    private const double ChipMarginLeft = 4;

    public event EventHandler<WeekEmptySlotTappedEventArgs>? EmptySlotTapped;
    public event EventHandler<WeekEmptySlotTappedEventArgs>? AllDaySlotTapped;
    public event EventHandler<WeekEventBlockTappedEventArgs>? EventBlockTapped;
    public event EventHandler<WeekEventDragCompletedEventArgs>? EventDragCompleted;
    public event EventHandler<WeekEventResizeCompletedEventArgs>? EventResizeCompleted;
    public event EventHandler<WeekSlotDragCreatedEventArgs>? SlotDragCreated;

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
        RepositionEventChips();
        RebuildAllDayLanes();
    }

    // Reposition/resize the timed event chips horizontally to the current column width.
    // Vertical placement is minute-based and unaffected by width, so it is left untouched.
    private void RepositionEventChips()
    {
        foreach (var lane in WeekBodyGrid.Children.OfType<Canvas>())
            foreach (var border in lane.Children.OfType<Border>())
                if (border.Tag is WeekEventBlock block)
                    ApplyChipHorizontalBounds(border, block);
    }

    private void ApplyChipHorizontalBounds(Border border, WeekEventBlock block)
    {
        var left = (block.LayoutBounds.X > 0 ? block.LayoutBounds.X : block.LeftRatio * _weekDayColumnWidth) + ChipMarginLeft;
        Canvas.SetLeft(border, left);
        var rawWidth = block.LayoutBounds.Width > 0 ? block.LayoutBounds.Width : block.WidthRatio * _weekDayColumnWidth;
        border.Width = Math.Min(rawWidth - ChipMarginLeft, _weekDayColumnWidth * 0.8 - ChipMarginLeft);
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
                Preview = InteractionPreview,
                Width = _weekDayColumnWidth
            };
            Canvas.SetLeft(overlay, 0);
            Canvas.SetTop(overlay, 0);
            lane.Children.Add(overlay);
            // Keep the overlay (drag/resize ghost) the same width as the day column so its
            // ratio-based geometry resolves to the chip's actual pixel size.
            lane.SizeChanged += (_, e) => overlay.Width = e.NewSize.Width;

            // Tap on empty slot
            lane.Tapped += OnLaneTapped;
            lane.PointerPressed += OnLanePointerPressed;
            lane.PointerMoved += OnLanePointerMoved;
            lane.PointerReleased += OnLanePointerReleased;
            lane.PointerCanceled += OnLanePointerCanceled;

            Grid.SetColumn(lane, i);
            WeekBodyGrid.Children.Add(lane);
        }

        // Rebuild time slots
        BuildTimeSlots();

        // Compute all-day lane height from current blocks
        var allDayBlocks = (WeekAllDayEventBlocks as IEnumerable)?.OfType<WeekAllDayEventBlock>().ToList() ?? [];
        var maxRow = allDayBlocks.Count == 0 ? 0 : allDayBlocks.Max(b => b.Row);
        WeekAllDayGrid.Height = Math.Max(28d, (maxRow + 1) * 24d);
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

    private void RebuildAllDayLanes()
    {
        if (WeekDayColumns is not IEnumerable cols) return;
        var days = cols.OfType<WeekDayColumn>().Take(7).ToList();
        if (days.Count == 0 || WeekAllDayGrid.ColumnDefinitions.Count != days.Count) return;

        WeekAllDayGrid.Children.Clear();
        for (var i = 0; i < days.Count; i++)
        {
            var lane = BuildAllDayLane(days[i].Date);
            Grid.SetColumn(lane, i);
            WeekAllDayGrid.Children.Add(lane);
        }
    }

    private Canvas BuildAllDayLane(DateTime day)
    {
        var canvas = new Canvas { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Tag = day };
        canvas.Tapped += OnAllDayLaneTapped;
        var blocks = (WeekAllDayEventBlocks as IEnumerable)?
            .OfType<WeekAllDayEventBlock>()
            .Where(b => b.StartDate.Date <= day.Date && b.EndDate.Date >= day.Date)
            .ToList() ?? [];

        foreach (var block in blocks)
        {
            var chipColWidth = _weekDayColumnWidth > 0 ? _weekDayColumnWidth : BASE_COLUMN_SIZE;
            var chip = new Border
            {
                Background = new SolidColorBrush(block.BackgroundColor),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(6, 2, 6, 2),
                Height = 20,
                Opacity = 0.82,
                Tag = block,
                Width = chipColWidth - 4
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
            CornerRadius = new CornerRadius(2),
            Tag = block
        };

        ApplyChipHorizontalBounds(border, block);
        Canvas.SetTop(border, block.Top);
        var chipHeight = Math.Max(16, block.Height);
        border.Height = chipHeight;

        if (chipHeight > ResizeHandlePx * 2.5)
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ResizeHandlePx) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ResizeHandlePx) });

            var topStrip = BuildResizeStrip();
            Grid.SetRow(topStrip, 0);
            grid.Children.Add(topStrip);

            var title = new TextBlock
            {
                Text = block.Title,
                FontSize = 10,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                VerticalAlignment = VerticalAlignment.Top,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(3, 0, 3, 0)
            };
            Grid.SetRow(title, 1);
            grid.Children.Add(title);

            var bottomStrip = BuildResizeStrip();
            Grid.SetRow(bottomStrip, 2);
            grid.Children.Add(bottomStrip);

            border.Child = grid;
        }
        else
        {
            border.Padding = new Thickness(2, 0, 2, 0);
            border.Child = new TextBlock
            {
                Text = block.Title,
                FontSize = 10,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                VerticalAlignment = VerticalAlignment.Top,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        border.Tapped += OnEventBlockTapped;
        border.ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY;
        border.ManipulationStarted += OnEventBlockManipulationStarted;
        border.ManipulationDelta += OnEventBlockManipulationDelta;
        border.ManipulationCompleted += OnEventBlockManipulationCompleted;
        border.PointerMoved += OnChipPointerMoved;
        border.PointerExited += OnChipPointerExited;

        return border;
    }

    // Thin grip strip rendered at the top/bottom of an event chip to signal
    // that the edge can be dragged to change the time.
    private static Border BuildResizeStrip()
    {
        var strip = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(45, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        strip.Child = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Width = 18,
            Height = 2,
            RadiusX = 1,
            RadiusY = 1,
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(210, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        return strip;
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
        if (sender is not Canvas canvas || canvas.Tag is not WeekDayColumn dayColumn) return;
        // A press that starts on an existing event chip is a move/resize gesture handled
        // by the chip's manipulation events. Do not also start the empty-slot drag-create,
        // otherwise its preview overlay would render on top of the moving event.
        if (IsWithinEventChip(e.OriginalSource)) return;
        var pos = e.GetCurrentPoint(canvas).Position;
        _dragCreateLane = canvas;
        _dragCreateDay = dayColumn;
        _dragCreateStartMinute = _mapper.MapToMinute(pos.Y);
        _dragCreateCurrentMinute = _dragCreateStartMinute;
        _pressStartPoint = pos;
        canvas.CapturePointer(e.Pointer);
    }

    // Walks up the visual tree from the press source; true if the press landed inside an
    // event chip (a Border tagged with a WeekEventBlock) before reaching the lane Canvas.
    private static bool IsWithinEventChip(object? originalSource)
    {
        var node = originalSource as DependencyObject;
        while (node != null)
        {
            if (node is Border border && border.Tag is WeekEventBlock) return true;
            if (node is Canvas) return false;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    private void OnLanePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragCreateLane == null || _dragCreateDay == null) return;
        if (sender is not Canvas canvas) return;
        var pos = e.GetCurrentPoint(canvas).Position;

        // Only start drag-create after a small movement threshold
        if (_interactionState != WeekInteractionState.DraggingCreate)
        {
            if (Math.Abs(pos.Y - _pressStartPoint.Y) < 6) return;
            TransitionTo(WeekInteractionState.DraggingCreate);
        }

        _dragCreateCurrentMinute = _mapper.MapToMinute(pos.Y);
        var startMin = Math.Min(_dragCreateStartMinute, _dragCreateCurrentMinute);
        var endMin = Math.Max(_dragCreateStartMinute, _dragCreateCurrentMinute);
        endMin = Math.Max(endMin, startMin + 15);

        InteractionPreview = new WeekInteractionPreview
        {
            IsVisible = true,
            EventId = null,
            Date = _dragCreateDay.Date,
            StartMinute = startMin,
            EndMinute = endMin,
            LeftRatio = 0,
            WidthRatio = 1
        };
        UpdateOverlayPreview();
    }

    private void OnLanePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Canvas canvas)
            canvas.ReleasePointerCapture(e.Pointer);

        if (_interactionState == WeekInteractionState.DraggingCreate && _dragCreateDay != null)
        {
            var startMin = Math.Min(_dragCreateStartMinute, _dragCreateCurrentMinute);
            var endMin = Math.Max(_dragCreateStartMinute, _dragCreateCurrentMinute);
            endMin = Math.Max(endMin, startMin + 15);

            SlotDragCreated?.Invoke(this, new WeekSlotDragCreatedEventArgs
            {
                Date = _dragCreateDay.Date,
                StartMinute = startMin,
                EndMinute = endMin
            });

            _suppressTapUntilUtc = DateTime.UtcNow.AddMilliseconds(300);
        }

        ResetDragCreateState();
    }

    private void OnLanePointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Canvas canvas)
            canvas.ReleasePointerCapture(e.Pointer);
        ResetDragCreateState();
    }

    private void ResetDragCreateState()
    {
        _dragCreateLane = null;
        _dragCreateDay = null;
        TransitionTo(WeekInteractionState.Idle);
        ClearPreview();
        UpdateOverlayPreview();
    }

    private void UpdateOverlayPreview()
    {
        foreach (var canvas in WeekBodyGrid.Children.OfType<Canvas>())
        {
            foreach (var overlay in canvas.Children.OfType<WeekInteractionOverlayView>())
            {
                if (canvas == _dragCreateLane)
                    overlay.Preview = InteractionPreview;
                else
                    overlay.Preview = new WeekInteractionPreview { IsVisible = false };
            }
        }
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
        // Handled so the tap does not also bubble to the lane and start a new-event create.
        e.Handled = true;
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

    private void OnAllDayLaneTapped(object sender, TappedRoutedEventArgs e)
    {
        if (DateTime.UtcNow < _suppressTapUntilUtc) return;
        if (sender is not Canvas canvas || canvas.Tag is not DateTime day) return;
        AllDaySlotTapped?.Invoke(this, new WeekEmptySlotTappedEventArgs { Date = day, StartMinute = 0 });
    }

    private void UpdateLaneOverlay(DateTime date)
    {
        foreach (var canvas in WeekBodyGrid.Children.OfType<Canvas>())
        {
            var isTarget = canvas.Tag is WeekDayColumn d && d.Date.Date == date.Date;
            foreach (var overlay in canvas.Children.OfType<WeekInteractionOverlayView>())
                overlay.Preview = isTarget ? InteractionPreview : new WeekInteractionPreview { IsVisible = false };
        }
    }

    private const double ResizeHandlePx = 10;

    private void OnEventBlockManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
    {
        if (sender is not Border b || b.Tag is not WeekEventBlock block) return;
        var relY = e.Position.Y;
        var height = b.ActualHeight;

        if (relY <= ResizeHandlePx && height > ResizeHandlePx * 2.5)
        {
            _activeResizeEdge = ResizeEdge.Top;
        }
        else if (relY >= height - ResizeHandlePx && height > ResizeHandlePx * 2.5)
        {
            _activeResizeEdge = ResizeEdge.Bottom;
        }
        else
        {
            // The whole body (between the resize edges) drags the event.
            _activeResizeEdge = ResizeEdge.None;
        }

        _resizeOriginalStartMinute = block.StartMinute;
        _resizeOriginalEndMinute = block.EndMinute;
        _moveGrabOffsetX = e.Position.X;
    }

    private void OnEventBlockManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (sender is not Border b || b.Tag is not WeekEventBlock block) return;
        var now = DateTime.UtcNow;
        if ((now - _lastInteractionFrameUtc).TotalMilliseconds < InteractionFrameThrottleMs) return;
        _lastInteractionFrameUtc = now;

        _activeBlock = block;
        _suppressTapUntilUtc = now.AddMilliseconds(300);

        if (_activeResizeEdge != ResizeEdge.None)
        {
            var deltaMin = (int)Math.Round(e.Cumulative.Translation.Y);
            int previewStart, previewEnd;
            if (_activeResizeEdge == ResizeEdge.Top)
            {
                previewStart = _mapper.SnapMinute(Math.Clamp(_resizeOriginalStartMinute + deltaMin, 0, _resizeOriginalEndMinute - 15));
                previewEnd = _resizeOriginalEndMinute;
            }
            else
            {
                previewStart = _resizeOriginalStartMinute;
                previewEnd = _mapper.SnapMinute(Math.Clamp(_resizeOriginalEndMinute + deltaMin, _resizeOriginalStartMinute + 15, 24 * 60));
            }
            UpdatePreview(block.EventId, block.Date, previewStart, previewEnd);
            UpdateLaneOverlay(block.Date);
            TransitionTo(WeekInteractionState.DraggingResize, block);
        }
        else
        {
            // Move: the chip itself follows the cursor (semi-transparent) while a faint
            // ghost is painted on the day/time slot where the event will snap, so the
            // user can see the landing spot before releasing.
            if (_activeMoveChip == null)
            {
                _activeMoveChip = b;
                b.Opacity = 0.5;
                b.RenderTransform = new TranslateTransform();
                Canvas.SetZIndex(b, 100);
            }
            if (b.RenderTransform is TranslateTransform tt)
            {
                tt.X = e.Cumulative.Translation.X;
                tt.Y = e.Cumulative.Translation.Y;
            }
            SetCursor(CursorSizeAll);

            var newPoint = new Point(
                CursorAbsoluteX(block, e.Cumulative.Translation.X),
                block.Top + e.Cumulative.Translation.Y);
            TransitionTo(WeekInteractionState.DraggingMove, block, newPoint);

            var target = _mapper.MapToDateTime(newPoint, WeekStartDate, _weekDayColumnWidth);
            var targetStart = target.Hour * 60 + target.Minute;
            var duration = Math.Max(15, block.EndMinute - block.StartMinute);
            var targetEnd = Math.Min(targetStart + duration, 24 * 60);
            UpdatePreview(block.EventId, target.Date, targetStart, targetEnd);
            UpdateLaneOverlay(target.Date);
        }
    }

    // Cursor X relative to the whole week grid origin (Sunday column), so MapToDate
    // resolves the day under the pointer (not the chip's left edge). Built from the chip's
    // original absolute left, the grab offset within the chip, and the drag translation.
    private double CursorAbsoluteX(WeekEventBlock block, double translationX)
    {
        var dayIndex = Math.Clamp((int)Math.Round((block.Date.Date - WeekStartDate.Date).TotalDays), 0, 6);
        var chipLeft = (dayIndex + block.LeftRatio) * _weekDayColumnWidth + ChipMarginLeft;
        return chipLeft + _moveGrabOffsetX + translationX;
    }

    private void OnEventBlockManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        if (sender is not Border b || b.Tag is not WeekEventBlock block)
        {
            CancelCurrentInteraction();
            return;
        }

        var totalMovement = Math.Sqrt(
            e.Cumulative.Translation.X * e.Cumulative.Translation.X +
            e.Cumulative.Translation.Y * e.Cumulative.Translation.Y);

        if (_activeResizeEdge != ResizeEdge.None && totalMovement >= 4)
        {
            var deltaMin = (int)Math.Round(e.Cumulative.Translation.Y);
            int startMin, endMin;
            if (_activeResizeEdge == ResizeEdge.Top)
            {
                startMin = _mapper.SnapMinute(Math.Clamp(_resizeOriginalStartMinute + deltaMin, 0, _resizeOriginalEndMinute - 15));
                endMin = _resizeOriginalEndMinute;
            }
            else
            {
                startMin = _resizeOriginalStartMinute;
                endMin = _mapper.SnapMinute(Math.Clamp(_resizeOriginalEndMinute + deltaMin, _resizeOriginalStartMinute + 15, 24 * 60));
            }
            EventResizeCompleted?.Invoke(this, new WeekEventResizeCompletedEventArgs
            {
                EventId = block.EventId,
                OccurrenceKey = block.MoveKey,
                Date = block.Date,
                StartMinute = startMin,
                EndMinute = endMin
            });
        }
        else if (_activeResizeEdge == ResizeEdge.None && _activeBlock != null && totalMovement >= 8)
        {
            var finalPoint = new Point(
                CursorAbsoluteX(block, e.Cumulative.Translation.X),
                block.Top + e.Cumulative.Translation.Y);
            RestoreMoveChipOpacity();
            var dt = _mapper.MapToDateTime(finalPoint, WeekStartDate, _weekDayColumnWidth);
            var startMinute = dt.Hour * 60 + dt.Minute;
            EventDragCompleted?.Invoke(this, new WeekEventDragCompletedEventArgs
            {
                EventId = block.EventId,
                OccurrenceKey = block.MoveKey,
                TargetDateTime = dt.Date,
                TargetStartMinute = startMinute,
                DurationMinutes = block.EndMinute - block.StartMinute
            });
            _suppressTapUntilUtc = DateTime.UtcNow.AddMilliseconds(300);
        }
        else
        {
            RestoreMoveChipOpacity();
            _selectedEventId = block.EventId;
            UpdateSelectionState();
            _suppressTapUntilUtc = DateTime.UtcNow.AddMilliseconds(300);
            EventBlockTapped?.Invoke(this, new WeekEventBlockTappedEventArgs
            {
                EventId = block.EventId,
                Date = block.Date,
                StartMinute = block.StartMinute,
                OccurrenceKey = block.OccurrenceKey
            });
        }

        _activeResizeEdge = ResizeEdge.None;
        TransitionTo(WeekInteractionState.Idle);
        ClearPreview();
        UpdateLaneOverlay(block.Date);
        _activeBlock = null;
    }

    private void RestoreMoveChipOpacity()
    {
        if (_activeMoveChip != null)
        {
            _activeMoveChip.Opacity = 1.0;
            _activeMoveChip.RenderTransform = null;
            Canvas.SetZIndex(_activeMoveChip, 0);
            _activeMoveChip = null;
        }
    }

    private void OnChipPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border b) return;
        var pos = e.GetCurrentPoint(b).Position;
        var height = b.ActualHeight;

        // Top/bottom edges resize (up-down arrows); the rest of the body moves
        // the event (4-way move cursor).
        if (height > ResizeHandlePx * 2.5 &&
            (pos.Y < ResizeHandlePx || pos.Y >= height - ResizeHandlePx))
        {
            SetCursor(CursorSizeNS);
            return;
        }
        SetCursor(CursorSizeAll);
    }

    private void OnChipPointerExited(object sender, PointerRoutedEventArgs e)
    {
        SetCursor(CursorArrow);
    }

    public void CancelCurrentInteraction()
    {
        RestoreMoveChipOpacity();
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
