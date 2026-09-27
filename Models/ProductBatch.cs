namespace CafeArian.Models;

public sealed class ProductBatch
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string BatchNumber { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string Source { get; set; } = "";
    public string ProducedAt { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public string Status => DateTime.TryParseExact(ExpiresAt, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.None, out var expiry) && expiry.Date < DateTime.Today
        ? "منقضی" : Quantity <= 0 ? "تمام‌شده" : "قابل فروش";
}
