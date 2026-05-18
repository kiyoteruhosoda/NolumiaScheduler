using NolumiaScheduler.Presentation.Pages;
using NolumiaScheduler.Presentation.Services;

namespace NolumiaScheduler
{
    public partial class App : Microsoft.Maui.Controls.Application
    {
        private readonly IServiceProvider _services;

        public App(IAlarmService alarmService, IServiceProvider services)
        {
            InitializeComponent();
            _services = services;
            alarmService.Start();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var mainWindow = new Window(new AppShell()) { Title = "NolumiaScheduler" };

#if DEBUG
            mainWindow.Created += (_, _) =>
            {
                var debugPage = _services.GetRequiredService<AlarmDebugPage>();
                var debugWindow = new Window(new NavigationPage(debugPage))
                {
                    Title = "アラームデバッグ",
                    Width = 420,
                    Height = 600,
                    X = 50,
                    Y = 50
                };
                OpenWindow(debugWindow);
            };
#endif

            return mainWindow;
        }
    }
}
