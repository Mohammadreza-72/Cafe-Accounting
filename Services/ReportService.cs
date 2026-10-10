using CafeArian.Data;
using ClosedXML.Excel;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CafeArian.Services;

public sealed class ReportService
{
    private sealed record Table(string Name, string[] Headers, List<object?[]> Rows, int[]? FractionColumns = null);

    public void ExportExcel(string path)
    {
        UserSession.Require("Admin");
        using var workbook = new XLWorkbook();
        foreach (var table in Tables())
        {
            var sheet = workbook.Worksheets.Add(table.Name);
            sheet.RightToLeft = true;
            for (var column = 0; column < table.Headers.Length; column++)
                sheet.Cell(1, column + 1).Value = table.Headers[column];
            for (var row = 0; row < table.Rows.Count; row++)
                for (var column = 0; column < table.Headers.Length; column++)
                {
                    var value = table.Rows[row][column];
                    var cell = sheet.Cell(row + 2, column + 1);
                    if (value is null or DBNull) continue;
                    if (value is long or int or double or decimal)
                        cell.Value = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    else
                        cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                }
            var heading = sheet.Range(1, 1, 1, table.Headers.Length);
            heading.Style.Font.Bold = true;
            heading.Style.Fill.BackgroundColor = XLColor.FromHtml("#173C36");
            heading.Style.Font.FontColor = XLColor.White;
            sheet.SheetView.FreezeRows(1);
            sheet.Columns().AdjustToContents(12, 45);
        }
        workbook.SaveAs(path);
    }

    public void ExportPdf(string path)
    {
        UserSession.Require("Admin");
        using var pdf = new PdfDocument();
        pdf.Info.Title = "گزارش مدیریتی کافه آرین";
        foreach (var table in Tables())
        {
            var rowCount = Math.Max(1, table.Rows.Count);
            for (var offset = 0; offset < rowCount; offset += 24)
            {
                var page = pdf.AddPage();
                page.Size = PageSize.A4;
                using var graphics = XGraphics.FromPdfPage(page);
                using var png = RenderPage(table, offset);
                using var image = XImage.FromStream(png);
                graphics.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
            }
        }
        pdf.Save(path);
    }

    private static MemoryStream RenderPage(Table table, int offset)
    {
        var root = new StackPanel { Width = 1240, Height = 1754,
            Background = Brushes.White, FlowDirection = FlowDirection.RightToLeft };
        root.Children.Add(new TextBlock { Text = "کافه آرین | گزارش مدیریتی", FontSize = 32,
            FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(23, 60, 54)),
            Margin = new Thickness(48, 50, 48, 16) });
        root.Children.Add(new TextBlock { Text = $"{table.Name} | {DateTime.Now:yyyy/MM/dd HH:mm}",
            FontSize = 23, Margin = new Thickness(48, 0, 48, 28) });
        root.Children.Add(Row(table.Headers.Cast<object?>().ToArray(), table.Headers.Length, true, table.FractionColumns));
        foreach (var values in table.Rows.Skip(offset).Take(24))
            root.Children.Add(Row(values, table.Headers.Length, false, table.FractionColumns));
        if (table.Rows.Count == 0)
            root.Children.Add(new TextBlock { Text = "داده‌ای برای نمایش وجود ندارد.",
                FontSize = 22, Margin = new Thickness(48, 22, 48, 0) });
        root.Measure(new Size(1240, 1754));
        root.Arrange(new Rect(0, 0, 1240, 1754));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1240, 1754, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        return stream;
    }

    private static Border Row(object?[] values, int columns, bool heading, int[]? fractionColumns)
    {
        var grid = new Grid { Width = 1144, Height = heading ? 55 : 48 };
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < columns; i++)
        {
            var cell = new TextBlock { Text = Display(values[i], fractionColumns?.Contains(i) == true),
                FontSize = heading ? 17 : 16, VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(8, 0, 8, 0),
                Foreground = heading ? Brushes.White : Brushes.Black };
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        return new Border { Child = grid, Margin = new Thickness(48, 0, 48, 1),
            Background = heading ? new SolidColorBrush(Color.FromRgb(23, 60, 54)) : Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(224, 228, 226)),
            BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    internal static string Display(object? value, bool fractional = false) => value switch
    {
        null or DBNull => "",
        long or int or double or decimal => Convert.ToDecimal(value).ToString(fractional ? "#,0.######" : "N0", CultureInfo.GetCultureInfo("fa-IR")),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };

    private static List<Table> Tables()
    {
        using var connection = Database.OpenConnection();
        Table Query(string name, string[] headers, string sql)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();
            var rows = new List<object?[]>();
            while (reader.Read())
            {
                var row = new object?[headers.Length];
                for (var i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }
            return new Table(name, headers, rows);
        }
        return
        [
            Query("فروش", ["فاکتور", "تاریخ", "موبایل مشتری", "جمع", "تخفیف", "مالیات", "کارمزد", "نهایی", "وضعیت"],
                "SELECT s.InvoiceNumber,s.SaleDate,COALESCE(c.Mobile,''),s.SubTotal,s.DiscountAmount,s.TaxAmount,s.FeeAmount,s.FinalAmount,s.Status FROM Sales s LEFT JOIN Customers c ON c.Id=s.CustomerId ORDER BY s.Id DESC"),
            Query("خرید", ["شماره", "تاریخ", "تأمین‌کننده", "فاکتور", "جمع", "پرداخت‌شده", "باقی‌مانده"],
                "SELECT p.Id,p.CreatedAt,p.SupplierName,COALESCE(p.InvoiceNumber,''),p.TotalAmount,COALESCE(SUM(pay.Amount),0),p.TotalAmount-COALESCE(SUM(pay.Amount),0) FROM Purchases p LEFT JOIN PurchasePayments pay ON pay.PurchaseId=p.Id GROUP BY p.Id ORDER BY p.Id DESC"),
            Query("هزینه", ["شماره", "تاریخ", "دسته", "شرح", "مبلغ", "روش پرداخت"],
                "SELECT e.Id,e.CreatedAt,COALESCE(c.Name,''),e.Description,e.Amount,e.PaymentKind FROM Expenses e LEFT JOIN ExpenseCategories c ON c.Id=e.CategoryId ORDER BY e.Id DESC"),
            new Table("موجودی", ["محصول", "نوع", "واحد", "ثبت‌شده", "قابل فروش", "میانگین بها", "SKU", "بارکد"],
                new ProductService().Search("").Select(p => new object?[]
                    { p.Name, p.ProductTypeName, p.UnitName, p.OnHand, p.IsUnlimitedStock ? (object)p.StockDisplay : p.Stock, p.AverageCost, p.Sku, p.Barcode }).ToList(), [3,4,5]),
            Query("مشتریان", ["نام", "موبایل", "تعداد سفارش", "خرید", "تخفیف"],
                "SELECT FullName,Mobile,TotalOrders,TotalPurchase,TotalDiscount FROM Customers ORDER BY Id DESC"),
            Query("پرداخت", ["فاکتور", "تاریخ", "روش", "مبلغ اولیه", "وضعیت فاکتور", "خالص حسابداری", "بانک", "کارتخوان"],
                "SELECT s.InvoiceNumber,p.CreatedAt,m.Name,p.Amount,CASE WHEN s.Status='Completed' THEN 'نهایی' ELSE 'لغوشده؛ بازپرداخت دستی' END,CASE WHEN s.Status='Completed' THEN p.Amount ELSE 0 END,COALESCE(b.BankName,''),COALESCE(pos.Name,'') FROM Payments p JOIN Sales s ON s.Id=p.SaleId JOIN PaymentMethods m ON m.Id=p.PaymentMethodId LEFT JOIN BankAccounts b ON b.Id=p.BankAccountId LEFT JOIN POSDevices pos ON pos.Id=p.PosDeviceId ORDER BY p.Id DESC"),
            Query("سود و زیان", ["روز", "فروش بدون مالیات", "بهای تمام‌شده", "هزینه", "اثر اصلاح موجودی", "سود تخمینی"],
                """
                WITH dates AS (
                    SELECT date(SaleDate,'localtime') day FROM Sales
                    UNION SELECT date(CreatedAt,'localtime') FROM Expenses
                    UNION SELECT date(CreatedAt,'localtime') FROM InventoryTransactions WHERE TransactionType IN ('Adjustment','BatchDisposal')
                ), totals AS (
                    SELECT day,
                      COALESCE((SELECT SUM(FinalAmount-TaxAmount) FROM Sales s WHERE s.Status='Completed' AND date(s.SaleDate,'localtime')=day),0) revenue,
                      COALESCE((SELECT SUM(si.CostPrice*si.Quantity) FROM SaleItems si JOIN Sales s ON s.Id=si.SaleId WHERE s.Status='Completed' AND date(s.SaleDate,'localtime')=day),0) cost,
                      COALESCE((SELECT SUM(Amount) FROM Expenses e WHERE date(e.CreatedAt,'localtime')=day),0) expense,
                      COALESCE((SELECT -SUM(Quantity*UnitCost) FROM InventoryTransactions it WHERE it.TransactionType IN ('Adjustment','BatchDisposal') AND date(it.CreatedAt,'localtime')=day),0) adjustment
                    FROM dates
                )
                SELECT day,revenue,cost,expense,adjustment,revenue-cost-expense-adjustment FROM totals ORDER BY day DESC;
                """)
        ];
    }
}
