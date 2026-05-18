using Microsoft.UI.Xaml;

// To learn more about WinMAUI, the WinMAUI project structure,
// and more about our project templates, see: http://aka.ms/WinMAUI-project-info.

namespace NolumiaScheduler.WinMAUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : MauiWinMAUIApplication
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }

}
