using NolumiaScheduler.Presentation.Services;

namespace NolumiaScheduler
{
    public partial class App : Microsoft.Maui.Controls.Application
    {
        public App(IAlarmService alarmService)
        {
            InitializeComponent();
            alarmService.Start();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
    }
}
