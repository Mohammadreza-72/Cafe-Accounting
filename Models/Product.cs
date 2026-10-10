namespace CafeArian.Models;

public class Product
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Barcode { get; set; }
    public string? Sku { get; set; }
    public long? CategoryId { get; set; }
    public string Category { get; set; } = "";
    public decimal SalePrice { get; set; }
    public decimal CostPrice { get; set; }
    public decimal AverageCost { get; set; }
    public decimal OnHand { get; set; }
    public decimal Stock { get; set; }
    public bool IsUnlimitedStock { get; set; }
    public string StockDisplay => IsUnlimitedStock ? "بدون محدودیت مواد" : Stock.ToString("0.######");
    public decimal MinimumStock { get; set; }
    public bool IsActive { get; set; } = true;
    public int ProductType { get; set; } = 1;
    public string UnitName { get; set; } = "عدد";
    public long? BatchId { get; set; }
    public string BatchNumber { get; set; } = "";
    public string ProductTypeName => ProductType switch
    {
        2 => "ماده اولیه",
        3 => "آماده‌شونده",
        4 => "یخچالی",
        _ => "کالای فروشی"
    };
}
