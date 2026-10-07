using CafeArian.Data;
using CafeArian.Models;
using System.Globalization;

namespace CafeArian.Services;

public sealed class OperationsService
{
    public void RecordPurchase(long productId, string supplier, string? invoice, decimal quantity, decimal unitCost, bool allowDuplicate = false)
    {
        UserSession.Require("Admin", "Inventory");
        StockQuantity.Validate(quantity);
        if (quantity <= 0 || unitCost < 0 || unitCost != decimal.Truncate(unitCost) ||
            string.IsNullOrWhiteSpace(supplier))
            throw new InvalidOperationException("اطلاعات خرید معتبر نیست.");
        var total = quantity * unitCost;
        if (total != decimal.Truncate(total)) throw new InvalidOperationException("مبلغ خرید باید عدد صحیح باشد.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        invoice = NormalizeReference(invoice);
        var duplicateAccepted = CheckPurchaseReference(connection, transaction, supplier.Trim(), null, invoice, allowDuplicate);
        StockQuantity.EnsureCompatible(connection, transaction, productId);
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
        if (duplicateAccepted) AuditDuplicate(connection, transaction, purchaseId, supplier, invoice);
        using (var item = connection.CreateCommand())
        {
            item.Transaction = transaction;
            item.CommandText = """
                INSERT INTO PurchaseItems(PurchaseId, ProductId, Quantity, UnitCost)
                SELECT $purchase, Id, $qty, $cost FROM Products
                WHERE Id = $product AND IsActive = 1 AND ProductType IN (1,2);
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
                  Quantity = ROUND(Inventory.Quantity + $qty,6), UpdatedAt = CURRENT_TIMESTAMP;
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
        PostPurchaseJournal(connection, transaction, purchaseId, total, "Unpaid", null);
        transaction.Commit();
    }

    public long RecordPurchase(IReadOnlyCollection<PurchaseLine> lines, long supplierId, string? invoice,
        string paymentKind = "Unpaid", long? bankAccountId = null, bool allowDuplicate = false)
    {
        UserSession.Require("Admin", "Inventory");
        foreach (var line in lines) StockQuantity.Validate(line.Quantity);
        if (paymentKind != "Unpaid") UserSession.Require("Admin");
        if (lines.Count == 0 || lines.GroupBy(x => x.ProductId).Any(x => x.Count() != 1) ||
            lines.Any(x => x.ProductId <= 0 || x.Quantity <= 0 || x.UnitCost < 0 ||
                x.UnitCost != decimal.Truncate(x.UnitCost) ||
                x.Total != decimal.Truncate(x.Total)) || lines.Sum(x => x.Total) > long.MaxValue)
            throw new InvalidOperationException("اقلام خرید معتبر نیستند.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        ValidateFunding(connection, transaction, paymentKind, bankAccountId, true);
        using var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = "SELECT Name FROM Suppliers WHERE Id=$id AND IsActive=1";
        lookup.Parameters.AddWithValue("$id", supplierId);
        var supplier = Convert.ToString(lookup.ExecuteScalar());
        if (string.IsNullOrWhiteSpace(supplier))
            throw new InvalidOperationException("تأمین‌کنندهٔ فعال پیدا نشد.");
        invoice = NormalizeReference(invoice);
        var duplicateAccepted = CheckPurchaseReference(connection, transaction, supplier, supplierId, invoice, allowDuplicate);
        using var purchase = connection.CreateCommand();
        purchase.Transaction = transaction;
        purchase.CommandText = """
            INSERT INTO Purchases(SupplierName,SupplierId,InvoiceNumber,TotalAmount,PaymentKind,BankAccountId)
            VALUES($name,$supplier,$invoice,$total,$kind,$bank);
            SELECT last_insert_rowid();
            """;
        purchase.Parameters.AddWithValue("$name", supplier);
        purchase.Parameters.AddWithValue("$supplier", supplierId);
        purchase.Parameters.AddWithValue("$invoice", (object?)invoice?.Trim() ?? DBNull.Value);
        purchase.Parameters.AddWithValue("$total", (long)lines.Sum(x => x.Total));
        purchase.Parameters.AddWithValue("$kind", paymentKind);
        purchase.Parameters.AddWithValue("$bank", (object?)bankAccountId ?? DBNull.Value);
        var purchaseId = Convert.ToInt64(purchase.ExecuteScalar());
        if (duplicateAccepted) AuditDuplicate(connection, transaction, purchaseId, supplier, invoice);
        foreach (var line in lines)
        {
            StockQuantity.EnsureCompatible(connection, transaction, line.ProductId);
            using var item = connection.CreateCommand();
            item.Transaction = transaction;
            item.CommandText = """
                INSERT INTO PurchaseItems(PurchaseId,ProductId,Quantity,UnitCost)
                SELECT $purchase,Id,$qty,$cost FROM Products
                WHERE Id=$product AND IsActive=1 AND ProductType IN (1,2);
                """;
            item.Parameters.AddWithValue("$purchase", purchaseId);
            item.Parameters.AddWithValue("$product", line.ProductId);
            item.Parameters.AddWithValue("$qty", Convert.ToDouble(line.Quantity));
            item.Parameters.AddWithValue("$cost", (long)line.UnitCost);
            if (item.ExecuteNonQuery() != 1)
                throw new InvalidOperationException($"محصول خرید «{line.ProductName}» فعال یا قابل خرید نیست.");
            using var stock = connection.CreateCommand();
            stock.Transaction = transaction;
            stock.CommandText = """
                INSERT INTO Inventory(ProductId,Quantity,AverageCost) VALUES($product,$qty,$cost)
                ON CONFLICT(ProductId) DO UPDATE SET
                    AverageCost=CASE WHEN Inventory.Quantity+$qty=0 THEN $cost
                    ELSE (Inventory.Quantity*Inventory.AverageCost+$qty*$cost)/(Inventory.Quantity+$qty) END,
                    Quantity=ROUND(Inventory.Quantity+$qty,6),UpdatedAt=CURRENT_TIMESTAMP;
                INSERT INTO InventoryTransactions(ProductId,TransactionType,Quantity,UnitCost,ReferenceType,ReferenceId)
                VALUES($product,'Purchase',$qty,$cost,'Purchase',$purchase);
                """;
            stock.Parameters.AddWithValue("$product", line.ProductId);
            stock.Parameters.AddWithValue("$qty", Convert.ToDouble(line.Quantity));
            stock.Parameters.AddWithValue("$cost", (long)line.UnitCost);
            stock.Parameters.AddWithValue("$purchase", purchaseId);
            stock.ExecuteNonQuery();
        }
        using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = "INSERT INTO AuditLog(Action,ReferenceType,ReferenceId) VALUES('Purchase','Purchase',$id)";
        audit.Parameters.AddWithValue("$id", purchaseId);
        audit.ExecuteNonQuery();
        PostPurchaseJournal(connection, transaction, purchaseId, lines.Sum(x => x.Total), paymentKind, bankAccountId);
        if (paymentKind != "Unpaid" && lines.Sum(x => x.Total) > 0)
            InsertPurchasePayment(connection, transaction, purchaseId, lines.Sum(x => x.Total),
                paymentKind, bankAccountId);
        transaction.Commit();
        return purchaseId;
    }

    private static string NormalizeReference(string? value) => new((value ?? "").Trim().Select(c => c switch
    {
        >= '۰' and <= '۹' => (char)('0' + c - '۰'),
        >= '٠' and <= '٩' => (char)('0' + c - '٠'),
        _ => c
    }).ToArray());

    private static bool CheckPurchaseReference(Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction, string supplier, long? supplierId,
        string invoice, bool allowDuplicate)
    {
        if (invoice.Length == 0) return false;
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT InvoiceNumber FROM Purchases WHERE SupplierId=$id OR SupplierName=$name COLLATE NOCASE";
        cmd.Parameters.AddWithValue("$id", (object?)supplierId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", supplier);
        using var reader = cmd.ExecuteReader();
        var duplicate = false;
        while (reader.Read())
            duplicate |= !reader.IsDBNull(0) && string.Equals(NormalizeReference(reader.GetString(0)), invoice, StringComparison.OrdinalIgnoreCase);
        reader.Close();
        if (!duplicate) return false;
        if (!allowDuplicate) throw new DuplicatePurchaseException();
        return true;
    }

    private static void AuditDuplicate(Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction, long purchaseId, string supplier, string? invoice)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "INSERT INTO AuditLog(Action,ReferenceType,ReferenceId,Details) VALUES('DuplicatePurchaseAccepted','Purchase',$id,$details)";
        cmd.Parameters.AddWithValue("$id", purchaseId);
        cmd.Parameters.AddWithValue("$details", $"تأیید ثبت دوبارهٔ سند {invoice} برای {supplier}");
        cmd.ExecuteNonQuery();
    }

    public void PayPurchase(long purchaseId, decimal amount, string paymentKind, long? bankAccountId = null)
    {
        UserSession.Require("Admin");
        if (amount <= 0 || amount != decimal.Truncate(amount))
            throw new InvalidOperationException("مبلغ تسویه معتبر نیست.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        ValidateFunding(connection, transaction, paymentKind, bankAccountId, false);
        using var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = """
            SELECT TotalAmount-COALESCE((SELECT SUM(Amount) FROM PurchasePayments WHERE PurchaseId=Purchases.Id),0)
            FROM Purchases WHERE Id=$id;
            """;
        lookup.Parameters.AddWithValue("$id", purchaseId);
        var due = lookup.ExecuteScalar();
        if (due is null || due is DBNull || amount > Convert.ToDecimal(due))
            throw new InvalidOperationException("مبلغ پرداخت از بدهی باقی‌مانده بیشتر است یا خرید پیدا نشد.");
        var paymentId = InsertPurchasePayment(connection, transaction, purchaseId, amount, paymentKind, bankAccountId);
        JournalService.Post(connection, transaction, "PurchasePayment", paymentId, "Original",
            new JournalLine("2100", amount, 0),
            new JournalLine(paymentKind == "Cash" ? "1100" : "1200", 0, amount, bankAccountId));
        using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = "INSERT INTO AuditLog(Action,ReferenceType,ReferenceId,Details) VALUES('PurchasePayment','Purchase',$id,$details)";
        audit.Parameters.AddWithValue("$id", purchaseId);
        audit.Parameters.AddWithValue("$details", $"پرداخت {amount} تومان؛ {paymentKind}");
        audit.ExecuteNonQuery();
        transaction.Commit();
    }

    private static long InsertPurchasePayment(Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction, long purchaseId, decimal amount,
        string paymentKind, long? bankAccountId)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO PurchasePayments(PurchaseId,Amount,PaymentKind,BankAccountId,UserId)
            VALUES($purchase,$amount,$kind,$bank,$user); SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$purchase", purchaseId);
        cmd.Parameters.AddWithValue("$amount", (long)amount);
        cmd.Parameters.AddWithValue("$kind", paymentKind);
        cmd.Parameters.AddWithValue("$bank", (object?)bankAccountId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$user", UserSession.Current!.Id);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void RecordExpense(string description, decimal amount,
        string paymentKind = "Cash", long? bankAccountId = null, long? categoryId = null)
    {
        UserSession.Require("Admin");
        if (string.IsNullOrWhiteSpace(description) || amount <= 0 || amount != decimal.Truncate(amount))
            throw new InvalidOperationException("شرح یا مبلغ هزینه معتبر نیست.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        ValidateFunding(connection, transaction, paymentKind, bankAccountId, false);
        if (categoryId.HasValue)
        {
            using var category = connection.CreateCommand();
            category.Transaction = transaction;
            category.CommandText = "SELECT COUNT(*) FROM ExpenseCategories WHERE Id=$id AND IsActive=1";
            category.Parameters.AddWithValue("$id", categoryId.Value);
            if (Convert.ToInt32(category.ExecuteScalar()) != 1)
                throw new InvalidOperationException("دستهٔ هزینه فعال پیدا نشد.");
        }
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "INSERT INTO Expenses(Description, Amount, UserId, PaymentKind, BankAccountId, CategoryId) VALUES($description, $amount, $user, $kind, $bank, $category); SELECT last_insert_rowid()";
        cmd.Parameters.AddWithValue("$description", description.Trim());
        cmd.Parameters.AddWithValue("$amount", (long)amount);
        cmd.Parameters.AddWithValue("$user", UserSession.Current!.Id);
        cmd.Parameters.AddWithValue("$kind", paymentKind);
        cmd.Parameters.AddWithValue("$bank", (object?)bankAccountId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$category", (object?)categoryId ?? DBNull.Value);
        var expenseId = Convert.ToInt64(cmd.ExecuteScalar());
        using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = "INSERT INTO AuditLog(Action, ReferenceType, ReferenceId) VALUES('Expense','Expense',$id)";
        audit.Parameters.AddWithValue("$id", expenseId);
        audit.ExecuteNonQuery();
        JournalService.Post(connection, transaction, "Expense", expenseId, "Original",
            new JournalLine("6000", amount, 0),
            new JournalLine(paymentKind == "Cash" ? "1100" : "1200", 0, amount, bankAccountId));
        transaction.Commit();
    }

    private static void ValidateFunding(Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction, string kind, long? bankAccountId, bool allowUnpaid)
    {
        if (kind != "Cash" && kind != "Bank" && !(allowUnpaid && kind == "Unpaid"))
            throw new InvalidOperationException("روش پرداخت معتبر نیست.");
        if (kind != "Bank")
        {
            if (bankAccountId.HasValue) throw new InvalidOperationException("حساب بانکی برای این روش پرداخت مجاز نیست.");
            return;
        }
        if (!bankAccountId.HasValue) throw new InvalidOperationException("حساب بانکی را انتخاب کنید.");
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT COUNT(*) FROM BankAccounts WHERE Id=$id AND IsActive=1";
        cmd.Parameters.AddWithValue("$id", bankAccountId.Value);
        if (Convert.ToInt32(cmd.ExecuteScalar()) != 1)
            throw new InvalidOperationException("حساب بانکی فعال پیدا نشد.");
    }

    private static void PostPurchaseJournal(Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction, long purchaseId, decimal total,
        string paymentKind, long? bankAccountId)
    {
        if (total == 0) return;
        var creditAccount = paymentKind switch { "Cash" => "1100", "Bank" => "1200", _ => "2100" };
        JournalService.Post(connection, transaction, "Purchase", purchaseId, "Original",
            new JournalLine("1300", total, 0),
            new JournalLine(creditAccount, 0, total, bankAccountId));
    }

    public void AdjustStock(long productId, decimal delta, string reason)
    {
        UserSession.Require("Admin", "Inventory");
        StockQuantity.Validate(delta);
        if (delta == 0 || string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("مقدار تغییر و علت اصلاح موجودی را وارد کنید.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        StockQuantity.EnsureCompatible(connection, transaction, productId);
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Inventory SET Quantity=ROUND(Quantity+$delta,6), UpdatedAt=CURRENT_TIMESTAMP
                WHERE ProductId=$product AND ROUND(Quantity,6)+$delta>=0
                  AND EXISTS(SELECT 1 FROM Products WHERE Id=$product AND IsActive=1 AND ProductType IN (1,2));
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
        using (var value = connection.CreateCommand())
        {
            value.Transaction = transaction;
            value.CommandText = "SELECT AverageCost FROM Inventory WHERE ProductId=$product";
            value.Parameters.AddWithValue("$product", productId);
            var amount = Math.Round(Math.Abs(delta) * Convert.ToDecimal(value.ExecuteScalar()),
                2, MidpointRounding.AwayFromZero);
            if (amount > 0)
            {
                using var movementId = connection.CreateCommand();
                movementId.Transaction = transaction;
                movementId.CommandText = "SELECT last_insert_rowid()";
                // The audit row is the stable reference for this manually entered adjustment.
                var auditId = Convert.ToInt64(movementId.ExecuteScalar());
                JournalService.Post(connection, transaction, "StockAdjustment", auditId, "Original",
                    delta > 0 ? new JournalLine("1300", amount, 0) : new JournalLine("6100", amount, 0),
                    delta > 0 ? new JournalLine("4300", 0, amount) : new JournalLine("1300", 0, amount));
            }
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
        cmd.CommandText = """
            SELECT Id, CreatedAt, SupplierName, COALESCE(InvoiceNumber,''), TotalAmount, PaymentKind,
                   COALESCE((SELECT SUM(Amount) FROM PurchasePayments WHERE PurchaseId=Purchases.Id),0)
            FROM Purchases ORDER BY Id DESC;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<PurchaseRecord>();
        while (reader.Read())
            result.Add(new PurchaseRecord { Id = reader.GetInt64(0), Date = reader.GetString(1),
                Supplier = reader.GetString(2), InvoiceNumber = reader.GetString(3),
                Total = Convert.ToDecimal(reader.GetValue(4)), PaymentKind = reader.GetString(5),
                Paid = Convert.ToDecimal(reader.GetValue(6)) });
        return result;
    }

    public List<ExpenseRecord> Expenses()
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT e.Id,e.CreatedAt,e.Description,e.Amount,e.PaymentKind,COALESCE(c.Name,'') FROM Expenses e LEFT JOIN ExpenseCategories c ON c.Id=e.CategoryId ORDER BY e.Id DESC";
        using var reader = cmd.ExecuteReader();
        var result = new List<ExpenseRecord>();
        while (reader.Read())
            result.Add(new ExpenseRecord { Id = reader.GetInt64(0), Date = reader.GetString(1),
                Description = reader.GetString(2), Amount = Convert.ToDecimal(reader.GetValue(3)),
                PaymentKind = reader.GetString(4), Category = reader.GetString(5) });
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
              COALESCE((SELECT SUM(s.FinalAmount-s.TaxAmount) FROM Sales s WHERE s.Status='Completed'
                AND date(s.SaleDate,'localtime')=date('now','localtime')),0)
              - COALESCE((SELECT SUM(si.CostPrice*si.Quantity) FROM SaleItems si
                JOIN Sales s ON s.Id=si.SaleId WHERE s.Status='Completed' AND date(s.SaleDate,'localtime')=date('now','localtime')),0),
              (SELECT COUNT(*) FROM Customers),
              COALESCE((SELECT -SUM(Quantity*UnitCost) FROM InventoryTransactions
                WHERE TransactionType IN ('Adjustment','BatchDisposal')
                  AND date(CreatedAt,'localtime')=date('now','localtime')),0);
            """;
        using var reader = cmd.ExecuteReader();
        reader.Read();
        return new DashboardSnapshot
        {
            TodaySales = Convert.ToDecimal(reader.GetValue(0)),
            TodayExpenses = Convert.ToDecimal(reader.GetValue(1)),
            TodayGrossProfit = Convert.ToDecimal(reader.GetValue(2)),
            LowStockCount = new ProductService().Search("").Count(x => x.Stock <= x.MinimumStock),
            CustomerCount = reader.GetInt64(3),
            TodayInventoryAdjustmentCost = Convert.ToDecimal(reader.GetValue(4))
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
                Amount = totals.GetValueOrDefault(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            };
        }).ToList();
        var max = days.Max(x => x.Amount);
        foreach (var day in days) day.BarHeight = max == 0 ? 4 : Math.Max(4, (double)(day.Amount / max) * 110);
        return days;
    }

    public List<Product> LowStockProducts() =>
        new ProductService().Search("").Where(x => x.Stock <= x.MinimumStock)
            .OrderBy(x => x.Stock).Take(10).ToList();

    public List<TopProductRecord> TopProducts()
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT product.Name, SUM(item.Quantity), SUM(item.TotalPrice)
            FROM SaleItems item JOIN Sales sale ON sale.Id=item.SaleId
            JOIN Products product ON product.Id=item.ProductId
            WHERE sale.Status='Completed'
              AND date(sale.SaleDate,'localtime')>=date('now','localtime','-6 days')
            GROUP BY item.ProductId ORDER BY SUM(item.Quantity) DESC, SUM(item.TotalPrice) DESC
            LIMIT 10;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<TopProductRecord>();
        while (reader.Read()) result.Add(new TopProductRecord
        {
            Name = reader.GetString(0), Quantity = Convert.ToDecimal(reader.GetValue(1)),
            Revenue = Convert.ToDecimal(reader.GetValue(2))
        });
        return result;
    }

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
