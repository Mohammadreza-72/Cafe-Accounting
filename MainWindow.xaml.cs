using CafeArian.Data;
using CafeArian.Models;
using CafeArian.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CafeArian;

public partial class MainWindow : Window
{
    private readonly ProductService _products = new();
    private readonly CategoryService _categories = new();
    private readonly SaleService _sales = new();
    private readonly CustomerService _customers = new();
    private readonly OperationsService _operations = new();
    private readonly JournalService _journal = new();
    private readonly ReportService _reports = new();
    private readonly PrintSettingsService _printSettings = new();
    private readonly ExpenseCategoryService _expenseCategories = new();
    private readonly ReceiptService _receipts = new();
    private readonly LabelService _labels = new();
    private readonly RecipeService _recipes = new();
    private readonly BatchService _batches = new();
    private readonly PaymentAccountService _paymentAccounts = new();
    private readonly SupplierService _suppliers = new();
    private readonly DiscountService _discounts = new();
    private readonly ChargeSettingsService _charges = new();
    private readonly UserService _users = new();
    private readonly BackupService _backups = new();
    private readonly ObservableCollection<CartItem> _cart = new();
    private readonly ObservableCollection<PurchaseLine> _purchaseLines = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private long? _editingProductId;
    private bool _changingPayment;
    private string? _registeredMobile;
    private bool _allowClose;
    private bool _refreshingProducts;

    public MainWindow()
    {
        InitializeComponent();
        WindowLayout.Attach(this);
        var current = UserSession.Current ?? throw new InvalidOperationException("کاربر وارد نشده است.");
        CurrentUserText.Text = $"{current.Username} | {current.RoleName}";
        var admin = current.Role == "Admin";
        var inventory = current.Role == "Inventory";
        QuickSaleButton.Visibility = admin || current.Role == "Cashier" ? Visibility.Visible : Visibility.Collapsed;
        QuickProductButton.Visibility = QuickPurchaseButton.Visibility = admin || inventory ? Visibility.Visible : Visibility.Collapsed;
        Closing += Window_Closing;
        SizeChanged += (_, _) =>
        {
            SidebarColumn.Width = new GridLength(ActualWidth < 1150 ? 155 : 190);
            BrandPanel.Height = ActualHeight < 680 ? 55 : 90;
        };
        SalesNav.Visibility = CustomersNav.Visibility = HistoryNav.Visibility =
            admin || current.Role == "Cashier" ? Visibility.Visible : Visibility.Collapsed;
        ProductsNav.Visibility = RecipesNav.Visibility = BatchesNav.Visibility =
            InventoryNav.Visibility = SuppliersNav.Visibility =
            admin || inventory ? Visibility.Visible : Visibility.Collapsed;
        InventoryNav.Visibility = Visibility.Collapsed;
        DiscountsNav.Visibility = FinanceNav.Visibility = AccountsNav.Visibility =
            UsersNav.Visibility = SettingsNav.Visibility = DiagnosticsNav.Visibility =
            admin ? Visibility.Visible : Visibility.Collapsed;
        CancelSelectedButton.Visibility = admin ? Visibility.Visible : Visibility.Collapsed;
        SettlePurchaseButton.Visibility = admin ? Visibility.Visible : Visibility.Collapsed;
        PurchasePaymentBox.IsEnabled = PurchaseBankBox.IsEnabled = admin;
        ExpenseCard.Visibility = ProfitCard.Visibility = admin ? Visibility.Visible : Visibility.Collapsed;
        CartList.ItemsSource = _cart;
        PurchaseLinesGrid.ItemsSource = _purchaseLines;
        _timer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("yyyy/MM/dd  HH:mm");
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
        ClockText.Text = DateTime.Now.ToString("yyyy/MM/dd  HH:mm");
        DatabasePathText.Text = Database.DbPath;
        ShowPage(DashboardPage, "داشبورد", "مرور وضعیت امروز کافه");
        Loaded += async (_, _) =>
        {
            AttachInputLabels(this);
            RefreshAll();
            try
            {
                var path = await Task.Run(() => _backups.AutoBackupIfNeeded());
                AutoBackupStatusText.Text = $"پشتیبان امروز: {path}";
            }
            catch (Exception ex)
            {
                var entry = DiagnosticsService.Record(ex, "پشتیبان خودکار");
                AutoBackupStatusText.Text = $"پشتیبان خودکار انجام نشد. شناسه خطا: {entry.Id}";
            }
        };
    }

    private void ShowPage(UIElement page, string title, string subtitle)
    {
        var role = UserSession.Current?.Role;
        var allowed = page == DashboardPage || role == "Admin" ||
            role == "Cashier" && (page == SalesPage || page == CustomersPage || page == HistoryPage) ||
            role == "Inventory" && (page == ProductsPage || page == RecipesPage ||
                page == BatchesPage || page == InventoryPage || page == SuppliersPage);
        if (!allowed)
        {
            MessageBox.Show("برای این بخش دسترسی ندارید.", "کافه آرین");
            return;
        }
        var pages = new UIElement[] { DashboardPage, SalesPage, ProductsPage, RecipesPage, BatchesPage, InventoryPage,
            SuppliersPage, CustomersPage, DiscountsPage, HistoryPage, FinancePage, AccountsPage, UsersPage,
            SettingsPage, DiagnosticsPage };
        foreach (var item in pages)
            item.Visibility = item == page ? Visibility.Visible : Visibility.Collapsed;
        var navigation = new[] { DashboardNav, SalesNav, ProductsNav, RecipesNav, BatchesNav, InventoryNav,
            SuppliersNav, CustomersNav, DiscountsNav, HistoryNav, FinanceNav, AccountsNav, UsersNav,
            SettingsNav, DiagnosticsNav };
        for (var i = 0; i < navigation.Length; i++)
        {
            var active = pages[i] == page || navigation[i] == ProductsNav && page == InventoryPage;
            navigation[i].Background = active ? new SolidColorBrush(Color.FromRgb(52, 56, 59)) : Brushes.Transparent;
            navigation[i].FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
        }
        PageTitle.Text = title;
        PageSubtitle.Text = subtitle;
        OperationStatus.Visibility = Visibility.Collapsed;
    }

    private void SetStatus(string message, bool error = false)
    {
        OperationStatusText.Text = (error ? "نیاز به اصلاح: " : "انجام شد: ") + message;
        OperationStatusText.Foreground = error ? new SolidColorBrush(Color.FromRgb(180, 35, 24)) : (Brush)FindResource("AccentBrush");
        OperationStatus.Visibility = Visibility.Visible;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || (_cart.Count == 0 && _purchaseLines.Count == 0)) return;
        e.Cancel = MessageBox.Show(this,
            "فاکتور فروش یا سند خرید ثبت‌نشده دارید. با بستن برنامه اقلام آن از دست می‌روند. برنامه بسته شود؟",
            "سند ثبت‌نشده", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes;
    }

    private static void AttachInputLabels(DependencyObject parent)
    {
        if (parent is StackPanel panel)
        {
            TextBlock? label = null;
            foreach (var child in panel.Children)
            {
                if (child is TextBlock text) label = text;
                else if (child is Control control && child is TextBox or PasswordBox or ComboBox or DatePicker)
                {
                    if (label is not null) AutomationProperties.SetLabeledBy(control, label);
                    label = null;
                }
                else label = null;
            }
        }
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            AttachInputLabels(child);
    }

    private void RefreshAll()
    {
        RefreshDashboard();
        RefreshProducts();
        RefreshCustomers();
        if (UserSession.Current?.Role is "Admin" or "Inventory") RefreshSuppliers();
        if (UserSession.Current?.Role == "Admin")
        {
            RefreshDiscounts(); RefreshChargeSettings(); RefreshPrintSettings(); RefreshExpenseCategories(); RefreshUsers();
        }
        RefreshHistory();
        PurchaseGrid.ItemsSource = _operations.Purchases();
        ExpenseGrid.ItemsSource = _operations.Expenses();
        RefreshPaymentAccounts();
        UpdateTotals();
    }

    private void RefreshPaymentAccounts()
    {
        var banks = _paymentAccounts.Banks();
        var devices = _paymentAccounts.Devices();
        BankGrid.ItemsSource = banks;
        PosGrid.ItemsSource = devices;
        PosBankBox.ItemsSource = banks;
        TransferBankBox.ItemsSource = banks;
        PurchaseBankBox.ItemsSource = banks;
        SettlementBankBox.ItemsSource = banks;
        ExpenseBankBox.ItemsSource = banks;
        PosDeviceBox.ItemsSource = devices;
    }

    private void RefreshDiscounts() => DiscountGrid.ItemsSource = _discounts.All();
    private void RefreshUsers() => UserGrid.ItemsSource = _users.All();

    private void RefreshChargeSettings()
    {
        var settings = _charges.Load();
        ChargeTaxModeBox.SelectedIndex = settings.TaxMode switch { "Percent" => 1, "Fixed" => 2, _ => 0 };
        ChargeTaxValueBox.Text = settings.TaxValue.ToString("0", CultureInfo.InvariantCulture);
        ChargeTaxBaseBox.SelectedIndex = settings.TaxBase == "BeforeDiscount" ? 1 : 0;
        ChargeFeeModeBox.SelectedIndex = settings.FeeMode switch { "Percent" => 1, "Fixed" => 2, _ => 0 };
        ChargeFeeValueBox.Text = settings.FeeValue.ToString("0", CultureInfo.InvariantCulture);
        ChargeFeeBaseBox.SelectedIndex = settings.FeeBase == "BeforeDiscount" ? 1 : 0;
        ChargeRoundingBox.SelectedIndex = settings.RoundingMode switch { "Floor" => 1, "Ceiling" => 2, _ => 0 };
    }

    private void RefreshPrintSettings()
    {
        var settings = _printSettings.Load();
        ReceiptPrinterBox.Text = settings.ReceiptPrinter;
        LabelPrinterBox.Text = settings.LabelPrinter;
        ReceiptWidthBox.Text = settings.ReceiptWidthMm.ToString(CultureInfo.InvariantCulture);
        LabelWidthBox.Text = settings.LabelWidthMm.ToString(CultureInfo.InvariantCulture);
    }

    private void RefreshExpenseCategories()
    {
        var selected = ExpenseCategoryBox.SelectedValue;
        var list = _expenseCategories.All();
        ExpenseCategoryBox.ItemsSource = list;
        ExpenseCategoryBox.SelectedValue = selected;
        if (ExpenseCategoryBox.SelectedItem is null)
            ExpenseCategoryBox.SelectedValue = list.FirstOrDefault(x => x.Name == "سایر")?.Id;
    }

    private void RefreshSuppliers()
    {
        var selectedId = (PurchaseSupplierBox.SelectedItem as Supplier)?.Id;
        var list = _suppliers.All();
        SupplierGrid.ItemsSource = list;
        PurchaseSupplierBox.ItemsSource = list;
        if (selectedId.HasValue)
            PurchaseSupplierBox.SelectedItem = list.FirstOrDefault(x => x.Id == selectedId);
    }

    private void RefreshDashboard()
    {
        var summary = _operations.Dashboard();
        TodaySalesText.Text = Money(summary.TodaySales);
        TodayExpensesText.Text = Money(summary.TodayExpenses);
        TodayProfitText.Text = Money(summary.TodayNetProfit);
        LowStockText.Text = summary.LowStockCount.ToString("N0");
        WeeklySalesChart.ItemsSource = _operations.WeeklySales();
        TopProductsGrid.ItemsSource = _operations.TopProducts();
        var lowStock = _operations.LowStockProducts();
        LowStockList.ItemsSource = lowStock;
        NoLowStockText.Visibility = lowStock.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FinanceSummaryText.Text =
            $"فروش: {Money(summary.TodaySales)}\nهزینه ثبت‌شده: {Money(summary.TodayExpenses)}\n" +
            $"سود ناخالص تخمینی: {Money(summary.TodayGrossProfit)}\n" +
            $"اثر اصلاح موجودی: {Money(summary.TodayInventoryAdjustmentCost)}\n" +
            $"سود خالص تخمینی: {Money(summary.TodayNetProfit)}\n" +
            string.Join("\n", _operations.TodayPayments().Select(x => $"{x.Key}: {Money(x.Value)}")) +
            (UserSession.Current?.Role == "Admin"
                ? $"\nمانده ثبت‌شده صندوق: {Money(_journal.Balance("1100"))}" +
                  $"\nمانده ثبت‌شده بانک: {Money(_journal.Balance("1200"))}" +
                  $"\nبدهی ثبت‌شده به تأمین‌کنندگان: {Money(-_journal.Balance("2100"))}"
                : "");
    }

    private void RefreshProducts()
    {
        var categoryId = ProductCategoryBox.SelectedValue;
        ProductCategoryBox.ItemsSource = _categories.All();
        ProductCategoryBox.SelectedValue = categoryId;
        var list = _products.Search("");
        _refreshingProducts = true;
        ProductGrid.ItemsSource = _products.Search(CatalogSearchBox.Text);
        _refreshingProducts = false;
        InventoryGrid.ItemsSource = list;
        PurchaseProductBox.ItemsSource = list.Where(x => x.ProductType is 1 or 2).ToList();
        AdjustmentProductBox.ItemsSource = list.Where(x => x.ProductType is 1 or 2).ToList();
        BatchProductBox.ItemsSource = list.Where(x => x.ProductType == 4).ToList();
        BatchGrid.ItemsSource = _batches.All();
        RecipeProductBox.ItemsSource = list.Where(x => x.ProductType is 3 or 4).ToList();
        RecipeIngredientBox.ItemsSource = list.Where(x => x.ProductType == 2).ToList();
        SearchProducts(SearchBox.Text);
    }

    private void SearchProducts(string query)
    {
        ProductsWrap.Children.Clear();
        var products = _products.Search(query, true);
        ProductsHint.Text = $"{products.Count} محصول";
        if (products.Count == 0)
            ProductsWrap.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(query) ? "هنوز کالای قابل فروش تعریف نشده است." : "کالایی پیدا نشد؛ نام یا بارکد را بررسی کنید.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8), MaxWidth = 320
            });
        foreach (var product in products)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = product.Name, TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 44, FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock { Text = Money(product.SalePrice), Margin = new Thickness(0, 6, 0, 3) });
            content.Children.Add(new TextBlock { Text = $"قابل فروش: {product.Stock:0.##}", FontSize = 12 });
            var button = new Button
            {
                Width = 174, Height = 122, Margin = new Thickness(5),
                Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Tag = product, Background = Brushes.White,
                BorderBrush = (Brush)FindResource("LineBrush"),
                IsEnabled = product.Stock > 0,
                ToolTip = product.Name + "\n" + (product.Stock > 0 ? "برای افزودن به فاکتور انتخاب کنید." : product.ProductType switch
                {
                    3 => "موجودی مواد اولیه یا دستور تهیه را بررسی کنید.",
                    4 => "بچ دارای موجودی و تاریخ معتبر ثبت کنید.",
                    _ => "موجودی این کالا صفر است. از بخش کالا و انبار آن را ثبت کنید."
                })
            };
            AutomationProperties.SetName(button, $"{product.Name}، {Money(product.SalePrice)}، قابل فروش {product.Stock:0.##}");
            ToolTipService.SetShowOnDisabled(button, true);
            button.Click += Product_Click;
            ProductsWrap.Children.Add(button);
        }
    }

    private void AddProduct(Product product)
    {
        var existing = _cart.FirstOrDefault(x => x.ProductId == product.Id && x.BatchId == product.BatchId);
        var proposed = _cart.Select(x => new CartItem { ProductId = x.ProductId, BatchId = x.BatchId,
            ProductName = x.ProductName, UnitPrice = x.UnitPrice, Quantity = x.Quantity }).ToList();
        var line = proposed.FirstOrDefault(x => x.ProductId == product.Id && x.BatchId == product.BatchId);
        if (line is null) proposed.Add(new CartItem { ProductId = product.Id, BatchId = product.BatchId, Quantity = 1 });
        else line.Quantity++;
        try { _sales.ValidateCartStock(proposed); }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message, true);
            return;
        }
        if (existing is null)
            _cart.Add(new CartItem { ProductId = product.Id, BatchId = product.BatchId,
                ProductName = product.BatchId is null ? product.Name : $"{product.Name} | بچ {product.BatchNumber}",
                UnitPrice = product.SalePrice });
        else existing.Quantity++;
        CartList.Items.Refresh();
        UpdateTotals();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void UpdateTotals()
    {
        var subtotal = _cart.Sum(x => x.Total);
        var code = CouponBox.Text.Trim();
        DiscountBox.IsEnabled = code.Length == 0;
        if (code.Length > 0 && DiscountBox.Text != "0") DiscountBox.Text = "0";
        var discount = TryMoney(DiscountBox.Text, out var parsedDiscount) ? parsedDiscount : -1;
        var couponError = false;
        if (code.Length > 0)
        {
            try
            {
                discount = _discounts.Quote(code, subtotal);
                CouponHint.Text = $"تخفیف کد: {Money(discount)}";
            }
            catch (InvalidOperationException ex)
            {
                couponError = true;
                CouponHint.Text = ex.Message;
            }
        }
        else CouponHint.Text = "";
        var validDiscount = !couponError && discount >= 0 && discount <= subtotal;
        var charges = _charges.Quote(subtotal, validDiscount ? discount : 0,
            TaxApplyBox.IsChecked == true, FeeApplyBox.IsChecked == true);
        var final = validDiscount ? subtotal - discount + charges.Tax + charges.Fee : subtotal;
        SubtotalText.Text = Money(subtotal);
        ChargePreviewText.Text = $"مالیات: {Money(charges.Tax)} | کارمزد: {Money(charges.Fee)}";
        FinalText.Text = validDiscount ? Money(final) : couponError ? "کد تخفیف نامعتبر" : "تخفیف نامعتبر";
        if (!_changingPayment)
        {
            _changingPayment = true;
            var mode = PaymentModeBox.SelectedIndex;
            CardBox.IsReadOnly = TransferBox.IsReadOnly = mode != 3;
            PaymentAmountsPanel.Visibility = mode == 3 ? Visibility.Visible : Visibility.Collapsed;
            if (mode != 3)
            {
                CardBox.Text = (mode == 1 ? final : 0).ToString("0", CultureInfo.InvariantCulture);
                TransferBox.Text = (mode == 2 ? final : 0).ToString("0", CultureInfo.InvariantCulture);
            }
            var card = TryMoney(CardBox.Text, out var parsedCard) ? parsedCard : -1;
            var transfer = TryMoney(TransferBox.Text, out var parsedTransfer) ? parsedTransfer : -1;
            if (card >= 0 && transfer >= 0 && card + transfer <= final)
                CashBox.Text = (final - card - transfer).ToString("0");
            else CashBox.Text = "0";
            PaymentHint.Text = !validDiscount ? "تخفیف را اصلاح کنید." :
                card < 0 || transfer < 0 || card + transfer > final ? "مبلغ پرداخت‌ها نامعتبر است." :
                $"نقدی: {CashBox.Text} | کارتخوان: {card:N0} | انتقال: {transfer:N0} تومان";
            PosDeviceBox.IsEnabled = card > 0;
            TransferBankBox.IsEnabled = transfer > 0;
            _changingPayment = false;
        }
    }

    private void PaymentMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _changingPayment = true;
        CardBox.Text = TransferBox.Text = "0";
        _changingPayment = false;
        UpdateTotals();
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

    private static decimal RequiredQuantity(string? input, string label)
    {
        var normalized = NormalizeQuantity(input);
        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new InvalidOperationException($"{label} باید عدد غیرمنفی باشد.");
        return value;
    }

    private static decimal RequiredSignedQuantity(string? input)
    {
        var normalized = NormalizeQuantity(input);
        if (!decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var value) || value == 0)
            throw new InvalidOperationException("مقدار تغییر موجودی باید عدد مثبت یا منفیِ غیرصفر باشد.");
        return value;
    }

    private static string NormalizeQuantity(string? input) =>
        new string((input ?? "").Trim().Select(c => c switch
        {
            >= '۰' and <= '۹' => (char)('0' + c - '۰'),
            >= '٠' and <= '٩' => (char)('0' + c - '٠'),
            '٫' => '.',
            '٬' or ',' or ' ' or '\u00a0' => '\0',
            _ => c
        }).Where(c => c != '\0').ToArray());

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
        else { SetStatus("بارکد پیدا نشد؛ بارکد و موجودی بچ را بررسی کنید.", true); e.Handled = true; }
    }

    private void DiscountBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) UpdateTotals();
    }
    private void CouponBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) UpdateTotals();
    }
    private void ChargeApply_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) UpdateTotals();
    }

    private void PaymentBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded && !_changingPayment) UpdateTotals();
    }

    private void MobileBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (string.IsNullOrWhiteSpace(MobileBox.Text))
        {
            MobileStatusText.Text = "";
            _registeredMobile = null;
            return;
        }
        if (!CustomerService.TryNormalizeMobile(MobileBox.Text, out var mobile))
        {
            _registeredMobile = null;
            MobileStatusText.Text = "برای ثبت خودکار، شمارهٔ موبایل ۱۱ رقمی را کامل وارد کنید.";
            return;
        }
        if (_registeredMobile == mobile) return;
        try
        {
            _registeredMobile = _customers.EnsureMobile(mobile);
            MobileStatusText.Text = "شماره در مشتریان ثبت شد؛ نام را می‌توانید در بخش مشتریان تکمیل کنید.";
            RefreshCustomers();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void IncreaseItem_Click(object sender, RoutedEventArgs e)
    {
        if (CartList.SelectedItem is not CartItem selected) { SetStatus("ابتدا یک ردیف از جدول انتخاب کنید.", true); return; }
        var batch = selected.BatchId is null ? null : _batches.All().FirstOrDefault(x => x.Id == selected.BatchId);
        var product = batch is not null ? _products.FindByBarcode(batch.Barcode) :
            _products.Search("").FirstOrDefault(x => x.Id == selected.ProductId);
        if (product is not null) AddProduct(product);
    }

    private void DecreaseItem_Click(object sender, RoutedEventArgs e)
    {
        if (CartList.SelectedItem is not CartItem selected) { SetStatus("ابتدا یک ردیف از جدول انتخاب کنید.", true); return; }
        if (selected.Quantity <= 1) _cart.Remove(selected);
        else selected.Quantity--;
        CartList.Items.Refresh();
        UpdateTotals();
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (CartList.SelectedItem is not CartItem selected) { SetStatus("ابتدا یک قلم از فاکتور انتخاب کنید.", true); return; }
        _cart.Remove(selected);
        UpdateTotals();
    }

    private void Checkout_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _sales.ValidateCartStock(_cart.ToList());
            var discount = RequiredMoney(DiscountBox.Text, "تخفیف");
            var card = RequiredMoney(CardBox.Text, "مبلغ کارتخوان");
            var transfer = RequiredMoney(TransferBox.Text, "مبلغ کارت به کارت");
            var cash = RequiredMoney(CashBox.Text, "مبلغ نقدی");
            var saleId = _sales.CreateSale(_cart.ToList(), discount, MobileBox.Text, cash, card,
                transferAmount: transfer,
                bankAccountId: (TransferBankBox.SelectedItem as BankAccount)?.Id,
                posDeviceId: (PosDeviceBox.SelectedItem as PosDevice)?.Id,
                discountCode: CouponBox.Text,
                applyConfiguredTax: TaxApplyBox.IsChecked == true,
                applyConfiguredFee: FeeApplyBox.IsChecked == true);
            _cart.Clear();
            DiscountBox.Text = "0";
            CouponBox.Clear();
            CardBox.Text = "0";
            TransferBox.Text = "0";
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
            var mobile = CustomerService.NormalizeMobile(CustomerMobileBox.Text);
            _customers.Save(CustomerNameBox.Text, CustomerMobileBox.Text);
            CustomerNameBox.Clear(); CustomerMobileBox.Clear();
            CustomerSearchBox.Clear();
            RefreshCustomers(); RefreshDashboard();
            CustomerStatusText.Foreground = (Brush)FindResource("AccentBrush");
            CustomerStatusText.Text = $"مشتری با موبایل {mobile} ذخیره شد.";
        }
        catch (InvalidOperationException ex)
        {
            CustomerStatusText.Foreground = new SolidColorBrush(Color.FromRgb(180, 35, 24));
            CustomerStatusText.Text = ex.Message;
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void CustomerHistory_Click(object sender, RoutedEventArgs e)
    {
        if (CustomerGrid.SelectedItem is not Customer customer) { SetStatus("ابتدا یک ردیف از جدول انتخاب کنید.", true); return; }
        HistorySearchBox.Text = customer.Mobile;
        History_Click(sender, e);
    }

    private void ProductGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingProducts) return;
        if (!IsLoaded || ProductGrid.SelectedItem is not Product product) return;
        _editingProductId = product.Id;
        ProductNameBox.Text = product.Name;
        ProductBarcodeBox.Text = product.Barcode ?? "";
        ProductSkuBox.Text = product.Sku ?? "";
        ProductCategoryBox.SelectedValue = product.CategoryId;
        ProductSaleBox.Text = product.SalePrice.ToString("0");
        ProductCostBox.Text = product.CostPrice.ToString("0");
        ProductMinimumBox.Text = product.MinimumStock.ToString(CultureInfo.InvariantCulture);
        ProductUnitBox.Text = product.UnitName;
        ProductTypeBox.SelectedIndex = product.ProductType - 1;
        ProductStockBox.Text = "0";
        ShowProductEditor(true);
    }
    private void NewProduct_Click(object sender, RoutedEventArgs e) { ClearProductForm(); ShowProductEditor(true); }
    private void ProductList_Click(object sender, RoutedEventArgs e) => ShowProductEditor(false);
    private void ShowProductEditor(bool editing)
    {
        if (!editing) ProductGrid.SelectedItem = null;
        ProductEditor.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        ProductGrid.Visibility = CatalogToolbar.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
    }
    private void CatalogSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _refreshingProducts = true;
        ProductGrid.ItemsSource = _products.Search(CatalogSearchBox.Text);
        _refreshingProducts = false;
    }
    private void ProductType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ProductStockBox is null) return;
        ProductStockBox.IsEnabled = ProductTypeBox.SelectedIndex is 0 or 1;
        if (!ProductStockBox.IsEnabled) ProductStockBox.Text = "0";
    }
    private void RecipeItem_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecipeGrid.SelectedItem is not RecipeItem item) return;
        RecipeIngredientBox.SelectedValue = item.IngredientProductId;
        RecipeQuantityBox.Text = item.Quantity.ToString("0.######", CultureInfo.InvariantCulture);
    }
    private void ClearProductForm()
    {
        _editingProductId = null;
        ProductGrid.SelectedItem = null;
        ProductNameBox.Clear(); ProductBarcodeBox.Clear(); ProductSkuBox.Clear(); ProductCategoryBox.SelectedItem = null;
        ProductSaleBox.Text = "0"; ProductCostBox.Text = "0";
        ProductMinimumBox.Text = "0";
        ProductUnitBox.Text = "عدد";
        ProductTypeBox.SelectedIndex = 0;
        ProductStockBox.Text = "0";
        ProductNameBox.Focus();
    }
    private void SaveProduct_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var productType = ProductTypeBox.SelectedIndex + 1;
            var savedId = _products.Save(_editingProductId, ProductNameBox.Text, ProductBarcodeBox.Text,
                RequiredMoney(ProductSaleBox.Text, "قیمت فروش"),
                RequiredMoney(ProductCostBox.Text, "بهای خرید"),
                RequiredQuantity(ProductMinimumBox.Text, "حداقل موجودی"),
                productType, ProductUnitBox.Text,
                ProductSkuBox.Text, ProductCategoryBox.SelectedValue as long?,
                RequiredQuantity(ProductStockBox.Text, "مقدار ورود موجودی"));
            CatalogSearchBox.Clear();
            RefreshProducts(); RefreshDashboard();
            if (productType is 3 or 4)
            {
                ProductGrid.SelectedItem = ProductGrid.Items.OfType<Product>().FirstOrDefault(x => x.Id == savedId);
                ProductStatusText.Text = productType == 3
                    ? "کالا ذخیره شد. اکنون «تنظیم دستور تهیه» را انجام دهید."
                    : "کالا ذخیره شد. در «بچ و انقضا» موجودی ثبت کنید؛ فقط برای تولید از مواد اولیه، دستور تهیه لازم است.";
            }
            else
            {
                ClearProductForm();
                ShowProductEditor(false);
                SetStatus("کالا و موجودی آن ذخیره شد. بارکد و SKU در جدول نمایش داده می‌شوند.");
            }
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19 && ex.Message.Contains("Products.Barcode"))
        {
            ShowError(ex, "بارکد قبلاً برای محصول دیگری ثبت شده است.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void DeactivateProduct_Click(object sender, RoutedEventArgs e)
    {
        if (ProductGrid.SelectedItem is not Product product) { SetStatus("ابتدا یک ردیف از جدول انتخاب کنید.", true); return; }
        if (MessageBox.Show($"محصول «{product.Name}» غیرفعال شود؟", "تأیید",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _products.Deactivate(product.Id);
        RefreshProducts(); ClearProductForm();
    }
    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var id = _categories.Add(NewCategoryBox.Text);
            ProductCategoryBox.ItemsSource = _categories.All();
            ProductCategoryBox.SelectedValue = id;
            NewCategoryBox.Clear();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void Purchase_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (PurchaseSupplierBox.SelectedItem is not Supplier supplier)
                throw new InvalidOperationException("تأمین‌کننده را انتخاب کنید.");
            var kind = (PurchasePaymentBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Unpaid";
            var bank = kind == "Bank" ? PurchaseBankBox.SelectedValue as long? : null;
            try { _operations.RecordPurchase(_purchaseLines.ToList(), supplier.Id, PurchaseInvoiceBox.Text, kind, bank); }
            catch (DuplicatePurchaseException ex)
            {
                if (MessageBox.Show(this, ex.Message + "\nبا وجود این، یک خرید مستقل دیگر ثبت شود؟", "سند خرید تکراری",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                _operations.RecordPurchase(_purchaseLines.ToList(), supplier.Id, PurchaseInvoiceBox.Text, kind, bank, allowDuplicate: true);
            }
            _purchaseLines.Clear(); PurchaseInvoiceBox.Clear();
            RefreshProducts(); RefreshDashboard();
            PurchaseGrid.ItemsSource = _operations.Purchases();
            SetStatus("سند خرید ثبت شد و موجودی افزایش یافت.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void PayPurchase_Click(object sender, RoutedEventArgs e)
    {
        if (PurchaseGrid.SelectedItem is not PurchaseRecord purchase) { SetStatus("ابتدا یک ردیف از جدول انتخاب کنید.", true); return; }
        try
        {
            var kind = (SettlementKindBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Cash";
            _operations.PayPurchase(purchase.Id,
                RequiredMoney(PurchasePaymentAmountBox.Text, "مبلغ تسویه"), kind,
                kind == "Bank" ? SettlementBankBox.SelectedValue as long? : null);
            PurchasePaymentAmountBox.Clear();
            PurchaseGrid.ItemsSource = _operations.Purchases();
            RefreshDashboard();
            SetStatus("پرداخت خرید ثبت شد و مانده به‌روز شد.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void AddPurchaseLine_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (PurchaseProductBox.SelectedItem is not Product product)
                throw new InvalidOperationException("محصول یا ماده اولیه را انتخاب کنید.");
            if (_purchaseLines.Any(x => x.ProductId == product.Id))
                throw new InvalidOperationException("این محصول در سند هست؛ ابتدا قلم قبلی را حذف کنید.");
            var quantity = RequiredQuantity(PurchaseQuantityBox.Text, "مقدار خرید");
            if (quantity <= 0) throw new InvalidOperationException("مقدار خرید باید مثبت باشد.");
            var unitCost = RequiredMoney(PurchaseCostBox.Text, "بهای واحد");
            if (quantity * unitCost != decimal.Truncate(quantity * unitCost))
                throw new InvalidOperationException("جمع مبلغ قلم باید تومان صحیح باشد.");
            _purchaseLines.Add(new PurchaseLine { ProductId = product.Id,
                ProductName = product.Name, Quantity = quantity, UnitCost = unitCost });
            PurchaseQuantityBox.Clear(); PurchaseCostBox.Clear();
            SetStatus($"قلم به سند افزوده شد؛ {_purchaseLines.Count} قلم، جمع {Money(_purchaseLines.Sum(x => x.Total))}. برای نهایی‌کردن، «ثبت سند خرید» را بزنید.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void RemovePurchaseLine_Click(object sender, RoutedEventArgs e)
    {
        if (PurchaseLinesGrid.SelectedItem is not PurchaseLine line) { SetStatus("ابتدا یک قلم از سند خرید انتخاب کنید.", true); return; }
        _purchaseLines.Remove(line);
        SetStatus($"قلم از سند ثبت‌نشده برداشته شد؛ جمع {Money(_purchaseLines.Sum(x => x.Total))}.");
    }
    private void Adjustment_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (AdjustmentProductBox.SelectedItem is not Product product)
                throw new InvalidOperationException("محصول یا ماده اولیه را انتخاب کنید.");
            _operations.AdjustStock(product.Id, RequiredSignedQuantity(AdjustmentDeltaBox.Text),
                AdjustmentReasonBox.Text);
            AdjustmentDeltaBox.Clear(); AdjustmentReasonBox.Clear();
            RefreshProducts(); RefreshDashboard();
            SetStatus("اصلاح موجودی ثبت شد.");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void RecipeProduct_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) RefreshRecipe();
    }
    private void RefreshRecipe()
    {
        if (RecipeProductBox.SelectedItem is not Product product)
        {
            RecipeGrid.ItemsSource = null;
            RecipeCostText.Text = "محصول تولیدی یا یخچالی را انتخاب کنید.";
            return;
        }
        var items = _recipes.GetItems(product.Id);
        RecipeGrid.ItemsSource = items;
        RecipeCostText.Text = $"بهای مواد هر واحد: {Money(items.Sum(x => x.LineCost))}";
    }
    private void SaveRecipeItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (RecipeProductBox.SelectedItem is not Product product ||
                RecipeIngredientBox.SelectedItem is not Product ingredient)
                throw new InvalidOperationException("محصول و ماده اولیه را انتخاب کنید.");
            var quantity = RequiredQuantity(RecipeQuantityBox.Text, "مقدار ماده اولیه");
            if (quantity <= 0) throw new InvalidOperationException("مقدار ماده اولیه باید مثبت باشد.");
            _recipes.SaveItem(product.Id, ingredient.Id, quantity);
            RecipeQuantityBox.Clear();
            RefreshRecipe();
            SearchProducts(SearchBox.Text);
            RefreshDashboard();
            SetStatus("مادهٔ مصرفی ذخیره شد و تعداد قابل فروش به‌روز شد.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void RemoveRecipeItem_Click(object sender, RoutedEventArgs e)
    {
        if (RecipeGrid.SelectedItem is not RecipeItem item) { SetStatus("ابتدا یک ردیف از جدول انتخاب کنید.", true); return; }
        try
        {
            _recipes.RemoveItem(item.Id);
            RefreshRecipe();
            SearchProducts(SearchBox.Text);
            RefreshDashboard();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void Expense_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var kind = (ExpensePaymentBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Cash";
            _operations.RecordExpense(ExpenseDescriptionBox.Text,
                RequiredMoney(ExpenseAmountBox.Text, "مبلغ هزینه"), kind,
                kind == "Bank" ? ExpenseBankBox.SelectedValue as long? : null,
                ExpenseCategoryBox.SelectedValue as long?);
            ExpenseDescriptionBox.Clear(); ExpenseAmountBox.Clear();
            RefreshDashboard();
            ExpenseGrid.ItemsSource = _operations.Expenses();
            SetStatus("هزینه ثبت شد.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ExportExcel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = "CafeArian-Reports.xlsx" };
        if (dialog.ShowDialog(this) != true) return;
        try { _reports.ExportExcel(dialog.FileName); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void AddExpenseCategory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var id = _expenseCategories.Add(NewExpenseCategoryBox.Text);
            NewExpenseCategoryBox.Clear();
            RefreshExpenseCategories();
            ExpenseCategoryBox.SelectedValue = id;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            ShowError(ex, "این دستهٔ هزینه قبلاً ثبت شده است.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf",
            FileName = "CafeArian-Reports.pdf" };
        if (dialog.ShowDialog(this) != true) return;
        try { _reports.ExportPdf(dialog.FileName); }
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
        if (HistoryGrid.SelectedItem is not SaleRecord sale) { SetStatus("ابتدا یک ردیف از جدول انتخاب کنید.", true); return; }
        try { _receipts.Print(sale.Id); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void CancelSelected_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not SaleRecord sale) { SetStatus("ابتدا فاکتور موردنظر را انتخاب کنید.", true); return; }
        if (sale.Status != "Completed") { SetStatus("این فاکتور قبلاً لغو شده است.", true); return; }
        if (MessageBox.Show($"فاکتور {sale.InvoiceNumber} لغو و موجودی آن برگردانده شود؟ بازپرداخت وجه باید جداگانه انجام شود.",
            "تأیید لغو", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
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
            "تأیید بازیابی", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            _backups.Restore(dialog.FileName);
            _allowClose = true;
            MessageBox.Show("بازیابی انجام شد. برنامه بسته می‌شود؛ آن را دوباره باز کنید و وارد شوید.", "کافه آرین");
            Application.Current.Shutdown();
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
        ShowProductEditor(false);
        ShowPage(ProductsPage, "کالا و موجودی", "تعریف کالا، ورود اولیه و دسترسی به خرید");
    }
    private void ProductRecipe_Click(object sender, RoutedEventArgs e)
    {
        var productId = _editingProductId;
        if (!productId.HasValue || ProductTypeBox.SelectedIndex is not (2 or 3))
        {
            ProductStatusText.Text = "ابتدا کالای آماده‌شونده یا بچ‌دار را ذخیره و از جدول انتخاب کنید.";
            return;
        }
        Recipes_Click(sender, e);
        RecipeProductBox.SelectedValue = productId.Value;
    }
    private void Inventory_Click(object sender, RoutedEventArgs e)
    {
        RefreshProducts();
        ShowPage(InventoryPage, "خرید و موجودی", "ثبت ورود کالا و مشاهده موجودی");
    }
    private async void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(DiagnosticsPage, "عیب‌یابی", "سلامت داده‌ها و خطاهای ثبت‌شده");
        await RefreshDiagnosticsAsync();
    }
    private async void RunDiagnostics_Click(object sender, RoutedEventArgs e) =>
        await RefreshDiagnosticsAsync();

    private async Task RefreshDiagnosticsAsync()
    {
        if (UserSession.Current?.Role != "Admin") return;
        DiagnosticsSummaryText.Text = "در حال بررسی...";
        try
        {
            var checks = await Task.Run(DiagnosticsService.RunChecks);
            DiagnosticsChecksGrid.ItemsSource = checks;
            DiagnosticsErrorsGrid.ItemsSource = DiagnosticsService.RecentErrors();
            var warnings = checks.Count(x => x.Status is "هشدار" or "خطا");
            DiagnosticsSummaryText.Text = $"{checks.Count} بررسی انجام شد؛ {warnings} مورد نیازمند توجه. " +
                $"{DiagnosticsErrorsGrid.Items.Count} خطای اخیر ثبت شده است. مسیر خطاها: {DiagnosticsService.LogFolder}";
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void DiagnosticsErrors_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        DiagnosticsDetailsBox.Text = (DiagnosticsErrorsGrid.SelectedItem as DiagnosticEvent)?.Details ?? "";

    private void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            UserSession.Require("Admin");
            var dialog = new SaveFileDialog { Filter = "Text (*.txt)|*.txt",
                FileName = "cafe-arian-diagnostics.txt",
                InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(Database.DbPath)) };
            if (dialog.ShowDialog() != true) return;
            File.WriteAllText(dialog.FileName, DiagnosticsService.BuildReport(), new UTF8Encoding(false));
            MessageBox.Show("گزارش عیب‌یابی ذخیره شد. پیش از فرستادن آن را مرور کنید.", "کافه آرین");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void OpenDiagnosticsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            UserSession.Require("Admin");
            Directory.CreateDirectory(DiagnosticsService.LogFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = DiagnosticsService.LogFolder,
                UseShellExecute = true
            });
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void Suppliers_Click(object sender, RoutedEventArgs e)
    {
        RefreshSuppliers();
        ShowPage(SuppliersPage, "تأمین‌کنندگان", "اطلاعات تماس و طرف حساب خرید");
    }
    private void AddSupplier_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var id = _suppliers.Add(SupplierNameBox.Text, SupplierMobileBox.Text,
                SupplierCompanyBox.Text, SupplierAddressBox.Text, SupplierNotesBox.Text);
            SupplierNameBox.Clear(); SupplierMobileBox.Clear(); SupplierCompanyBox.Clear();
            SupplierAddressBox.Clear(); SupplierNotesBox.Clear();
            RefreshSuppliers();
            PurchaseSupplierBox.SelectedItem = ((IEnumerable<Supplier>)PurchaseSupplierBox.ItemsSource)
                .FirstOrDefault(x => x.Id == id);
            SetStatus("تأمین‌کننده ثبت شد و برای سند خرید انتخاب شد.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void DeactivateSupplier_Click(object sender, RoutedEventArgs e)
    {
        if (SupplierGrid.SelectedItem is not Supplier supplier) { SetStatus("ابتدا یک ردیف از جدول انتخاب کنید.", true); return; }
        if (MessageBox.Show($"تأمین‌کنندهٔ «{supplier.Name}» غیرفعال شود؟", "تأیید",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { _suppliers.Deactivate(supplier.Id); RefreshSuppliers(); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void Recipes_Click(object sender, RoutedEventArgs e)
    {
        RefreshProducts();
        ShowPage(RecipesPage, "دستور تهیه", "مصرف مواد اولیه و بهای تمام‌شده");
        if (RecipeProductBox.Items.Count > 0) RecipeProductBox.SelectedIndex = 0;
        RefreshRecipe();
    }
    private void Batches_Click(object sender, RoutedEventArgs e)
    {
        RefreshProducts();
        ShowPage(BatchesPage, "بچ و تاریخ انقضا", "ثبت، رهگیری و خروج موجودی یخچالی");
    }
    private void RegisterBatch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (BatchProductBox.SelectedItem is not Product product)
                throw new InvalidOperationException("محصول یخچالی را انتخاب کنید.");
            if (BatchProducedPicker.SelectedDate is not DateTime produced ||
                BatchExpiryPicker.SelectedDate is not DateTime expires)
                throw new InvalidOperationException("تاریخ تولید و انقضا را انتخاب کنید.");
            var quantity = RequiredQuantity(BatchQuantityBox.Text, "مقدار بچ");
            _batches.Register(product.Id, BatchNumberBox.Text, BatchBarcodeBox.Text,
                BatchSourceBox.Text, produced, expires, quantity,
                BatchProductionCheck.IsChecked == true ? 0 : RequiredMoney(BatchCostBox.Text, "بهای واحد"),
                BatchProductionCheck.IsChecked == true);
            BatchNumberBox.Clear(); BatchBarcodeBox.Clear(); BatchQuantityBox.Clear();
            BatchCostBox.Clear();
            RefreshProducts(); RefreshDashboard();
            SetStatus("بچ ثبت شد و موجودی به‌روز شد.");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            ShowError(ex, "شماره یا بارکد این بچ تکراری است.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void DiscardBatch_Click(object sender, RoutedEventArgs e)
    {
        if (BatchGrid.SelectedItem is not ProductBatch batch) { SetStatus("ابتدا بچ موردنظر را انتخاب کنید.", true); return; }
        if (batch.Quantity <= 0) { SetStatus("این بچ موجودی باقی‌مانده ندارد.", true); return; }
        if (MessageBox.Show($"موجودی {batch.Quantity:N2} از بچ «{batch.BatchNumber}» به‌عنوان ضایعات ثبت شود؟",
            "تأیید خروج بچ", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            _batches.Discard(batch.Id);
            RefreshProducts(); RefreshDashboard();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void PrintBatchLabel_Click(object sender, RoutedEventArgs e)
    {
        if (BatchGrid.SelectedItem is not ProductBatch batch) { SetStatus("ابتدا یک ردیف از جدول انتخاب کنید.", true); return; }
        try { _labels.PrintBatch(batch.Id); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void PrintProductLabel_Click(object sender, RoutedEventArgs e)
    {
        if (ProductGrid.SelectedItem is not Product product) { SetStatus("ابتدا یک ردیف از جدول انتخاب کنید.", true); return; }
        try { _labels.PrintProduct(product.Id); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void Customers_Click(object sender, RoutedEventArgs e)
    {
        RefreshCustomers();
        ShowPage(CustomersPage, "مشتریان", "اطلاعات تماس و جمع خرید");
    }
    private void Discounts_Click(object sender, RoutedEventArgs e)
    {
        RefreshDiscounts();
        ShowPage(DiscountsPage, "کدهای تخفیف", "تعریف و کنترل اعتبار کدها");
    }
    private void AddDiscount_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var limit = string.IsNullOrWhiteSpace(DiscountLimitBox.Text) ? (long?)null :
                checked((long)RequiredMoney(DiscountLimitBox.Text, "سقف استفاده"));
            _discounts.Add(DiscountCodeBox.Text,
                DiscountTypeBox.SelectedIndex == 0 ? "Percent" : "Fixed",
                RequiredMoney(DiscountValueBox.Text, "مقدار تخفیف"),
                RequiredMoney(DiscountMinimumBox.Text, "حداقل خرید"),
                DiscountStartPicker.SelectedDate, DiscountEndPicker.SelectedDate, limit);
            DiscountCodeBox.Clear(); DiscountValueBox.Clear(); DiscountLimitBox.Clear();
            RefreshDiscounts();
            SetStatus("کد تخفیف ثبت شد.");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            ShowError(ex, "این کد تخفیف قبلاً ثبت شده است.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void DeactivateDiscount_Click(object sender, RoutedEventArgs e)
    {
        if (DiscountGrid.SelectedItem is not DiscountCode code) { SetStatus("ابتدا یک ردیف از جدول انتخاب کنید.", true); return; }
        if (MessageBox.Show($"کد «{code.Code}» غیرفعال شود؟", "تأیید",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { _discounts.Deactivate(code.Id); RefreshDiscounts(); }
        catch (Exception ex) { ShowError(ex); }
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
    private void Accounts_Click(object sender, RoutedEventArgs e)
    {
        RefreshPaymentAccounts();
        ShowPage(AccountsPage, "حساب‌ها و کارتخوان‌ها", "حساب‌های مقصد و پایانه‌های ثبت‌شده");
    }
    private void AddBank_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _paymentAccounts.AddBank(BankNameBox.Text, BankTitleBox.Text, BankNumberBox.Text, BankCardBox.Text);
            BankNameBox.Clear(); BankTitleBox.Clear(); BankNumberBox.Clear(); BankCardBox.Clear();
            RefreshPaymentAccounts();
            SetStatus("حساب بانکی ثبت شد.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void AddPos_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _paymentAccounts.AddDevice(PosNameBox.Text, (PosBankBox.SelectedItem as BankAccount)?.Id, PosTerminalBox.Text);
            PosNameBox.Clear(); PosTerminalBox.Clear();
            RefreshPaymentAccounts();
            SetStatus("کارتخوان ثبت شد.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void DeactivateBank_Click(object sender, RoutedEventArgs e)
    {
        if (BankGrid.SelectedItem is not BankAccount account) { SetStatus("ابتدا یک ردیف از جدول انتخاب کنید.", true); return; }
        if (MessageBox.Show($"حساب «{account.BankName}» غیرفعال شود؟", "تأیید",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { _paymentAccounts.DeactivateBank(account.Id); RefreshPaymentAccounts(); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void DeactivatePos_Click(object sender, RoutedEventArgs e)
    {
        if (PosGrid.SelectedItem is not PosDevice device) { SetStatus("ابتدا یک ردیف از جدول انتخاب کنید.", true); return; }
        if (MessageBox.Show($"کارتخوان «{device.Name}» غیرفعال شود؟", "تأیید",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { _paymentAccounts.DeactivateDevice(device.Id); RefreshPaymentAccounts(); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void Settings_Click(object sender, RoutedEventArgs e) =>
        ShowPage(SettingsPage, "تنظیمات و پشتیبان‌گیری", "مالیات، کارمزد و نسخه‌های داده");

    private void Users_Click(object sender, RoutedEventArgs e)
    {
        RefreshUsers();
        ShowPage(UsersPage, "کاربران", "مدیر، صندوق‌دار و انباردار");
    }
    private void AddUser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _users.Add(NewUsernameBox.Text, NewUserPasswordBox.Password,
                NewUserRoleBox.SelectedIndex switch { 0 => "Admin", 2 => "Inventory", _ => "Cashier" });
            NewUsernameBox.Clear(); NewUserPasswordBox.Clear(); RefreshUsers();
            SetStatus("کاربر ثبت شد.");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            ShowError(ex, "این نام کاربری قبلاً ثبت شده است.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ResetUserPassword_Click(object sender, RoutedEventArgs e)
    {
        if (UserGrid.SelectedItem is not AppUser user) { SetStatus("ابتدا یک ردیف از جدول انتخاب کنید.", true); return; }
        try
        {
            _users.ResetPassword(user.Id, NewUserPasswordBox.Password);
            NewUserPasswordBox.Clear();
            MessageBox.Show("رمز کاربر بازنشانی شد.", "کافه آرین");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void DeactivateUser_Click(object sender, RoutedEventArgs e)
    {
        if (UserGrid.SelectedItem is not AppUser user) { SetStatus("ابتدا یک ردیف از جدول انتخاب کنید.", true); return; }
        if (MessageBox.Show($"حساب «{user.Username}» غیرفعال شود؟", "تأیید",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { _users.Deactivate(user.Id); RefreshUsers(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void SaveChargeSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _charges.Save(new ChargeSettings
            {
                TaxMode = ChargeTaxModeBox.SelectedIndex switch { 1 => "Percent", 2 => "Fixed", _ => "Disabled" },
                TaxValue = RequiredMoney(ChargeTaxValueBox.Text, "نرخ یا مبلغ مالیات"),
                TaxBase = ChargeTaxBaseBox.SelectedIndex == 1 ? "BeforeDiscount" : "AfterDiscount",
                FeeMode = ChargeFeeModeBox.SelectedIndex switch { 1 => "Percent", 2 => "Fixed", _ => "Disabled" },
                FeeValue = RequiredMoney(ChargeFeeValueBox.Text, "نرخ یا مبلغ کارمزد"),
                FeeBase = ChargeFeeBaseBox.SelectedIndex == 1 ? "BeforeDiscount" : "AfterDiscount",
                RoundingMode = ChargeRoundingBox.SelectedIndex switch { 1 => "Floor", 2 => "Ceiling", _ => "HalfUp" }
            });
            UpdateTotals();
            MessageBox.Show("تنظیمات مالیات و کارمزد ذخیره شد.", "کافه آرین");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void SavePrintSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(NormalizeQuantity(ReceiptWidthBox.Text), NumberStyles.None, CultureInfo.InvariantCulture, out var receiptWidth) ||
                !int.TryParse(NormalizeQuantity(LabelWidthBox.Text), NumberStyles.None, CultureInfo.InvariantCulture, out var labelWidth))
                throw new InvalidOperationException("عرض چاپ را به میلی‌متر و به‌صورت عدد صحیح وارد کنید.");
            _printSettings.Save(new PrintSettings
            {
                ReceiptPrinter = ReceiptPrinterBox.Text,
                LabelPrinter = LabelPrinterBox.Text,
                ReceiptWidthMm = receiptWidth,
                LabelWidthMm = labelWidth
            });
            SetStatus("تنظیمات چاپ ذخیره شد.");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F1: Sales_Click(sender, new RoutedEventArgs()); e.Handled = true; break;
            case Key.F2: Sales_Click(sender, new RoutedEventArgs()); SearchBox.Focus(); e.Handled = true; break;
            case Key.F3: Customers_Click(sender, new RoutedEventArgs()); e.Handled = true; break;
            case Key.F4:
                Sales_Click(sender, new RoutedEventArgs());
                SaleDetailsExpander.IsExpanded = true;
                DiscountBox.Focus(); DiscountBox.BringIntoView();
                e.Handled = true; break;
            case Key.F5 when SalesPage.Visibility == Visibility.Visible:
                Checkout_Click(sender, new RoutedEventArgs()); e.Handled = true; break;
            case Key.Escape when SearchBox.IsKeyboardFocusWithin: SearchBox.Clear(); e.Handled = true; break;
        }
    }
    private void ShowError(Exception ex, string? friendlyMessage = null,
        [CallerMemberName] string operation = "")
    {
        if (ex is InvalidOperationException)
        {
            SetStatus(friendlyMessage ?? ex.Message, true);
            return;
        }
        var label = operation switch
        {
            nameof(SaveProduct_Click) => "ثبت کالا",
            nameof(AddPurchaseLine_Click) => "افزودن قلم خرید",
            nameof(Purchase_Click) => "ثبت خرید",
            nameof(Adjustment_Click) => "اصلاح موجودی",
            nameof(RegisterBatch_Click) => "ثبت بچ",
            nameof(Checkout_Click) => "ثبت فروش",
            nameof(Backup_Click) => "پشتیبان‌گیری",
            nameof(Restore_Click) => "بازیابی پشتیبان",
            nameof(RefreshDiagnosticsAsync) => "عیب‌یابی",
            _ => "عملیات برنامه"
        };
        var entry = DiagnosticsService.Record(ex, label);
        var message = friendlyMessage ?? (ex is InvalidOperationException ? ex.Message : "خطای داخلی رخ داد.");
        MessageBox.Show($"{message}\nشناسه خطا: {entry.Id}\nجزئیات برای مدیر در بخش عیب‌یابی ثبت شد.",
            "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
