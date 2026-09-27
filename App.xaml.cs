using System.Windows;
using CafeArian.Data;

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
        }
        catch (Exception ex)
        {
            MessageBox.Show($"پایگاه داده باز نشد: {ex.Message}", "کافه آرین",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
