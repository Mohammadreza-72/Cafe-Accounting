namespace CafeArian.Models;

public class Customer
{
    public long Id { get; set; }
    public string FullName { get; set; } = "";
    public string Mobile { get; set; } = "";
    public long TotalOrders { get; set; }
    public decimal TotalPurchase { get; set; }
    public decimal TotalDiscount { get; set; }
}
