using NolumiaScheduler.Presentation.Pages;

namespace NolumiaScheduler;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("BusinessCalendarEdit", typeof(BusinessCalendarEditPage));
        Routing.RegisterRoute("EventEdit",            typeof(EventEditPage));
    }
}
