namespace CafeArian.Models;

public sealed class DiscountCode
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Type { get; set; } = "";
    public string TypeName => Type == "Percent" ? "درصدی" : "مبلغ ثابت";
    public decimal Value { get; set; }
    public decimal MinimumPurchase { get; set; }
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public long? UsageLimit { get; set; }
    public long UsedCount { get; set; }
    public bool IsActive { get; set; }
    public string Status => IsActive ? "فعال" : "غیرفعال";
}
