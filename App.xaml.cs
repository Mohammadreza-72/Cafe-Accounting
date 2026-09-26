using System.Windows;
using CafeArian.Data;

namespace CafeArian;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Database.Initialize();
        SeedData.Initialize();
    }
}
