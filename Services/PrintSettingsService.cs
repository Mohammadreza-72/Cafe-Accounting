using CafeArian.Data;
using CafeArian.Models;

namespace CafeArian.Services;

public sealed class PrintSettingsService
{
    public PrintSettings Load()
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ReceiptPrinter,LabelPrinter,ReceiptWidthMm,LabelWidthMm FROM PrintSettings WHERE Id=1";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("تنظیمات چاپ پیدا نشد.");
        return new PrintSettings { ReceiptPrinter = reader.GetString(0), LabelPrinter = reader.GetString(1),
            ReceiptWidthMm = reader.GetInt32(2), LabelWidthMm = reader.GetInt32(3) };
    }

    public void Save(PrintSettings settings)
    {
        UserSession.Require("Admin");
        if (settings.ReceiptWidthMm is < 40 or > 120 || settings.LabelWidthMm is < 25 or > 120 ||
            settings.ReceiptPrinter.Length > 200 || settings.LabelPrinter.Length > 200)
            throw new InvalidOperationException("عرض یا نام چاپگر معتبر نیست.");
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE PrintSettings SET ReceiptPrinter=$receipt,LabelPrinter=$label,
                ReceiptWidthMm=$receiptWidth,LabelWidthMm=$labelWidth WHERE Id=1;
            """;
        cmd.Parameters.AddWithValue("$receipt", settings.ReceiptPrinter.Trim());
        cmd.Parameters.AddWithValue("$label", settings.LabelPrinter.Trim());
        cmd.Parameters.AddWithValue("$receiptWidth", settings.ReceiptWidthMm);
        cmd.Parameters.AddWithValue("$labelWidth", settings.LabelWidthMm);
        cmd.ExecuteNonQuery();
    }
}
