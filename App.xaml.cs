using System.Windows;

namespace VpnClient
{
    public partial class App : System.Windows.Application
    {
        private MainWindow? _mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _mainWindow = new MainWindow();
            _mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Освобождаем ресурсы при любом выходе
            _mainWindow?.CleanupOnExit();
            base.OnExit(e);
        }
    }
}