namespace CafeArian.Models;

public sealed class SaleRecord
{
    public long Id { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public string Date { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Mobile { get; set; } = "";
    public decimal Amount { get; set; }
    public string Status { get; set; } = "";
}

public sealed class DashboardSnapshot
{
    public decimal TodaySales { get; set; }
    public decimal TodayExpenses { get; set; }
    public decimal TodayGrossProfit { get; set; }
    public decimal TodayInventoryAdjustmentCost { get; set; }
    public long LowStockCount { get; set; }
    public long CustomerCount { get; set; }
    public decimal TodayNetProfit => TodayGrossProfit - TodayExpenses - TodayInventoryAdjustmentCost;
}

public sealed class PurchaseRecord
{
    public long Id { get; set; }
    public string Date { get; set; } = "";
    public string Supplier { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public decimal Total { get; set; }
}

public sealed class ExpenseRecord
{
    public long Id { get; set; }
    public string Date { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
}

public sealed class DailySalesRecord
{
    public string Label { get; set; } = "";
    public decimal Amount { get; set; }
    public double BarHeight { get; set; }
}
