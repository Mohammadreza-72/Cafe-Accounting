using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using CafeArian;
using CafeArian.Data;
using CafeArian.Models;
using CafeArian.Services;

internal static class Program
{
    private static int _checks;
    private static MainWindow? _window;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Usage: ui-test <work-directory>");
        var folder = Path.Combine(Path.GetFullPath(args[0]), "ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("CAFEARIAN_DB_PATH", Path.Combine(folder, "ui.db"));
        var app = new Application();
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "ui-resources.xaml"));
        var resources = new XElement(XName.Get("ResourceDictionary", "http://schemas.microsoft.com/winfx/2006/xaml/presentation"),
            new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), source.Root!.Elements().Single().Elements());
        app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            Database.Initialize();
            SeedData.Initialize();
            var users = new UserService();
            var password = Guid.NewGuid().ToString("N");
            users.CreateInitialAdmin("ui-admin", password);
            users.Add("ui-cashier", password, "Cashier");
            users.Add("ui-stock", password, "Inventory");
            var products = new ProductService();
            var name = "نوشیدنی آزمایشی با نام طولانی برای تشخیص خوانایی در فاکتور";
            var productId = products.Save(null, name, "", 45000, 30000, 2, 1, "عدد", stockAddition: 6);
            OpenWindow();
            Check(Control<Button>("QuickSaleButton").IsVisible && Control<Button>("QuickPurchaseButton").IsVisible,
                "Admin dashboard exposes sale and purchase actions");
            Check(AutomationProperties.GetLabeledBy(Control<TextBox>("ProductSaleBox")) is TextBlock { Text: "قیمت فروش" },
                "Loaded forms link input labels to accessibility metadata");

            Click("SalesNav");
            var tile = Control<Panel>("ProductsWrap").Children.OfType<Button>().Single();
            Check(tile.Content is StackPanel && AutomationProperties.GetName(tile).Contains(name),
                "Long product name keeps an accessible full label and separate content lines");
            tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            Check(Control<DataGrid>("CartList").Items.Count == 1 && Control<TextBlock>("FinalText").Text.Contains("45"),
                "Product action updates the live cart and final total");
            foreach (var size in new[] { new Size(1100, 680), new Size(1360, 820) })
            {
                _window!.Width = size.Width; _window.Height = size.Height; Pump();
                var checkout = Descendants(_window).OfType<Button>().Single(x => AutomationProperties.GetName(x) == "ثبت فاکتور");
                var root = (FrameworkElement)_window.Content;
                var before = checkout.TranslatePoint(new Point(), root);
                var paymentScroller = Ancestor<ScrollViewer>(Control<TextBox>("CardBox"));
                paymentScroller.ScrollToEnd(); Pump();
                var after = checkout.TranslatePoint(new Point(), root);
                Check(before.Y >= 0 && before.Y + checkout.ActualHeight <= root.ActualHeight + 1 &&
                    Math.Abs(before.Y - after.Y) < 1 && checkout.ActualHeight >= 40,
                    $"Checkout stays inside the window and stationary while payment scrolls at {size.Width}x{size.Height}");
            }
            Control<TextBox>("SearchBox").Text = "no-such-test-item"; Pump();
            Check(Control<Panel>("ProductsWrap").Children.OfType<TextBlock>().Any(x => x.Text.Contains("پیدا نشد")),
                "Unmatched product search gives recovery guidance");

            Click("CustomersNav");
            Control<TextBox>("CustomerNameBox").Text = "مشتری ساختگی آزمون";
            Control<TextBox>("CustomerMobileBox").Text = "123";
            ClickText("ذخیره مشتری");
            Check(new CustomerService().Search("").Count == 0 && Control<TextBlock>("CustomerStatusText").Text.Length > 0 &&
                Control<TextBox>("CustomerNameBox").Text.Length > 0,
                "Invalid customer phone keeps the entered name and explains the error without saving");
            Control<TextBox>("CustomerMobileBox").Text = "۰۹۱۲۳۴۵۶۷۸۹";
            ClickText("ذخیره مشتری");
            Check(new CustomerService().Search("").Count == 1 && Control<TextBlock>("CustomerStatusText").Text.Contains("ذخیره شد"),
                "Persian phone registers through the customer form with visible confirmation");
            ClickText("سوابق خرید مشتری");
            Check(Status().Contains("انتخاب کنید"), "Missing row selection produces actionable feedback");

            Click("ProductsNav");
            Control<TextBox>("ProductNameBox").Text = "بچ خریداری‌شده آزمایشی";
            Control<ComboBox>("ProductTypeBox").SelectedIndex = 3;
            ClickText("ذخیره محصول");
            Check(Control<TextBlock>("ProductStatusText").Text.Contains("فقط برای تولید"),
                "Bought-in batch product guidance does not require a production recipe");
            ClickText("ثبت خرید و مدیریت موجودی");
            ClickText("ثبت سند خرید و افزایش موجودی");
            Check(Status().Contains("تأمین‌کننده"), "Incomplete purchase gives inline validation");
            Check(!Directory.Exists(Path.Combine(folder, "Logs")) || !Directory.EnumerateFiles(Path.Combine(folder, "Logs")).Any(),
                "Expected form validation does not create internal-error logs");

            Click("SettingsNav");
            Control<TextBox>("ReceiptWidthBox").Text = "۸۰";
            Control<TextBox>("LabelWidthBox").Text = "۵۰";
            ClickText("ذخیره تنظیمات چاپ");
            var settings = new PrintSettingsService().Load();
            Check(settings.ReceiptWidthMm == 80 && settings.LabelWidthMm == 50 && Status().Contains("ذخیره شد"),
                "Persian printer widths save through the actual settings form");
            CloseWindow();

            foreach (var role in new[] { ("ui-cashier", true, false), ("ui-stock", false, true) })
            {
                users.SignIn(role.Item1, password);
                OpenWindow();
                Check(Control<Button>("QuickSaleButton").IsVisible == role.Item2 &&
                    Control<Button>("QuickProductButton").IsVisible == role.Item3 &&
                    Control<Button>("QuickPurchaseButton").IsVisible == role.Item3,
                    $"{role.Item1} dashboard only suggests permitted work");
                CloseWindow();
            }
            Console.WriteLine($"UI checks passed: {_checks}. Synthetic database: {folder}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { CloseWindow(); app.Shutdown(); }
    }

    private static void OpenWindow()
    {
        _window = new MainWindow { Width = 1100, Height = 680, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        _window.Show();
        Pump();
    }

    private static void CloseWindow()
    {
        if (_window is null) return;
        // Clean only the disposable test host; never dismiss a real user's draft warning.
        ((ObservableCollection<CartItem>)typeof(MainWindow).GetField("_cart", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_window)!).Clear();
        ((ObservableCollection<PurchaseLine>)typeof(MainWindow).GetField("_purchaseLines", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_window)!).Clear();
        _window.Close(); _window = null;
    }

    private static T Control<T>(string name) where T : FrameworkElement => (T)_window!.FindName(name);
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { _window?.UpdateLayout(); }, DispatcherPriority.ApplicationIdle);
    private static void Click(string name) { Control<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
    private static void ClickText(string text)
    {
        Descendants(_window!).OfType<Button>().Single(x => Equals(x.Content, text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
    }
    private static string Status() => Control<TextBlock>("OperationStatusText").Text;
    private static T Ancestor<T>(DependencyObject child) where T : DependencyObject
    {
        for (var current = VisualTreeHelper.GetParent(child); current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is T result) return result;
        throw new Exception($"Missing {typeof(T).Name} ancestor");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL: " + message);
        _checks++; Console.WriteLine("PASS: " + message);
    }
}
