using Microsoft.Maui.Controls;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.ViewModels;
using NolumiaScheduler.Resources.Strings;
using MauiApp = Microsoft.Maui.Controls.Application;

namespace NolumiaScheduler.Presentation.Pages;

public partial class CalendarPage : ContentPage
{
    private readonly CalendarViewModel _vm;
    private readonly ICalendarEventRepository _eventRepo;

    private Color _rowHoverColor = Color.FromArgb("#e8eaed");
    private Color _iconHoverColor = Color.FromArgb("#e0e0e0");
    private Color _outlineHoverColor = Color.FromArgb("#e8f0fe");

    public CalendarPage(CalendarViewModel vm, ICalendarEventRepository eventRepo)
    {
        InitializeComponent();
        _vm = vm;
        _eventRepo = eventRepo;
        BindingContext = vm;
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
        await Shell.Current.GoToAsync("EventEdit");
    }

    // ── Edit event ────────────────────────────────────────────

    private async void OnEditEventClicked(object? sender, TappedEventArgs e)
    {
        // sender is the Border; its BindingContext is CalendarEventItem (inherited from parent Grid)
        if (sender is Border b && b.BindingContext is CalendarEventItem item)
            await Shell.Current.GoToAsync($"EventEdit?eventId={item.EventId}");
    }


    private async void OnWeekEventBlockTapped(object? sender, Controls.WeekEventBlockTappedEventArgs e)
    {
        await Shell.Current.GoToAsync($"EventEdit?eventId={e.EventId}&occurrenceDate={e.Date:yyyy-MM-dd}&occurrenceStartMinute={e.StartMinute}");
    }

    private async void OnWeekEmptySlotTapped(object? sender, Controls.WeekEmptySlotTappedEventArgs e)
    {
        await Shell.Current.GoToAsync($"EventEdit?startDate={e.Date:yyyy-MM-dd}&startMinute={e.StartMinute}");
    }

    private async void OnWeekEventDragCompleted(object? sender, Controls.WeekEventDragCompletedEventArgs e)
    {
        // state machine導入後: drag完了は通知のみ（保存/遷移は次工程）
        await Task.CompletedTask;
    }

    private async void OnWeekEventResizeCompleted(object? sender, Controls.WeekEventResizeCompletedEventArgs e)
    {
        // state machine導入後: resize完了は通知のみ（保存/遷移は次工程）
        await Task.CompletedTask;
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
