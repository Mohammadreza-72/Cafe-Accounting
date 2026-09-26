namespace CafeArian.Models;

public class CartItem
{
    public long ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal Total => UnitPrice * Quantity;
}
