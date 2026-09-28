using CafeArian.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;

namespace CafeArian.Services;

public sealed class LabelService
{
    private readonly IPrintBackend _printer;

    public LabelService(IPrintBackend? printer = null) =>
        _printer = printer ?? new WindowsPrintBackend();

    public void PrintBatch(long batchId)
    {
        UserSession.Require("Admin", "Inventory");
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT p.Name, p.SalePrice, b.BatchNumber, b.ProducedAt, b.ExpiresAt, b.Barcode, b.ProductId
            FROM ProductBatches b JOIN Products p ON p.Id=b.ProductId WHERE b.Id=$id;
            """;
        cmd.Parameters.AddWithValue("$id", batchId);
        using var row = cmd.ExecuteReader();
        if (!row.Read()) throw new InvalidOperationException("بچ پیدا نشد.");
        var name = row.GetString(0);
        var price = Convert.ToDecimal(row.GetValue(1));
        var batch = row.GetString(2);
        var produced = row.GetString(3);
        var expires = row.GetString(4);
        var barcode = row.IsDBNull(5) ? "" : row.GetString(5);
        var productId = row.GetInt64(6);
        if (barcode.Length == 0) throw new InvalidOperationException("این بچ بارکد ندارد؛ ابتدا بارکد آن را ثبت کنید.");
        row.Close();
        var document = CreateDocument(name, price, barcode);
        Add(document, $"بچ {batch} | تولید {produced} | انقضا {expires}");
        if (_printer.Print(document, $"لیبل {name} - {batch}", PrintJobKind.Label))
            RecordPrint(connection, productId, batchId, barcode);
    }

    public void PrintProduct(long productId)
    {
        UserSession.Require("Admin", "Inventory");
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Name, SalePrice, Barcode FROM Products WHERE Id=$id AND IsActive=1";
        cmd.Parameters.AddWithValue("$id", productId);
        using var row = cmd.ExecuteReader();
        if (!row.Read()) throw new InvalidOperationException("محصول فعال پیدا نشد.");
        var name = row.GetString(0);
        var price = Convert.ToDecimal(row.GetValue(1));
        var barcode = row.IsDBNull(2) ? "" : row.GetString(2);
        if (barcode.Length == 0) throw new InvalidOperationException("این محصول بارکد ندارد؛ ابتدا بارکد آن را ثبت کنید.");
        row.Close();
        if (_printer.Print(CreateDocument(name, price, barcode), $"لیبل {name}", PrintJobKind.Label))
            RecordPrint(connection, productId, null, barcode);
    }

    private static FlowDocument CreateDocument(string name, decimal price, string barcode)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"), FontSize = 11,
            PageWidth = 230, PagePadding = new Thickness(7),
            FlowDirection = FlowDirection.RightToLeft
        };
        Add(document, name, true);
        Add(document, $"{price:N0} تومان");
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions { Width = 440, Height = 85, Margin = 8, PureBarcode = true }
        };
        var pixels = writer.Write(barcode);
        var bitmap = BitmapSource.Create(pixels.Width, pixels.Height, 96, 96,
            PixelFormats.Bgra32, null, pixels.Pixels, pixels.Width * 4);
        bitmap.Freeze();
        document.Blocks.Add(new BlockUIContainer(new Image
        {
            Source = bitmap, Width = 205, Height = 45, Stretch = Stretch.Fill
        }) { Margin = new Thickness(0, 2, 0, 0) });
        Add(document, barcode);
        return document;
    }

    private static void Add(FlowDocument document, string value, bool bold = false)
    {
        var line = new Paragraph(new Run(value)) { Margin = new Thickness(0, 1, 0, 1) };
        if (bold) line.FontWeight = FontWeights.Bold;
        document.Blocks.Add(line);
    }

    private static void RecordPrint(Microsoft.Data.Sqlite.SqliteConnection connection,
        long productId, long? batchId, string barcode)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO BarcodeLabels(ProductId, BatchId, Barcode, PrintCount, PrintedAt)
            VALUES($product, $batch, $barcode, 1, CURRENT_TIMESTAMP)
            ON CONFLICT(Barcode) DO UPDATE SET PrintCount=PrintCount+1, PrintedAt=CURRENT_TIMESTAMP;
            """;
        cmd.Parameters.AddWithValue("$product", productId);
        cmd.Parameters.AddWithValue("$batch", (object?)batchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$barcode", barcode);
        cmd.ExecuteNonQuery();
    }
}
