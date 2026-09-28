using System.Windows;
using CafeArian.Data;
using CafeArian.Services;

namespace CafeArian;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            Database.Initialize();
            SeedData.Initialize();
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var login = new LoginWindow();
            if (login.ShowDialog() != true || login.User is null)
            {
                Shutdown();
                return;
            }
            var main = new MainWindow();
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"شروع برنامه انجام نشد: {ex.Message}", "کافه آرین",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
