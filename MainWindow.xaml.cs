using CafeArian.Data;
using CafeArian.Models;
using CafeArian.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CafeArian;

public partial class MainWindow : Window
{
    private readonly ProductService _products = new();
    private readonly SaleService _sales = new();
    private readonly CustomerService _customers = new();
    private readonly OperationsService _operations = new();
    private readonly ReceiptService _receipts = new();
    private readonly BackupService _backups = new();
    private readonly ObservableCollection<CartItem> _cart = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private long? _editingProductId;
    private bool _changingPayment;

    public MainWindow()
    {
        InitializeComponent();
        CartList.ItemsSource = _cart;
        _timer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("yyyy/MM/dd  HH:mm");
        _timer.Start();
        ClockText.Text = DateTime.Now.ToString("yyyy/MM/dd  HH:mm");
        DatabasePathText.Text = Database.DbPath;
        ShowPage(DashboardPage, "داشبورد", "مرور وضعیت امروز کافه");
        Loaded += (_, _) => RefreshAll();
    }

    private void ShowPage(UIElement page, string title, string subtitle)
    {
        var pages = new UIElement[] { DashboardPage, SalesPage, ProductsPage, InventoryPage,
            CustomersPage, HistoryPage, FinancePage, SettingsPage };
        foreach (var item in pages)
            item.Visibility = item == page ? Visibility.Visible : Visibility.Collapsed;
        var navigation = new[] { DashboardNav, SalesNav, ProductsNav, InventoryNav,
            CustomersNav, HistoryNav, FinanceNav, SettingsNav };
        for (var i = 0; i < navigation.Length; i++)
        {
            navigation[i].Background = pages[i] == page ? new SolidColorBrush(Color.FromRgb(52, 56, 59)) : Brushes.Transparent;
            navigation[i].FontWeight = pages[i] == page ? FontWeights.Bold : FontWeights.Normal;
        }
        PageTitle.Text = title;
        PageSubtitle.Text = subtitle;
    }

    private void RefreshAll()
    {
        RefreshDashboard();
        RefreshProducts();
        RefreshCustomers();
        RefreshHistory();
        PurchaseGrid.ItemsSource = _operations.Purchases();
        ExpenseGrid.ItemsSource = _operations.Expenses();
        UpdateTotals();
    }

    private void RefreshDashboard()
    {
        var summary = _operations.Dashboard();
        TodaySalesText.Text = Money(summary.TodaySales);
        TodayExpensesText.Text = Money(summary.TodayExpenses);
        TodayProfitText.Text = Money(summary.TodayNetProfit);
        LowStockText.Text = summary.LowStockCount.ToString("N0");
        WeeklySalesChart.ItemsSource = _operations.WeeklySales();
        var lowStock = _operations.LowStockProducts();
        LowStockList.ItemsSource = lowStock;
        NoLowStockText.Visibility = lowStock.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FinanceSummaryText.Text =
            $"فروش: {Money(summary.TodaySales)}\nهزینه ثبت‌شده: {Money(summary.TodayExpenses)}\n" +
            $"سود ناخالص تخمینی: {Money(summary.TodayGrossProfit)}\nسود خالص تخمینی: {Money(summary.TodayNetProfit)}";
    }

    private void RefreshProducts()
    {
        var list = _products.Search("");
        ProductGrid.ItemsSource = list;
        InventoryGrid.ItemsSource = list;
        PurchaseProductBox.ItemsSource = list;
        SearchProducts(SearchBox.Text);
    }

    private void SearchProducts(string query)
    {
        ProductsWrap.Children.Clear();
        var products = _products.Search(query);
        ProductsHint.Text = $"{products.Count} محصول";
        foreach (var product in products)
        {
            var button = new Button
            {
                Width = 162, Height = 95, Margin = new Thickness(5),
                Content = $"{product.Name}\n{product.SalePrice:N0} تومان\nموجودی {product.Stock:N0}",
                Tag = product, Background = Brushes.White,
                BorderBrush = (Brush)FindResource("LineBrush"),
                IsEnabled = product.Stock > 0
            };
            button.Click += Product_Click;
            ProductsWrap.Children.Add(button);
        }
    }

    private void AddProduct(Product product)
    {
        var existing = _cart.FirstOrDefault(x => x.ProductId == product.Id);
        if ((existing?.Quantity ?? 0) + 1 > product.Stock)
        {
            MessageBox.Show("موجودی این محصول کافی نیست.", "کافه آرین");
            return;
        }
        if (existing is null)
            _cart.Add(new CartItem { ProductId = product.Id, ProductName = product.Name, UnitPrice = product.SalePrice });
        else existing.Quantity++;
        CartList.Items.Refresh();
        UpdateTotals();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void UpdateTotals()
    {
        var subtotal = _cart.Sum(x => x.Total);
        var discount = TryMoney(DiscountBox.Text, out var parsedDiscount) ? parsedDiscount : -1;
        var validDiscount = discount >= 0 && discount <= subtotal;
        var final = validDiscount ? subtotal - discount : subtotal;
        SubtotalText.Text = Money(subtotal);
        FinalText.Text = validDiscount ? Money(final) : "تخفیف نامعتبر";
        if (!_changingPayment)
        {
            _changingPayment = true;
            var card = TryMoney(CardBox.Text, out var parsedCard) ? parsedCard : -1;
            if (card >= 0 && card <= final) CashBox.Text = (final - card).ToString("0");
            else CashBox.Text = "0";
            PaymentHint.Text = !validDiscount ? "تخفیف را اصلاح کنید." :
                card < 0 || card > final ? "مبلغ کارتخوان نامعتبر است." :
                "باقی‌مانده به‌صورت نقدی محاسبه می‌شود.";
            _changingPayment = false;
        }
    }

    private static string Money(decimal amount) => $"{amount:N0} تومان";

    private static bool TryMoney(string? input, out decimal value)
    {
        var normalized = new string((input ?? "").Trim().Select(c => c switch
        {
            >= '۰' and <= '۹' => (char)('0' + c - '۰'),
            >= '٠' and <= '٩' => (char)('0' + c - '٠'),
            '٬' or ',' or ' ' => '\0',
            _ => c
        }).Where(c => c != '\0').ToArray());
        return decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value) && value == decimal.Truncate(value);
    }

    private static decimal RequiredMoney(string? input, string label)
    {
        if (!TryMoney(input, out var value) || value < 0)
            throw new InvalidOperationException($"{label} باید عدد صحیح و غیرمنفی باشد.");
        return value;
    }

    private void Product_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Product product }) AddProduct(product);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) SearchProducts(SearchBox.Text);
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var product = _products.FindByBarcode(SearchBox.Text);
        if (product is not null)
        {
            AddProduct(product);
            SearchBox.Clear();
            e.Handled = true;
        }
    }

    private void DiscountBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) UpdateTotals();
    }

    private void PaymentBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded && !_changingPayment) UpdateTotals();
    }

    private void IncreaseItem_Click(object sender, RoutedEventArgs e)
    {
        if (CartList.SelectedItem is not CartItem selected) return;
        var product = _products.Search("").FirstOrDefault(x => x.Id == selected.ProductId);
        if (product is not null) AddProduct(product);
    }

    private void DecreaseItem_Click(object sender, RoutedEventArgs e)
    {
        if (CartList.SelectedItem is not CartItem selected) return;
        if (selected.Quantity <= 1) _cart.Remove(selected);
        else selected.Quantity--;
        CartList.Items.Refresh();
        UpdateTotals();
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (CartList.SelectedItem is CartItem selected) _cart.Remove(selected);
        UpdateTotals();
    }

    private void Checkout_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var discount = RequiredMoney(DiscountBox.Text, "تخفیف");
            var card = RequiredMoney(CardBox.Text, "مبلغ کارتخوان");
            var cash = RequiredMoney(CashBox.Text, "مبلغ نقدی");
            var saleId = _sales.CreateSale(_cart.ToList(), discount, MobileBox.Text, cash, card);
            _cart.Clear();
            DiscountBox.Text = "0";
            CardBox.Text = "0";
            MobileBox.Clear();
            RefreshAll();
            if (MessageBox.Show($"فاکتور AR-{saleId:D6} ثبت شد. چاپ شود؟", "کافه آرین",
                MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                _receipts.Print(saleId);
            SearchBox.Focus();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void RefreshCustomers() => CustomerGrid.ItemsSource = _customers.Search(CustomerSearchBox.Text);
    private void CustomerSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) RefreshCustomers();
    }
    private void SaveCustomer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _customers.Save(CustomerNameBox.Text, CustomerMobileBox.Text);
            CustomerNameBox.Clear(); CustomerMobileBox.Clear();
            RefreshCustomers(); RefreshDashboard();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void CustomerHistory_Click(object sender, RoutedEventArgs e)
    {
        if (CustomerGrid.SelectedItem is not Customer customer) return;
        HistorySearchBox.Text = customer.Mobile;
        History_Click(sender, e);
    }

    private void ProductGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ProductGrid.SelectedItem is not Product product) return;
        _editingProductId = product.Id;
        ProductNameBox.Text = product.Name;
        ProductBarcodeBox.Text = product.Barcode ?? "";
        ProductSaleBox.Text = product.SalePrice.ToString("0");
        ProductCostBox.Text = product.CostPrice.ToString("0");
        ProductMinimumBox.Text = product.MinimumStock.ToString("0");
    }
    private void NewProduct_Click(object sender, RoutedEventArgs e) => ClearProductForm();
    private void ClearProductForm()
    {
        _editingProductId = null;
        ProductGrid.SelectedItem = null;
        ProductNameBox.Clear(); ProductBarcodeBox.Clear(); ProductSaleBox.Clear(); ProductCostBox.Clear();
        ProductMinimumBox.Text = "0";
        ProductNameBox.Focus();
    }
    private void SaveProduct_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _products.Save(_editingProductId, ProductNameBox.Text, ProductBarcodeBox.Text,
                RequiredMoney(ProductSaleBox.Text, "قیمت فروش"),
                RequiredMoney(ProductCostBox.Text, "بهای خرید"),
                RequiredMoney(ProductMinimumBox.Text, "حداقل موجودی"));
            RefreshProducts(); RefreshDashboard(); ClearProductForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void DeactivateProduct_Click(object sender, RoutedEventArgs e)
    {
        if (ProductGrid.SelectedItem is not Product product) return;
        if (MessageBox.Show($"محصول «{product.Name}» غیرفعال شود؟", "تأیید",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _products.Deactivate(product.Id);
        RefreshProducts(); ClearProductForm();
    }

    private void Purchase_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (PurchaseProductBox.SelectedItem is not Product product)
                throw new InvalidOperationException("محصول را انتخاب کنید.");
            if (!TryMoney(PurchaseQuantityBox.Text, out var quantity) || quantity <= 0)
                throw new InvalidOperationException("مقدار خرید باید عدد صحیح مثبت باشد.");
            _operations.RecordPurchase(product.Id, SupplierBox.Text, PurchaseInvoiceBox.Text,
                quantity, RequiredMoney(PurchaseCostBox.Text, "قیمت واحد"));
            PurchaseQuantityBox.Clear(); PurchaseCostBox.Clear(); PurchaseInvoiceBox.Clear();
            RefreshProducts(); RefreshDashboard();
            PurchaseGrid.ItemsSource = _operations.Purchases();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void Expense_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _operations.RecordExpense(ExpenseDescriptionBox.Text,
                RequiredMoney(ExpenseAmountBox.Text, "مبلغ هزینه"));
            ExpenseDescriptionBox.Clear(); ExpenseAmountBox.Clear();
            RefreshDashboard();
            ExpenseGrid.ItemsSource = _operations.Expenses();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void RefreshHistory() => HistoryGrid.ItemsSource = _operations.Sales(HistorySearchBox.Text);
    private void HistorySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) RefreshHistory();
    }
    private void ExportSales_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv", DefaultExt = ".csv",
            FileName = $"CafeArian-sales-{DateTime.Now:yyyyMMdd}.csv"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var lines = new List<string> { "شماره فاکتور,تاریخ,مشتری,موبایل,مبلغ,وضعیت" };
            lines.AddRange(_operations.Sales(HistorySearchBox.Text, -1).Select(s =>
                string.Join(",", Csv(s.InvoiceNumber), Csv(s.Date), Csv(s.Customer),
                    Csv(s.Mobile), s.Amount.ToString(CultureInfo.InvariantCulture), Csv(s.Status))));
            File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));
            MessageBox.Show("گزارش فروش ذخیره شد.", "کافه آرین");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private static string Csv(string value)
    {
        var safe = value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') ||
                   value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')
            ? "'" + value : value;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }
    private void PrintSelected_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not SaleRecord sale) return;
        try { _receipts.Print(sale.Id); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void CancelSelected_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not SaleRecord sale || sale.Status != "Completed") return;
        if (MessageBox.Show($"فاکتور {sale.InvoiceNumber} لغو و موجودی آن برگردانده شود؟ بازپرداخت وجه باید جداگانه انجام شود.",
            "تأیید لغو", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { _sales.CancelSale(sale.Id); RefreshAll(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "SQLite database (*.db)|*.db",
            FileName = $"CafeArian-backup-{DateTime.Now:yyyyMMdd-HHmm}.db",
            DefaultExt = ".db"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _backups.Create(dialog.FileName);
            MessageBox.Show("نسخه پشتیبان ذخیره شد.", "کافه آرین");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "SQLite database (*.db)|*.db" };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show("داده‌های فعلی با این فایل جایگزین شوند؟ یک نسخهٔ ایمنی از داده‌های فعلی حفظ می‌شود.",
            "تأیید بازیابی", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            _backups.Restore(dialog.FileName);
            RefreshAll();
            MessageBox.Show("بازیابی انجام شد. برای اطمینان، برنامه را یک‌بار ببندید و دوباره باز کنید.", "کافه آرین");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        RefreshDashboard();
        ShowPage(DashboardPage, "داشبورد", "مرور وضعیت امروز کافه");
    }
    private void Sales_Click(object sender, RoutedEventArgs e)
    {
        RefreshProducts();
        ShowPage(SalesPage, "فروش سریع", "جستجو، ثبت سفارش و پرداخت");
        SearchBox.Focus();
    }
    private void Products_Click(object sender, RoutedEventArgs e)
    {
        RefreshProducts();
        ShowPage(ProductsPage, "محصولات", "تعریف، قیمت‌گذاری و غیرفعال‌سازی");
    }
    private void Inventory_Click(object sender, RoutedEventArgs e)
    {
        RefreshProducts();
        ShowPage(InventoryPage, "خرید و موجودی", "ثبت ورود کالا و مشاهده موجودی");
    }
    private void Customers_Click(object sender, RoutedEventArgs e)
    {
        RefreshCustomers();
        ShowPage(CustomersPage, "مشتریان", "اطلاعات تماس و جمع خرید");
    }
    private void History_Click(object sender, RoutedEventArgs e)
    {
        RefreshHistory();
        ShowPage(HistoryPage, "فاکتورها", "جستجو، چاپ مجدد و لغو");
    }
    private void Finance_Click(object sender, RoutedEventArgs e)
    {
        RefreshDashboard();
        ShowPage(FinancePage, "مالی و گزارش", "ثبت هزینه و مرور خلاصه امروز");
    }
    private void Settings_Click(object sender, RoutedEventArgs e) =>
        ShowPage(SettingsPage, "پشتیبان‌گیری", "ذخیره امن داده‌های آفلاین");

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F1: Sales_Click(sender, new RoutedEventArgs()); e.Handled = true; break;
            case Key.F2: Sales_Click(sender, new RoutedEventArgs()); SearchBox.Focus(); e.Handled = true; break;
            case Key.F3: Customers_Click(sender, new RoutedEventArgs()); e.Handled = true; break;
            case Key.F4: Sales_Click(sender, new RoutedEventArgs()); DiscountBox.Focus(); e.Handled = true; break;
            case Key.F5 when SalesPage.Visibility == Visibility.Visible:
                Checkout_Click(sender, new RoutedEventArgs()); e.Handled = true; break;
            case Key.Escape when SearchBox.IsKeyboardFocusWithin: SearchBox.Clear(); e.Handled = true; break;
        }
    }
    private static void ShowError(Exception ex) =>
        MessageBox.Show(ex.Message, "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
}
