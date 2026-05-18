using Microsoft.UI.Xaml.Controls;

namespace NolumiaScheduler.WinUI.Helpers;

public sealed class NavigationService
{
    private Frame? _frame;

    public static NavigationService Instance { get; } = new();

    public void Initialize(Frame frame) => _frame = frame;

    public void Navigate(Type pageType, object? parameter = null)
        => _frame?.Navigate(pageType, parameter);

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
            _frame.GoBack();
    }

    public bool CanGoBack => _frame?.CanGoBack ?? false;
}
