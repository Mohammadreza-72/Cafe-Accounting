using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Windows.Input;
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
        System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
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
            users.SaveLocalProfile("ui-admin");
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
            Control<ComboBox>("PaymentModeBox").SelectedIndex = 1; Pump();
            Check(Control<TextBox>("CardBox").Text == "45000" && Control<TextBox>("CashBox").Text == "0",
                "All-card mode fills the total without a cash remainder");
            tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(Control<TextBox>("CardBox").Text == "90000", "All-card total follows cart changes");
            Control<ComboBox>("PaymentModeBox").SelectedIndex = 3;
            Control<TextBox>("CardBox").Text = "40000";
            Control<TextBox>("TransferBox").Text = "10000"; Pump();
            Check(Control<TextBox>("CashBox").Text == "40000" && Control<TextBlock>("PaymentHint").Text.Contains("40,000"),
                "Explicit split mode shows all tender amounts in the fixed summary");
            Control<ComboBox>("PaymentModeBox").SelectedIndex = 0; Pump();
            foreach (var size in new[] { new Size(900, 450), new Size(911, 485), new Size(960, 560), new Size(1093, 600), new Size(1100, 680), new Size(1360, 820) })
            {
                _window!.Width = size.Width; _window.Height = size.Height; Pump();
                var checkout = Descendants(_window).OfType<Button>().Single(x => AutomationProperties.GetName(x) == "ثبت فاکتور");
                var root = (FrameworkElement)_window.Content;
                var before = checkout.TranslatePoint(new Point(), root);
                var paymentScroller = Ancestor<ScrollViewer>(Control<TextBox>("CardBox"));
                paymentScroller.ScrollToTop(); Pump();
                var mode = Control<ComboBox>("PaymentModeBox");
                var modePosition = mode.TranslatePoint(new Point(), paymentScroller);
                Check(modePosition.Y >= 0 && modePosition.Y + mode.ActualHeight <= paymentScroller.ActualHeight + 1,
                    $"Tender selection is visible without scrolling at {size.Width}x{size.Height}");
                paymentScroller.ScrollToEnd(); Pump();
                var after = checkout.TranslatePoint(new Point(), root);
                Check(before.Y >= 0 && before.Y + checkout.ActualHeight <= root.ActualHeight + 1 &&
                    Math.Abs(before.Y - after.Y) < 1 && checkout.ActualHeight >= 40,
                    $"Checkout stays inside the window and stationary while payment scrolls at {size.Width}x{size.Height}");
                paymentScroller.ScrollToTop(); Pump();
                Render(Path.Combine(folder, $"sale-{size.Width}-{size.Height}.png"));
            }
            _window!.Width = 960; _window.Height = 560;
            Control<ComboBox>("PaymentModeBox").SelectedIndex = 3;
            Control<TextBox>("CardBox").Text = "40000"; Pump();
            Render(Path.Combine(folder, "sale-split-960.png"));
            _window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(_window), 0, Key.F4)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent }); Pump();
            Check(Control<Expander>("SaleDetailsExpander").IsExpanded && Control<TextBox>("DiscountBox").IsVisible,
                "F4 opens optional sale details before focusing the discount");
            Control<Expander>("SaleDetailsExpander").IsExpanded = false;
            var constrained = new Window { Width = 1360, Height = 820, MinWidth = 960, MinHeight = 560,
                Left = 2000, Top = 900 };
            typeof(WindowLayout).GetMethod("FitToWorkArea", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [constrained, new Rect(0, 0, 911, 485)]);
            Check(constrained.Width <= 911 && constrained.Height <= 485 && constrained.Left >= 0 &&
                constrained.Left + constrained.Width <= 911 && constrained.Top + constrained.Height <= 485,
                "Startup window fits a 1366x768 work area at 150 percent scaling");
            var login = new LoginWindow();
            Check(!Descendants(login).OfType<PasswordBox>().Any() && Descendants(login).OfType<TextBox>().Count() == 1,
                "First-run screen asks only for a name and has no password controls");
            typeof(WindowLayout).GetMethod("FitToWorkArea", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [login, new Rect(0, 0, 911, 485)]);
            Check(login.Width <= 911 && login.Height <= 485, "Login window stays inside a compact work area");
            login.Close(); constrained.Close();
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
            _window!.Width = 960; _window.Height = 560; Pump();
            Check(Control<DataGrid>("ProductGrid").IsVisible && Control<DataGrid>("ProductGrid").ActualHeight > 250,
                "Compact catalog gives the product list useful height");
            Control<TextBox>("CatalogSearchBox").Text = "no-match"; Pump();
            Check(Control<DataGrid>("ProductGrid").Items.Count == 0, "Catalog search filters rows");
            Control<TextBox>("CatalogSearchBox").Clear(); Pump();
            Control<DataGrid>("ProductGrid").SelectedItem = Control<DataGrid>("ProductGrid").Items.OfType<Product>().Single(x => x.Id == productId);
            Check(Control<ScrollViewer>("ProductEditor").IsVisible, "Selecting a product opens its separate editor");
            ClickText("بازگشت به فهرست");
            Control<DataGrid>("ProductGrid").SelectedItem = Control<DataGrid>("ProductGrid").Items.OfType<Product>().Single(x => x.Id == productId);
            Check(Control<ScrollViewer>("ProductEditor").IsVisible, "The same product can be reopened after returning to the list");
            Control<TextBox>("ProductStockBox").Text = "1";
            ClickText("ذخیره محصول");
            Check(products.Search("").Single(x => x.Id == productId).OnHand == 7 && Control<DataGrid>("ProductGrid").IsVisible,
                "Repeat stock entry saves through the form and returns to the list");
            Render(Path.Combine(folder, "catalog-960.png"));
            Click("NewProductButton");
            Control<TextBox>("ProductNameBox").Text = "بچ خریداری‌شده آزمایشی";
            Control<ComboBox>("ProductTypeBox").SelectedIndex = 3;
            ClickText("ذخیره محصول");
            Check(Control<TextBlock>("ProductStatusText").Text.Contains("فقط برای تولید"),
                "Bought-in batch product guidance does not require a production recipe");
            Check(!Control<TextBox>("ProductStockBox").IsEnabled, "Derived stock cannot be entered in the product form");
            ClickText("ثبت خرید از تأمین‌کننده");
            ClickText("ثبت سند خرید و افزایش موجودی");
            Check(Status().Contains("تأمین‌کننده"), "Incomplete purchase gives inline validation");
            Check(!Directory.Exists(Path.Combine(folder, "Logs")) || !Directory.EnumerateFiles(Path.Combine(folder, "Logs")).Any(),
                "Expected form validation does not create internal-error logs");

            Click("SettingsNav");
            Control<TextBox>("ProfileNameBox").Text = "UI renamed";
            ClickText("ذخیره نام کاربر");
            Check(Control<TextBlock>("CurrentUserText").Text == "UI renamed" && users.StartLocalSession()?.Username == "UI renamed",
                "Settings saves the local name and updates the header immediately");
            Control<TextBox>("ReceiptWidthBox").Text = "۸۰";
            Control<TextBox>("LabelWidthBox").Text = "۵۰";
            ClickText("ذخیره تنظیمات چاپ");
            var settings = new PrintSettingsService().Load();
            Check(settings.ReceiptWidthMm == 80 && settings.LabelWidthMm == 50 && Status().Contains("ذخیره شد"),
                "Persian printer widths save through the actual settings form");
            var raw = products.Save(null, "Shared ingredient", null, 0, 100, 0, 2, stockAddition: 1);
            var drinkA = products.Save(null, "Similar drink A", null, 1000, 0, 0, 3);
            var drinkB = products.Save(null, "Similar drink B", null, 1000, 0, 0, 3);
            new RecipeService().SaveItem(drinkA, raw, 1); new RecipeService().SaveItem(drinkB, raw, 1);
            Click("SalesNav");
            Control<TextBox>("SearchBox").Clear(); Pump();
            foreach (var id in new[] { drinkA, drinkB })
                Control<Panel>("ProductsWrap").Children.OfType<Button>().Single(x => ((Product)x.Tag).Id == id)
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Control<DataGrid>("CartList").Items.OfType<CartItem>().Count(x => x.ProductId == drinkA || x.ProductId == drinkB) == 1 &&
                Status().Contains("مجموع"), "Second drink sharing the last ingredient is rejected before payment");
            Click("ProductsNav"); Click("RecipesNav");
            Control<ComboBox>("RecipeProductBox").SelectedValue = drinkA;
            Click("AddWaterButton");
            var waterRow = new RecipeService().GetItems(drinkA).Single(x => x.IngredientName == "آب");
            Check(waterRow.IsUnmeasured && waterRow.LineCost == 0 && products.Search("").Single(x => x.Id == drinkA).Stock == 1,
                "One-click water requires no quantity or stock and does not change measured availability");
            Click("AddWaterButton");
            Check(new RecipeService().GetItems(drinkA).Count(x => x.IngredientName == "آب") == 1,
                "Repeated water action updates one ingredient instead of duplicating it");
            Control<DataGrid>("RecipeGrid").SelectedItem = Control<DataGrid>("RecipeGrid").Items.OfType<RecipeItem>().Single(x => x.IsUnmeasured);
            Check(Control<CheckBox>("RecipeUnmeasuredBox").IsChecked == true && !Control<TextBox>("RecipeQuantityBox").IsEnabled,
                "Unmeasured recipe row opens without an amount requirement");
            _window!.Width = 1100; _window.Height = 680; Pump();
            Render(Path.Combine(folder, "recipe-water.png"));
            _window.Width = 900; _window.Height = 450; Pump();
            Check(Control<DataGrid>("RecipeGrid").ActualHeight >= 90 &&
                Control<ScrollViewer>("RecipeEditorScroll").ScrollableHeight > 0,
                "Compact recipe keeps ingredients visible while the editor scrolls");
            Render(Path.Combine(folder, "recipe-compact.png"));
            CloseWindow();

            UserSession.Logout();
            Check(users.StartLocalSession()?.Username == "UI renamed", "Renamed profile resumes without a login prompt");
            OpenWindow();
            Check(Control<Button>("QuickSaleButton").IsVisible && Control<Button>("QuickPurchaseButton").IsVisible,
                "Local profile has full access after restarting");
            CloseWindow();
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
        _window.Left = -10000; _window.Top = -10000;
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
    private static void Render(string path)
    {
        var bitmap = new RenderTargetBitmap((int)_window!.ActualWidth, (int)_window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(_window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
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
