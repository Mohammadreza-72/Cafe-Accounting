using CafeArian.Data;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace CafeArian.Services;

public sealed class ReceiptService
{
    private readonly IPrintBackend _printer;

    public ReceiptService(IPrintBackend? printer = null) =>
        _printer = printer ?? new WindowsPrintBackend();

    public void Print(long saleId)
    {
        using var connection = Database.OpenConnection();
        using var sale = connection.CreateCommand();
        sale.CommandText = """
            SELECT InvoiceNumber, SaleDate, SubTotal, DiscountAmount, TaxAmount, FeeAmount, FinalAmount, Status
            FROM Sales WHERE Id = $id;
            """;
        sale.Parameters.AddWithValue("$id", saleId);
        string number, date, status;
        decimal subtotal, discount, tax, fee, final;
        using (var reader = sale.ExecuteReader())
        {
            if (!reader.Read()) throw new InvalidOperationException("فاکتور پیدا نشد.");
            number = reader.GetString(0);
            date = reader.GetString(1);
            subtotal = Convert.ToDecimal(reader.GetValue(2));
            discount = Convert.ToDecimal(reader.GetValue(3));
            tax = Convert.ToDecimal(reader.GetValue(4));
            fee = Convert.ToDecimal(reader.GetValue(5));
            final = Convert.ToDecimal(reader.GetValue(6));
            status = reader.GetString(7);
        }
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            PageWidth = 300,
            PagePadding = new Thickness(14),
            FlowDirection = FlowDirection.RightToLeft
        };
        Add(document, "کافه آرین", true);
        Add(document, $"فاکتور {number} | {date}");
        if (status != "Completed") Add(document, "این فاکتور لغو شده است");
        using (var items = connection.CreateCommand())
        {
            items.CommandText = """
                SELECT p.Name, si.Quantity, si.TotalPrice FROM SaleItems si
                JOIN Products p ON p.Id=si.ProductId WHERE si.SaleId=$id;
                """;
            items.Parameters.AddWithValue("$id", saleId);
            using var reader = items.ExecuteReader();
            while (reader.Read())
                Add(document, $"{reader.GetString(0)} × {reader.GetValue(1)}    {Convert.ToDecimal(reader.GetValue(2)):N0}");
        }
        Add(document, $"جمع: {subtotal:N0} تومان");
        Add(document, $"تخفیف: {discount:N0} تومان");
        if (tax > 0) Add(document, $"مالیات: {tax:N0} تومان");
        if (fee > 0) Add(document, $"کارمزد: {fee:N0} تومان");
        Add(document, $"پرداختی: {final:N0} تومان", true);
        using (var payments = connection.CreateCommand())
        {
            payments.CommandText = """
                SELECT m.Name, p.Amount FROM Payments p JOIN PaymentMethods m ON m.Id=p.PaymentMethodId
                WHERE p.SaleId=$id;
                """;
            payments.Parameters.AddWithValue("$id", saleId);
            using var reader = payments.ExecuteReader();
            while (reader.Read()) Add(document, $"{reader.GetString(0)}: {Convert.ToDecimal(reader.GetValue(1)):N0}");
        }
        Add(document, "از خرید شما سپاسگزاریم");
        _printer.Print(document, $"فاکتور {number}", PrintJobKind.Receipt);
    }

    private static void Add(FlowDocument document, string text, bool bold = false)
    {
        var paragraph = new Paragraph(new Run(text)) { Margin = new Thickness(0, 3, 0, 3) };
        if (bold) paragraph.FontWeight = FontWeights.Bold;
        document.Blocks.Add(paragraph);
    }
}
