using CafeArian.Data;
using CafeArian.Models;

namespace CafeArian.Services;

public sealed class OperationsService
{
    public void RecordPurchase(long productId, string supplier, string? invoice, decimal quantity, decimal unitCost)
    {
        if (quantity <= 0 || unitCost < 0 || unitCost != decimal.Truncate(unitCost) ||
            string.IsNullOrWhiteSpace(supplier))
            throw new InvalidOperationException("اطلاعات خرید معتبر نیست.");
        var total = quantity * unitCost;
        if (total != decimal.Truncate(total)) throw new InvalidOperationException("مبلغ خرید باید عدد صحیح باشد.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var purchase = connection.CreateCommand();
        purchase.Transaction = transaction;
        purchase.CommandText = """
            INSERT INTO Purchases(SupplierName, InvoiceNumber, TotalAmount) VALUES($supplier, $invoice, $total);
            SELECT last_insert_rowid();
            """;
        purchase.Parameters.AddWithValue("$supplier", supplier.Trim());
        purchase.Parameters.AddWithValue("$invoice", (object?)invoice?.Trim() ?? DBNull.Value);
        purchase.Parameters.AddWithValue("$total", (long)total);
        var purchaseId = Convert.ToInt64(purchase.ExecuteScalar());
        using (var item = connection.CreateCommand())
        {
            item.Transaction = transaction;
            item.CommandText = """
                INSERT INTO PurchaseItems(PurchaseId, ProductId, Quantity, UnitCost)
                SELECT $purchase, Id, $qty, $cost FROM Products WHERE Id = $product AND IsActive = 1;
                """;
            item.Parameters.AddWithValue("$purchase", purchaseId);
            item.Parameters.AddWithValue("$product", productId);
            item.Parameters.AddWithValue("$qty", Convert.ToDouble(quantity));
            item.Parameters.AddWithValue("$cost", (long)unitCost);
            if (item.ExecuteNonQuery() != 1) throw new InvalidOperationException("محصول فعال پیدا نشد.");
        }
        using (var stock = connection.CreateCommand())
        {
            stock.Transaction = transaction;
            stock.CommandText = """
                INSERT INTO Inventory(ProductId, Quantity, AverageCost) VALUES($product, $qty, $cost)
                ON CONFLICT(ProductId) DO UPDATE SET
                  AverageCost = CASE WHEN Inventory.Quantity + $qty = 0 THEN $cost
                    ELSE (Inventory.Quantity * Inventory.AverageCost + $qty * $cost) / (Inventory.Quantity + $qty) END,
                  Quantity = Inventory.Quantity + $qty, UpdatedAt = CURRENT_TIMESTAMP;
                """;
            stock.Parameters.AddWithValue("$product", productId);
            stock.Parameters.AddWithValue("$qty", Convert.ToDouble(quantity));
            stock.Parameters.AddWithValue("$cost", (long)unitCost);
            stock.ExecuteNonQuery();
        }
        using (var movement = connection.CreateCommand())
        {
            movement.Transaction = transaction;
            movement.CommandText = """
                INSERT INTO InventoryTransactions(ProductId, TransactionType, Quantity, UnitCost, ReferenceType, ReferenceId)
                VALUES($product, 'Purchase', $qty, $cost, 'Purchase', $purchase);
                """;
            movement.Parameters.AddWithValue("$product", productId);
            movement.Parameters.AddWithValue("$qty", Convert.ToDouble(quantity));
            movement.Parameters.AddWithValue("$cost", (long)unitCost);
            movement.Parameters.AddWithValue("$purchase", purchaseId);
            movement.ExecuteNonQuery();
        }
        using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = "INSERT INTO AuditLog(Action, ReferenceType, ReferenceId) VALUES('Purchase','Purchase',$id)";
            audit.Parameters.AddWithValue("$id", purchaseId);
            audit.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void RecordExpense(string description, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(description) || amount <= 0 || amount != decimal.Truncate(amount))
            throw new InvalidOperationException("شرح یا مبلغ هزینه معتبر نیست.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "INSERT INTO Expenses(Description, Amount) VALUES($description, $amount); SELECT last_insert_rowid()";
        cmd.Parameters.AddWithValue("$description", description.Trim());
        cmd.Parameters.AddWithValue("$amount", (long)amount);
        var expenseId = Convert.ToInt64(cmd.ExecuteScalar());
        using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = "INSERT INTO AuditLog(Action, ReferenceType, ReferenceId) VALUES('Expense','Expense',$id)";
        audit.Parameters.AddWithValue("$id", expenseId);
        audit.ExecuteNonQuery();
        transaction.Commit();
    }

    public void AdjustStock(long productId, decimal delta, string reason)
    {
        if (delta == 0 || string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("مقدار تغییر و علت اصلاح موجودی را وارد کنید.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Inventory SET Quantity=Quantity+$delta, UpdatedAt=CURRENT_TIMESTAMP
                WHERE ProductId=$product AND Quantity+$delta>=0
                  AND EXISTS(SELECT 1 FROM Products WHERE Id=$product AND IsActive=1 AND ProductType!=3);
                """;
            update.Parameters.AddWithValue("$delta", Convert.ToDouble(delta));
            update.Parameters.AddWithValue("$product", productId);
            if (update.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("محصول قابل‌اصلاح نیست یا موجودی کافی نیست.");
        }
        using (var movement = connection.CreateCommand())
        {
            movement.Transaction = transaction;
            movement.CommandText = """
                INSERT INTO InventoryTransactions(ProductId, TransactionType, Quantity, UnitCost, ReferenceType)
                SELECT $product, 'Adjustment', $delta, AverageCost, 'Manual'
                FROM Inventory WHERE ProductId=$product;
                """;
            movement.Parameters.AddWithValue("$product", productId);
            movement.Parameters.AddWithValue("$delta", Convert.ToDouble(delta));
            movement.ExecuteNonQuery();
        }
        using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = """
                INSERT INTO AuditLog(Action, ReferenceType, ReferenceId, Details)
                VALUES('StockAdjustment','Product',$product,$details);
                """;
            audit.Parameters.AddWithValue("$product", productId);
            audit.Parameters.AddWithValue("$details", $"تغییر {delta}؛ علت: {reason.Trim()}");
            audit.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public List<SaleRecord> Sales(string? filter = null, int limit = 500)
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.Id, s.InvoiceNumber, s.SaleDate, COALESCE(c.FullName, c.Mobile, ''),
                   s.FinalAmount, s.Status, COALESCE(c.Mobile,'')
            FROM Sales s LEFT JOIN Customers c ON c.Id = s.CustomerId
            WHERE $filter = '' OR s.InvoiceNumber LIKE '%' || $filter || '%'
              OR c.Mobile LIKE '%' || $filter || '%' OR c.FullName LIKE '%' || $filter || '%'
              OR s.SaleDate LIKE '%' || $filter || '%' OR CAST(s.FinalAmount AS TEXT) LIKE '%' || $filter || '%'
            ORDER BY s.Id DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$filter", filter?.Trim() ?? "");
        cmd.Parameters.AddWithValue("$limit", limit);
        using var reader = cmd.ExecuteReader();
        var result = new List<SaleRecord>();
        while (reader.Read())
            result.Add(new SaleRecord
            {
                Id = reader.GetInt64(0), InvoiceNumber = reader.GetString(1),
                Date = reader.GetString(2), Customer = reader.GetString(3),
                Amount = Convert.ToDecimal(reader.GetValue(4)), Status = reader.GetString(5),
                Mobile = reader.GetString(6)
            });
        return result;
    }

    public List<PurchaseRecord> Purchases()
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, CreatedAt, SupplierName, COALESCE(InvoiceNumber,''), TotalAmount FROM Purchases ORDER BY Id DESC";
        using var reader = cmd.ExecuteReader();
        var result = new List<PurchaseRecord>();
        while (reader.Read())
            result.Add(new PurchaseRecord { Id = reader.GetInt64(0), Date = reader.GetString(1),
                Supplier = reader.GetString(2), InvoiceNumber = reader.GetString(3),
                Total = Convert.ToDecimal(reader.GetValue(4)) });
        return result;
    }

    public List<ExpenseRecord> Expenses()
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, CreatedAt, Description, Amount FROM Expenses ORDER BY Id DESC";
        using var reader = cmd.ExecuteReader();
        var result = new List<ExpenseRecord>();
        while (reader.Read())
            result.Add(new ExpenseRecord { Id = reader.GetInt64(0), Date = reader.GetString(1),
                Description = reader.GetString(2), Amount = Convert.ToDecimal(reader.GetValue(3)) });
        return result;
    }

    public DashboardSnapshot Dashboard()
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT
              COALESCE((SELECT SUM(FinalAmount) FROM Sales WHERE Status='Completed' AND date(SaleDate,'localtime')=date('now','localtime')),0),
              COALESCE((SELECT SUM(Amount) FROM Expenses WHERE date(CreatedAt,'localtime')=date('now','localtime')),0),
              COALESCE((SELECT SUM(s.FinalAmount-s.TaxAmount-s.FeeAmount) FROM Sales s WHERE s.Status='Completed'
                AND date(s.SaleDate,'localtime')=date('now','localtime')),0)
              - COALESCE((SELECT SUM(si.CostPrice*si.Quantity) FROM SaleItems si
                JOIN Sales s ON s.Id=si.SaleId WHERE s.Status='Completed' AND date(s.SaleDate,'localtime')=date('now','localtime')),0),
              (SELECT COUNT(*) FROM Inventory i JOIN Products p ON p.Id=i.ProductId
                WHERE p.IsActive=1 AND i.Quantity<=p.MinimumStock),
              (SELECT COUNT(*) FROM Customers),
              COALESCE((SELECT -SUM(Quantity*UnitCost) FROM InventoryTransactions
                WHERE TransactionType='Adjustment' AND date(CreatedAt,'localtime')=date('now','localtime')),0);
            """;
        using var reader = cmd.ExecuteReader();
        reader.Read();
        return new DashboardSnapshot
        {
            TodaySales = Convert.ToDecimal(reader.GetValue(0)),
            TodayExpenses = Convert.ToDecimal(reader.GetValue(1)),
            TodayGrossProfit = Convert.ToDecimal(reader.GetValue(2)),
            LowStockCount = reader.GetInt64(3), CustomerCount = reader.GetInt64(4),
            TodayInventoryAdjustmentCost = Convert.ToDecimal(reader.GetValue(5))
        };
    }

    public List<DailySalesRecord> WeeklySales()
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT date(SaleDate,'localtime'), SUM(FinalAmount)
            FROM Sales WHERE Status='Completed'
              AND date(SaleDate,'localtime') >= date('now','localtime','-6 days')
            GROUP BY date(SaleDate,'localtime');
            """;
        using var reader = cmd.ExecuteReader();
        var totals = new Dictionary<string, decimal>();
        while (reader.Read()) totals[reader.GetString(0)] = Convert.ToDecimal(reader.GetValue(1));
        var days = Enumerable.Range(0, 7).Select(offset =>
        {
            var day = DateTime.Today.AddDays(offset - 6);
            return new DailySalesRecord
            {
                Label = day.ToString("MM/dd"),
                Amount = totals.GetValueOrDefault(day.ToString("yyyy-MM-dd"))
            };
        }).ToList();
        var max = days.Max(x => x.Amount);
        foreach (var day in days) day.BarHeight = max == 0 ? 4 : Math.Max(4, (double)(day.Amount / max) * 110);
        return days;
    }

    public List<Product> LowStockProducts() =>
        new ProductService().Search("").Where(x => x.Stock <= x.MinimumStock)
            .OrderBy(x => x.Stock).Take(10).ToList();

    public Dictionary<string, decimal> TodayPayments()
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT method.Name, SUM(payment.Amount)
            FROM Payments payment JOIN Sales sale ON sale.Id=payment.SaleId
            JOIN PaymentMethods method ON method.Id=payment.PaymentMethodId
            WHERE sale.Status='Completed' AND date(sale.SaleDate,'localtime')=date('now','localtime')
            GROUP BY method.Name;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<string, decimal>();
        while (reader.Read()) result[reader.GetString(0)] = Convert.ToDecimal(reader.GetValue(1));
        return result;
    }
}
