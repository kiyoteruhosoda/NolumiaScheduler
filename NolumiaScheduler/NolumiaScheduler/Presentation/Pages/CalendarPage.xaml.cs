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

    // Colors for hover, resolved once after page is loaded
    private Color _hoverColor = Color.FromArgb("#e0e0e0");
    private Color _transparentColor = Colors.Transparent;

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
        if (MauiApp.Current?.Resources.TryGetValue("GCalBorder", out var light) == true && light is Color lc)
            _hoverColor = lc;
        if (MauiApp.Current?.Resources.TryGetValue("GCalBorderDark", out var dark) == true && dark is Color dc)
        {
            var isDark = MauiApp.Current.RequestedTheme == AppTheme.Dark;
            if (isDark) _hoverColor = dc;
        }
    }

    private void OnEventItemPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border border)
            border.BackgroundColor = _hoverColor;
    }

    private void OnEventItemPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border border)
            border.BackgroundColor = _transparentColor;
    }

    private void OnDayCellSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is CalendarDayCell cell)
        {
            _vm.SelectDay(cell);
            ((CollectionView)sender!).SelectedItem = null;
        }
    }

    private async void OnNewEventClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("EventEdit");
    }

    private async void OnEditEventClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.BindingContext is CalendarEventItem item)
        {
            await Shell.Current.GoToAsync($"EventEdit?eventId={item.EventId}");
        }
    }

    private async void OnDeleteEventClicked(object? sender, EventArgs e)
    {
        if (sender is not Button btn || btn.BindingContext is not CalendarEventItem item)
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
