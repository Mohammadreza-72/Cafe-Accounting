using System.Windows;
using CafeArian.Data;
using CafeArian.Services;

namespace CafeArian;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            var entry = DiagnosticsService.Record(args.Exception, "رابط برنامه");
            MessageBox.Show($"خطای پیش‌بینی‌نشده رخ داد. شناسه: {entry.Id}\nجزئیات برای مدیر در بخش عیب‌یابی ثبت شد.",
                "کافه آرین", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception error)
                DiagnosticsService.Record(error, "خطای بحرانی برنامه");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            DiagnosticsService.Record(args.Exception, "کار پس‌زمینه");
            args.SetObserved();
        };
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
            var entry = DiagnosticsService.Record(ex, "شروع برنامه");
            MessageBox.Show($"شروع برنامه انجام نشد: {ex.Message}\nشناسه خطا: {entry.Id}", "کافه آرین",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
