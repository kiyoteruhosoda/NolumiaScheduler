using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.Presentation.ViewModels;
using NolumiaScheduler.Resources.Strings;
using MauiApp = Microsoft.Maui.Controls.Application;

namespace NolumiaScheduler.Presentation.Pages;

public partial class CalendarPage : ContentPage
{
    private readonly CalendarViewModel _vm;
    private readonly ICalendarEventRepository _eventRepo;
    private readonly IWeekInteractionCompletionService _interactionCompletionService;
    private readonly IServiceProvider _services;

    private Color _rowHoverColor = Color.FromArgb("#e8eaed");
    private Color _iconHoverColor = Color.FromArgb("#e0e0e0");
    private Color _outlineHoverColor = Color.FromArgb("#e8f0fe");

    public CalendarPage(
        CalendarViewModel vm,
        ICalendarEventRepository eventRepo,
        IWeekInteractionCompletionService interactionCompletionService,
        IServiceProvider services)
    {
        InitializeComponent();
        _vm = vm;
        _eventRepo = eventRepo;
        _interactionCompletionService = interactionCompletionService;
        _services = services;
        BindingContext = vm;
        SizeChanged += OnPageSizeChanged;
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        var isDark = MauiApp.Current?.RequestedTheme == AppTheme.Dark;
        if (isDark)
        {
            _rowHoverColor  = Color.FromArgb("#3c3c3c");
            _iconHoverColor = Color.FromArgb("#4a4a4a");
            _outlineHoverColor = Color.FromArgb("#1e3a5f");
        }
        else
        {
            _rowHoverColor  = Color.FromArgb("#f1f3f4");
            _iconHoverColor = Color.FromArgb("#e0e0e0");
            _outlineHoverColor = Color.FromArgb("#e8f0fe");
        }
    }

    // ── Header button hover ───────────────────────────────────

    private void OnHeaderButtonEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border b) b.BackgroundColor = _iconHoverColor;
    }

    private void OnHeaderButtonExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border b) b.BackgroundColor = Colors.Transparent;
    }

    private void OnOutlineButtonEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border b) b.BackgroundColor = _outlineHoverColor;
    }

    private void OnOutlineButtonExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border b) b.BackgroundColor = Colors.Transparent;
    }

    // ── Event row hover ───────────────────────────────────────

    private void OnEventItemPointerEntered(object? sender, PointerEventArgs e)
    {
        // sender is Grid; walk up to find the parent Border
        if (sender is Grid grid && grid.Parent is Border border)
            border.BackgroundColor = _rowHoverColor;
    }

    private void OnEventItemPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Grid grid && grid.Parent is Border border)
            border.BackgroundColor = Colors.Transparent;
    }

    // ── Icon button hover ─────────────────────────────────────

    private void OnIconBorderEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border b) b.BackgroundColor = _iconHoverColor;
    }

    private void OnIconBorderExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border b) b.BackgroundColor = Colors.Transparent;
    }

    // ── Calendar grid ─────────────────────────────────────────

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (Height <= 0) return;
        // Fixed heights: header(48) + mode bar(40) + sep(1) + dow labels(36) + sep(1) = 126
        const double fixedHeight = 126;
        var available = Height - fixedHeight;
        if (available <= 0) return;
        _vm.DayCellHeight = Math.Max(60, available / 6.0);
    }

    private void OnDayCellSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.Count > 0 && e.CurrentSelection[0] is CalendarDayCell cell)
        {
            _vm.SelectDay(cell);
            ((CollectionView)sender!).SelectedItem = null;
        }
    }

    // ── New event ─────────────────────────────────────────────

    private async void OnNewEventClicked(object? sender, EventArgs e)
    {
        await OpenEventEditModalAsync(startDate: DateOnly.FromDateTime(DateTime.Today));
    }

    // ── Edit event ────────────────────────────────────────────

    private async void OnEditEventClicked(object? sender, TappedEventArgs e)
    {
        if (sender is Border b && b.BindingContext is CalendarEventItem item)
            await OpenEventEditModalAsync(eventId: item.EventId);
    }

    private async void OnWeekEventBlockTapped(object? sender, Controls.WeekEventBlockTappedEventArgs e)
    {
        await OpenEventEditModalAsync(eventId: e.EventId, occurrenceDate: e.Date, occurrenceStartMinute: e.StartMinute);
    }

    private async void OnWeekEmptySlotTapped(object? sender, Controls.WeekEmptySlotTappedEventArgs e)
    {
        await OpenEventEditModalAsync(startDate: e.Date, startMinute: e.StartMinute);
    }

    private async Task OpenEventEditModalAsync(
        string? eventId = null,
        DateOnly? startDate = null,
        int? startMinute = null,
        DateOnly? occurrenceDate = null,
        int? occurrenceStartMinute = null)
    {
        var page = _services.GetRequiredService<EventEditPage>();

        // Set query properties in an order that avoids premature loads:
        // occurrence params first, then eventId/startDate to trigger the load.
        if (occurrenceDate.HasValue)
            page.OccurrenceDate = occurrenceDate.Value.ToString("yyyy-MM-dd");
        if (occurrenceStartMinute.HasValue)
            page.OccurrenceStartMinute = occurrenceStartMinute.Value.ToString();
        if (startMinute.HasValue)
            page.StartMinute = startMinute.Value.ToString();

        if (eventId != null)
            page.EventId = eventId;
        else if (startDate.HasValue)
            page.StartDate = startDate.Value.ToString("yyyy-MM-dd");

        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnWeekEventDragCompleted(object? sender, Controls.WeekEventDragCompletedEventArgs e)
    {
        await _interactionCompletionService.HandleDragCompletedAsync(e);
    }

    private async void OnWeekEventResizeCompleted(object? sender, Controls.WeekEventResizeCompletedEventArgs e)
    {
        await _interactionCompletionService.HandleResizeCompletedAsync(e);
    }

    // ── Delete event ──────────────────────────────────────────

    private async void OnDeleteEventClicked(object? sender, TappedEventArgs e)
    {
        if (sender is not Border b || b.BindingContext is not CalendarEventItem item)
            return;

        var ev = _eventRepo.FindById(new EventId(item.EventId));
        if (ev == null) return;

        if (ev.IsRecurring())
        {
            var action = await DisplayActionSheetAsync(
                AppResources.DeleteEventTitle,
                AppResources.CancelButton,
                null,
                AppResources.DeleteOccurrence,
                AppResources.DeleteAllOccurrences);

            if (action == AppResources.DeleteOccurrence)
                _vm.DeleteOccurrence(item.EventId, item.OccurrenceKey);
            else if (action == AppResources.DeleteAllOccurrences)
                _vm.DeleteEntireEvent(item.EventId);
        }
        else
        {
            var confirmed = await DisplayAlertAsync(
                AppResources.DeleteEventTitle,
                item.Title,
                AppResources.DeleteButton,
                AppResources.CancelButton);

            if (confirmed)
                _vm.DeleteEntireEvent(item.EventId);
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.ReloadCurrentMonth();
    }
}
