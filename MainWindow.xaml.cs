using CafeArian.Data;
using CafeArian.Models;
using CafeArian.Services;
using Microsoft.Data.Sqlite;
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
    private readonly RecipeService _recipes = new();
    private readonly BatchService _batches = new();
    private readonly PaymentAccountService _paymentAccounts = new();
    private readonly DiscountService _discounts = new();
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
        var pages = new UIElement[] { DashboardPage, SalesPage, ProductsPage, RecipesPage, BatchesPage, InventoryPage,
            CustomersPage, DiscountsPage, HistoryPage, FinancePage, AccountsPage, SettingsPage };
        foreach (var item in pages)
            item.Visibility = item == page ? Visibility.Visible : Visibility.Collapsed;
        var navigation = new[] { DashboardNav, SalesNav, ProductsNav, RecipesNav, BatchesNav, InventoryNav,
            CustomersNav, DiscountsNav, HistoryNav, FinanceNav, AccountsNav, SettingsNav };
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
        RefreshDiscounts();
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
        PosDeviceBox.ItemsSource = devices;
    }

    private void RefreshDiscounts() => DiscountGrid.ItemsSource = _discounts.All();

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
            $"سود ناخالص تخمینی: {Money(summary.TodayGrossProfit)}\n" +
            $"اثر اصلاح موجودی: {Money(summary.TodayInventoryAdjustmentCost)}\n" +
            $"سود خالص تخمینی: {Money(summary.TodayNetProfit)}\n" +
            string.Join("\n", _operations.TodayPayments().Select(x => $"{x.Key}: {Money(x.Value)}"));
    }

    private void RefreshProducts()
    {
        var list = _products.Search("");
        ProductGrid.ItemsSource = list;
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
        var existing = _cart.FirstOrDefault(x => x.ProductId == product.Id && x.BatchId == product.BatchId);
        var totalInCart = _cart.Where(x => x.ProductId == product.Id).Sum(x => x.Quantity);
        var available = product.BatchId is null ? product.Stock :
            _products.Search("").First(x => x.Id == product.Id).Stock;
        if ((existing?.Quantity ?? 0) + 1 > product.Stock || totalInCart + 1 > available)
        {
            MessageBox.Show("موجودی این محصول کافی نیست.", "کافه آرین");
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
        var final = validDiscount ? subtotal - discount : subtotal;
        SubtotalText.Text = Money(subtotal);
        FinalText.Text = validDiscount ? Money(final) : couponError ? "کد تخفیف نامعتبر" : "تخفیف نامعتبر";
        if (!_changingPayment)
        {
            _changingPayment = true;
            var card = TryMoney(CardBox.Text, out var parsedCard) ? parsedCard : -1;
            var transfer = TryMoney(TransferBox.Text, out var parsedTransfer) ? parsedTransfer : -1;
            if (card >= 0 && transfer >= 0 && card + transfer <= final)
                CashBox.Text = (final - card - transfer).ToString("0");
            else CashBox.Text = "0";
            PaymentHint.Text = !validDiscount ? "تخفیف را اصلاح کنید." :
                card < 0 || transfer < 0 || card + transfer > final ? "مبلغ پرداخت‌ها نامعتبر است." :
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

    private static decimal RequiredQuantity(string? input, string label)
    {
        var normalized = new string((input ?? "").Trim().Select(c => c switch
        {
            >= '۰' and <= '۹' => (char)('0' + c - '۰'),
            >= '٠' and <= '٩' => (char)('0' + c - '٠'),
            '٫' => '.',
            _ => c
        }).ToArray());
        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new InvalidOperationException($"{label} باید عدد غیرمنفی باشد.");
        return value;
    }

    private static decimal RequiredSignedQuantity(string? input)
    {
        var normalized = new string((input ?? "").Trim().Select(c => c switch
        {
            >= '۰' and <= '۹' => (char)('0' + c - '۰'),
            >= '٠' and <= '٩' => (char)('0' + c - '٠'),
            '٫' => '.',
            _ => c
        }).ToArray());
        if (!decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var value) || value == 0)
            throw new InvalidOperationException("مقدار تغییر موجودی باید عدد مثبت یا منفیِ غیرصفر باشد.");
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
    private void CouponBox_TextChanged(object sender, TextChangedEventArgs e)
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
        var batch = selected.BatchId is null ? null : _batches.All().FirstOrDefault(x => x.Id == selected.BatchId);
        var product = batch is not null ? _products.FindByBarcode(batch.Barcode) :
            _products.Search("").FirstOrDefault(x => x.Id == selected.ProductId);
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
            var transfer = RequiredMoney(TransferBox.Text, "مبلغ کارت به کارت");
            var cash = RequiredMoney(CashBox.Text, "مبلغ نقدی");
            var saleId = _sales.CreateSale(_cart.ToList(), discount, MobileBox.Text, cash, card,
                transferAmount: transfer,
                bankAccountId: (TransferBankBox.SelectedItem as BankAccount)?.Id,
                posDeviceId: (PosDeviceBox.SelectedItem as PosDevice)?.Id,
                discountCode: CouponBox.Text);
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
        ProductUnitBox.Text = product.UnitName;
        ProductTypeBox.SelectedIndex = product.ProductType - 1;
    }
    private void NewProduct_Click(object sender, RoutedEventArgs e) => ClearProductForm();
    private void ClearProductForm()
    {
        _editingProductId = null;
        ProductGrid.SelectedItem = null;
        ProductNameBox.Clear(); ProductBarcodeBox.Clear(); ProductSaleBox.Clear(); ProductCostBox.Clear();
        ProductMinimumBox.Text = "0";
        ProductUnitBox.Text = "عدد";
        ProductTypeBox.SelectedIndex = 0;
        ProductNameBox.Focus();
    }
    private void SaveProduct_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _products.Save(_editingProductId, ProductNameBox.Text, ProductBarcodeBox.Text,
                RequiredMoney(ProductSaleBox.Text, "قیمت فروش"),
                RequiredMoney(ProductCostBox.Text, "بهای خرید"),
                RequiredQuantity(ProductMinimumBox.Text, "حداقل موجودی"),
                ProductTypeBox.SelectedIndex + 1, ProductUnitBox.Text);
            RefreshProducts(); RefreshDashboard(); ClearProductForm();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            MessageBox.Show("بارکد قبلاً برای محصول دیگری ثبت شده است.", "خطا",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
            var quantity = RequiredQuantity(PurchaseQuantityBox.Text, "مقدار خرید");
            if (quantity <= 0) throw new InvalidOperationException("مقدار خرید باید مثبت باشد.");
            _operations.RecordPurchase(product.Id, SupplierBox.Text, PurchaseInvoiceBox.Text,
                quantity, RequiredMoney(PurchaseCostBox.Text, "قیمت واحد"));
            PurchaseQuantityBox.Clear(); PurchaseCostBox.Clear(); PurchaseInvoiceBox.Clear();
            RefreshProducts(); RefreshDashboard();
            PurchaseGrid.ItemsSource = _operations.Purchases();
        }
        catch (Exception ex) { ShowError(ex); }
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
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void RemoveRecipeItem_Click(object sender, RoutedEventArgs e)
    {
        if (RecipeGrid.SelectedItem is not RecipeItem item) return;
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
            Database.Initialize();
            SeedData.Initialize();
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
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            MessageBox.Show("شماره یا بارکد این بچ تکراری است.", "کافه آرین",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void DiscardBatch_Click(object sender, RoutedEventArgs e)
    {
        if (BatchGrid.SelectedItem is not ProductBatch batch || batch.Quantity <= 0) return;
        if (MessageBox.Show($"موجودی {batch.Quantity:N2} از بچ «{batch.BatchNumber}» به‌عنوان ضایعات ثبت شود؟",
            "تأیید خروج بچ", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            _batches.Discard(batch.Id);
            RefreshProducts(); RefreshDashboard();
        }
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
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            MessageBox.Show("این کد تخفیف قبلاً ثبت شده است.", "کافه آرین",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void DeactivateDiscount_Click(object sender, RoutedEventArgs e)
    {
        if (DiscountGrid.SelectedItem is not DiscountCode code) return;
        if (MessageBox.Show($"کد «{code.Code}» غیرفعال شود؟", "تأیید",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
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
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void DeactivateBank_Click(object sender, RoutedEventArgs e)
    {
        if (BankGrid.SelectedItem is not BankAccount account) return;
        if (MessageBox.Show($"حساب «{account.BankName}» غیرفعال شود؟", "تأیید",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { _paymentAccounts.DeactivateBank(account.Id); RefreshPaymentAccounts(); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void DeactivatePos_Click(object sender, RoutedEventArgs e)
    {
        if (PosGrid.SelectedItem is not PosDevice device) return;
        if (MessageBox.Show($"کارتخوان «{device.Name}» غیرفعال شود؟", "تأیید",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { _paymentAccounts.DeactivateDevice(device.Id); RefreshPaymentAccounts(); }
        catch (Exception ex) { ShowError(ex); }
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
