namespace CafeArian.Models;

public class Product
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Barcode { get; set; }
    public string Category { get; set; } = "";
    public decimal SalePrice { get; set; }
    public decimal CostPrice { get; set; }
    public decimal AverageCost { get; set; }
    public decimal Stock { get; set; }
    public decimal MinimumStock { get; set; }
    public bool IsActive { get; set; } = true;
    public int ProductType { get; set; } = 1;
    public string UnitName { get; set; } = "عدد";
    public string ProductTypeName => ProductType switch
    {
        2 => "ماده اولیه",
        3 => "آماده‌شونده",
        _ => "کالای فروشی"
    };
}
