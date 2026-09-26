using CafeArian.Models;
using CafeArian.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace CafeArian;

public partial class MainWindow : Window
{
    private readonly ProductService _productService = new();
    private readonly SaleService _saleService = new();
    private readonly ObservableCollection<CartItem> _cart = new();
    private readonly DispatcherTimer _timer = new();

    public MainWindow()
    {
        InitializeComponent();
        CartList.ItemsSource = _cart;
        SearchProducts("");

        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
        _timer.Start();

        Loaded += (_, _) => SearchBox.Focus();
    }

    private void SearchProducts(string text)
    {
        ProductsPanel.Children.Clear();

        foreach (var product in _productService.Search(text))
        {
            var button = new Button
            {
                Width = 165,
                Height = 90,
                Content = $"{product.Name}\n{product.SalePrice:N0} تومان",
                Tag = product,
                Margin = new Thickness(6),
                Background = System.Windows.Media.Brushes.White
            };

            button.Click += Product_Click;
            ProductsPanel.Children.Add(button);
        }
    }

    private void Product_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Product product }) return;

        var existing = _cart.FirstOrDefault(x => x.ProductId == product.Id);

        if (existing is not null)
        {
            existing.Quantity++;
            CartList.Items.Refresh();
        }
        else
        {
            _cart.Add(new CartItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = product.SalePrice,
                Quantity = 1
            });
        }

        UpdateTotals();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchProducts(SearchBox.Text);
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var product = _productService.FindByBarcode(SearchBox.Text);
            if (product is not null)
            {
                AddProduct(product);
                SearchBox.Clear();
                e.Handled = true;
            }
        }
    }

    private void AddProduct(Product product)
    {
        var existing = _cart.FirstOrDefault(x => x.ProductId == product.Id);

        if (existing is not null)
            existing.Quantity++;
        else
            _cart.Add(new CartItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = product.SalePrice
            });

        CartList.Items.Refresh();
        UpdateTotals();
    }

    private void DiscountBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateTotals();
    }

    private void UpdateTotals()
    {
        var subtotal = _cart.Sum(x => x.Total);
        var discount = ParseMoney(DiscountBox.Text);
        var final = Math.Max(0, subtotal - discount);

        SubtotalText.Text = $"{subtotal:N0} تومان";
        FinalText.Text = $"{final:N0} تومان";
    }

    private static decimal ParseMoney(string? value)
    {
        return decimal.TryParse(value, out var amount) ? amount : 0;
    }

    private void Checkout_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_cart.Count == 0)
            {
                MessageBox.Show("ابتدا محصولی به فاکتور اضافه کنید.");
                return;
            }

            var subtotal = _cart.Sum(x => x.Total);
            var discount = ParseMoney(DiscountBox.Text);
            var final = Math.Max(0, subtotal - discount);

            // در نسخه بعدی انتخاب نقدی/کارتخوان/ترکیبی در پنجره پرداخت اضافه می‌شود.
            var saleId = _saleService.CreateSale(
                _cart,
                discount,
                customerId: null,
                cashAmount: final,
                cardAmount: 0);

            MessageBox.Show($"فاکتور با موفقیت ثبت شد.\nشماره ثبت داخلی: {saleId}",
                "کافه آرین",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _cart.Clear();
            DiscountBox.Text = "0";
            MobileBox.Clear();
            UpdateTotals();
            SearchBox.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("داشبورد در مرحله بعدی تکمیل می‌شود.");
    }

    private void Sales_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
    }
}
