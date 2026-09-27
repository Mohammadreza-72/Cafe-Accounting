namespace CafeArian.Models;

public sealed class PurchaseLine
{
    public long ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Total => Quantity * UnitCost;
}
