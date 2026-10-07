using CafeArian.Data;
using CafeArian.Models;
using Microsoft.Data.Sqlite;

namespace CafeArian.Services;

public sealed class SaleService
{
    public void ValidateCartStock(IReadOnlyCollection<CartItem> items)
    {
        UserSession.Require("Admin", "Cashier");
        var products = new ProductService().Search("").ToDictionary(x => x.Id);
        var demand = new Dictionary<long, decimal>();
        foreach (var group in items.GroupBy(x => x.ProductId))
        {
            if (!products.TryGetValue(group.Key, out var product) || product.ProductType == 2)
                throw new InvalidOperationException("یکی از کالاهای سبد دیگر قابل فروش نیست.");
            var quantity = StockQuantity.Validate(group.Sum(x => x.Quantity));
            if (quantity <= 0 || quantity > product.Stock)
                throw new InvalidOperationException($"موجودی «{product.Name}» برای این سبد کافی نیست.");
            if (product.ProductType == 3)
                foreach (var ingredient in new RecipeService().GetItems(product.Id))
                    demand[ingredient.IngredientProductId] = demand.GetValueOrDefault(ingredient.IngredientProductId) +
                        StockQuantity.Validate(ingredient.Quantity * quantity);
            if (product.ProductType == 4)
            {
                var batches = new BatchService().All().ToDictionary(x => x.Id);
                foreach (var selected in group.Where(x => x.BatchId.HasValue).GroupBy(x => x.BatchId!.Value))
                    if (!batches.TryGetValue(selected.Key, out var batch) || batch.ProductId != product.Id ||
                        string.CompareOrdinal(batch.ExpiresAt, DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)) < 0 ||
                        selected.Sum(x => x.Quantity) > StockQuantity.Round(batch.Quantity))
                        throw new InvalidOperationException($"موجودی بچ انتخاب‌شدهٔ «{product.Name}» کافی نیست.");
            }
        }
        foreach (var item in demand)
            if (!products.TryGetValue(item.Key, out var ingredient) || item.Value > ingredient.OnHand)
                throw new InvalidOperationException($"مادهٔ «{ingredient?.Name ?? "غیرفعال"}» برای مجموع نوشیدنی‌های سبد کافی نیست؛ تعداد یا یکی از نوشیدنی‌ها را کم کنید.");
    }

    public long CreateSale(IReadOnlyCollection<CartItem> items, decimal discount,
        string? customerMobile, decimal cashAmount, decimal cardAmount,
        decimal taxAmount = 0, decimal feeAmount = 0, decimal transferAmount = 0,
        long? bankAccountId = null, long? posDeviceId = null, string? discountCode = null,
        bool applyConfiguredTax = false, bool applyConfiguredFee = false)
    {
        UserSession.Require("Admin", "Cashier");
        foreach (var item in items) StockQuantity.Validate(item.Quantity);
        if (items.Count == 0 || items.Any(x => x.Quantity <= 0 || x.UnitPrice < 0 ||
            x.UnitPrice != decimal.Truncate(x.UnitPrice) || x.Total != decimal.Truncate(x.Total)))
            throw new InvalidOperationException("اقلام فاکتور معتبر نیستند.");
        if (items.GroupBy(x => (x.ProductId, x.BatchId)).Any(x => x.Count() > 1))
            throw new InvalidOperationException("محصول تکراری در فاکتور وجود دارد.");
        var subtotal = items.Sum(x => x.Total);
        if (!Whole(discount) || discount < 0 || discount > subtotal)
            throw new InvalidOperationException("مبلغ تخفیف نامعتبر است.");
        if (!Whole(taxAmount) || !Whole(feeAmount) || taxAmount < 0 || feeAmount < 0)
            throw new InvalidOperationException("مالیات یا کارمزد نامعتبر است.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        long? discountId = null;
        if (!string.IsNullOrWhiteSpace(discountCode))
        {
            if (discount != 0)
                throw new InvalidOperationException("تخفیف دستی و کد تخفیف هم‌زمان قابل استفاده نیستند.");
            var resolved = DiscountService.Resolve(connection, transaction, discountCode, subtotal);
            discountId = resolved.Id;
            discount = resolved.Amount;
        }
        if (applyConfiguredTax || applyConfiguredFee)
        {
            var charges = ChargeSettingsService.Resolve(connection, transaction,
                subtotal, discount, applyConfiguredTax, applyConfiguredFee);
            if (applyConfiguredTax) taxAmount = charges.Tax;
            if (applyConfiguredFee) feeAmount = charges.Fee;
        }
        var finalAmount = subtotal - discount + taxAmount + feeAmount;
        if (!Whole(cashAmount) || !Whole(cardAmount) || !Whole(transferAmount) ||
            cashAmount < 0 || cardAmount < 0 || transferAmount < 0 ||
            cashAmount + cardAmount + transferAmount != finalAmount)
            throw new InvalidOperationException("جمع پرداخت‌ها باید دقیقاً برابر مبلغ نهایی باشد.");
        var customerId = CustomerService.FindOrCreate(connection, transaction, customerMobile);
        using var sale = connection.CreateCommand();
        sale.Transaction = transaction;
        sale.CommandText = """
            INSERT INTO Sales(InvoiceNumber, CustomerId, SubTotal, DiscountAmount, TaxAmount, FeeAmount, FinalAmount, UserId)
            VALUES($number, $customer, $subtotal, $discount, $tax, $fee, $final, $user);
            SELECT last_insert_rowid();
            """;
        sale.Parameters.AddWithValue("$number", Guid.NewGuid().ToString("N"));
        sale.Parameters.AddWithValue("$customer", (object?)customerId ?? DBNull.Value);
        sale.Parameters.AddWithValue("$subtotal", (long)subtotal);
        sale.Parameters.AddWithValue("$discount", (long)discount);
        sale.Parameters.AddWithValue("$tax", (long)taxAmount);
        sale.Parameters.AddWithValue("$fee", (long)feeAmount);
        sale.Parameters.AddWithValue("$final", (long)finalAmount);
        sale.Parameters.AddWithValue("$user", UserSession.Current!.Id);
        var saleId = Convert.ToInt64(sale.ExecuteScalar());
        if (discountId.HasValue)
        {
            using var usage = connection.CreateCommand();
            usage.Transaction = transaction;
            usage.CommandText = """
                INSERT INTO DiscountUsages(DiscountId,CustomerId,SaleId,Amount)
                VALUES($discount,$customer,$sale,$amount);
                """;
            usage.Parameters.AddWithValue("$discount", discountId.Value);
            usage.Parameters.AddWithValue("$customer", (object?)customerId ?? DBNull.Value);
            usage.Parameters.AddWithValue("$sale", saleId);
            usage.Parameters.AddWithValue("$amount", (long)discount);
            usage.ExecuteNonQuery();
        }
        using (var number = connection.CreateCommand())
        {
            number.Transaction = transaction;
            number.CommandText = "UPDATE Sales SET InvoiceNumber = $number WHERE Id = $id";
            number.Parameters.AddWithValue("$number", $"AR-{saleId:D6}");
            number.Parameters.AddWithValue("$id", saleId);
            number.ExecuteNonQuery();
        }

        foreach (var item in items.OrderByDescending(x => x.BatchId.HasValue))
        {
            var cost = ConsumeStock(connection, transaction, saleId, item);
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO SaleItems(SaleId, ProductId, Quantity, UnitPrice, CostPrice, TotalPrice)
                VALUES($sale, $product, $qty, $price, $cost, $total);
                """;
            insert.Parameters.AddWithValue("$sale", saleId);
            insert.Parameters.AddWithValue("$product", item.ProductId);
            insert.Parameters.AddWithValue("$qty", Convert.ToDouble(item.Quantity));
            insert.Parameters.AddWithValue("$price", (long)item.UnitPrice);
            insert.Parameters.AddWithValue("$cost", Convert.ToDouble(cost));
            insert.Parameters.AddWithValue("$total", (long)item.Total);
            insert.ExecuteNonQuery();
        }
        AddPayment(connection, transaction, saleId, "نقدی", cashAmount);
        AddPayment(connection, transaction, saleId, "کارتخوان", cardAmount, posDeviceId: posDeviceId);
        AddPayment(connection, transaction, saleId, "کارت به کارت", transferAmount, bankAccountId: bankAccountId);
        var journalLines = new List<JournalLine>();
        if (cashAmount > 0) journalLines.Add(new JournalLine("1100", cashAmount, 0));
        if (cardAmount > 0)
        {
            long? posBank = null;
            if (posDeviceId.HasValue)
            {
                using var pos = connection.CreateCommand();
                pos.Transaction = transaction;
                pos.CommandText = "SELECT BankAccountId FROM POSDevices WHERE Id=$id";
                pos.Parameters.AddWithValue("$id", posDeviceId.Value);
                posBank = pos.ExecuteScalar() is long bank ? bank : null;
            }
            journalLines.Add(new JournalLine("1200", cardAmount, 0, posBank));
        }
        if (transferAmount > 0) journalLines.Add(new JournalLine("1200", transferAmount, 0, bankAccountId));
        if (subtotal - discount > 0)
            journalLines.Add(new JournalLine("4000", 0, subtotal - discount));
        if (taxAmount > 0) journalLines.Add(new JournalLine("2300", 0, taxAmount));
        if (feeAmount > 0) journalLines.Add(new JournalLine("4200", 0, feeAmount));
        using (var costs = connection.CreateCommand())
        {
            costs.Transaction = transaction;
            costs.CommandText = """
                SELECT COALESCE(SUM(-Quantity*UnitCost),0) FROM InventoryTransactions
                WHERE ReferenceType='Sale' AND ReferenceId=$id AND Quantity<0;
                """;
            costs.Parameters.AddWithValue("$id", saleId);
            var cost = Math.Round(Convert.ToDecimal(costs.ExecuteScalar()), 2, MidpointRounding.AwayFromZero);
            if (cost > 0)
            {
                journalLines.Add(new JournalLine("5000", cost, 0));
                journalLines.Add(new JournalLine("1300", 0, cost));
            }
        }
        if (journalLines.Count > 0)
            JournalService.Post(connection, transaction, "Sale", saleId, "Original", journalLines.ToArray());
        if (customerId.HasValue)
        {
            using var customer = connection.CreateCommand();
            customer.Transaction = transaction;
            customer.CommandText = """
                UPDATE Customers SET TotalOrders = TotalOrders + 1,
                TotalPurchase = TotalPurchase + $amount, TotalDiscount = TotalDiscount + $discount
                WHERE Id = $id;
                """;
            customer.Parameters.AddWithValue("$amount", (long)finalAmount);
            customer.Parameters.AddWithValue("$discount", (long)discount);
            customer.Parameters.AddWithValue("$id", customerId.Value);
            customer.ExecuteNonQuery();
        }
        transaction.Commit();
        return saleId;
    }

    private static decimal ConsumeStock(SqliteConnection connection, SqliteTransaction transaction,
        long saleId, CartItem item)
    {
        using var product = connection.CreateCommand();
        product.Transaction = transaction;
        product.CommandText = """
            SELECT p.ProductType, COALESCE(i.AverageCost,p.CostPrice), p.SalePrice
            FROM Products p LEFT JOIN Inventory i ON i.ProductId=p.Id
            WHERE p.Id=$id AND p.IsActive=1;
            """;
        product.Parameters.AddWithValue("$id", item.ProductId);
        int type;
        decimal unitCost;
        using (var reader = product.ExecuteReader())
        {
            if (!reader.Read()) throw new InvalidOperationException($"محصول «{item.ProductName}» فعال نیست.");
            type = Convert.ToInt32(reader.GetValue(0));
            unitCost = Convert.ToDecimal(reader.GetValue(1));
            if (Convert.ToDecimal(reader.GetValue(2)) != item.UnitPrice)
                throw new InvalidOperationException($"قیمت «{item.ProductName}» تغییر کرده است؛ محصول را دوباره به سبد اضافه کنید.");
        }
        if (type == 2) throw new InvalidOperationException("ماده اولیه مستقیماً قابل فروش نیست.");
        if (type == 4) return ConsumeBatches(connection, transaction, saleId, item);
        if (item.BatchId.HasValue) throw new InvalidOperationException("بچ برای این نوع محصول معتبر نیست.");
        if (type != 3)
        {
            Decrease(connection, transaction, saleId, item.ProductId, item.ProductName,
                item.Quantity, unitCost, "Sale");
            return unitCost;
        }
        using var recipe = connection.CreateCommand();
        recipe.Transaction = transaction;
        recipe.CommandText = """
            SELECT ingredient.Id, ingredient.Name, ingredient.IsActive, ri.Quantity,
                   COALESCE(stock.AverageCost,ingredient.CostPrice)
            FROM Recipes r JOIN RecipeItems ri ON ri.RecipeId=r.Id
            JOIN Products ingredient ON ingredient.Id=ri.IngredientProductId
            LEFT JOIN Inventory stock ON stock.ProductId=ingredient.Id
            WHERE r.ProductId=$product AND r.IsActive=1;
            """;
        recipe.Parameters.AddWithValue("$product", item.ProductId);
        var ingredients = new List<(long Id, string Name, bool Active, decimal Quantity, decimal Cost)>();
        using (var reader = recipe.ExecuteReader())
            while (reader.Read())
                ingredients.Add((reader.GetInt64(0), reader.GetString(1), Convert.ToInt32(reader.GetValue(2)) == 1,
                    Convert.ToDecimal(reader.GetValue(3)), Convert.ToDecimal(reader.GetValue(4))));
        if (ingredients.Count == 0) throw new InvalidOperationException($"برای «{item.ProductName}» دستور تهیه ثبت نشده است.");
        if (ingredients.Any(x => !x.Active)) throw new InvalidOperationException("یکی از مواد دستور تهیه غیرفعال است.");
        foreach (var ingredient in ingredients)
            Decrease(connection, transaction, saleId, ingredient.Id, ingredient.Name,
                StockQuantity.Validate(ingredient.Quantity * item.Quantity), ingredient.Cost, "RecipeSale");
        return ingredients.Sum(x => x.Quantity * x.Cost);
    }

    private static decimal ConsumeBatches(SqliteConnection connection, SqliteTransaction transaction,
        long saleId, CartItem item)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT Id, BatchNumber, Quantity, UnitCost
            FROM ProductBatches WHERE ProductId=$product AND Quantity>0
              AND ExpiresAt>=date('now','localtime') AND ($batch IS NULL OR Id=$batch)
            ORDER BY ExpiresAt, Id;
            """;
        cmd.Parameters.AddWithValue("$product", item.ProductId);
        cmd.Parameters.AddWithValue("$batch", (object?)item.BatchId ?? DBNull.Value);
        using var reader = cmd.ExecuteReader();
        var batches = new List<(long Id, string Number, decimal Quantity, decimal Cost)>();
        while (reader.Read())
            batches.Add((reader.GetInt64(0), reader.GetString(1),
                StockQuantity.Round(Convert.ToDecimal(reader.GetValue(2))), Convert.ToDecimal(reader.GetValue(3))));
        reader.Close();
        var remaining = item.Quantity;
        decimal totalCost = 0;
        foreach (var batch in batches)
        {
            if (remaining <= 0) break;
            var used = Math.Min(remaining, batch.Quantity);
            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE ProductBatches SET Quantity=ROUND(Quantity-$qty,6)
                    WHERE Id=$batch AND ROUND(Quantity,6) >= $qty AND ExpiresAt>=date('now','localtime');
                    """;
                update.Parameters.AddWithValue("$qty", Convert.ToDouble(used));
                update.Parameters.AddWithValue("$batch", batch.Id);
                if (update.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException($"موجودی بچ «{batch.Number}» کافی نیست.");
            }
            Decrease(connection, transaction, saleId, item.ProductId, item.ProductName,
                used, batch.Cost, "BatchSale");
            using (var allocation = connection.CreateCommand())
            {
                allocation.Transaction = transaction;
                allocation.CommandText = """
                    INSERT INTO SaleBatchAllocations(SaleId,BatchId,Quantity) VALUES($sale,$batch,$qty);
                    """;
                allocation.Parameters.AddWithValue("$sale", saleId);
                allocation.Parameters.AddWithValue("$batch", batch.Id);
                allocation.Parameters.AddWithValue("$qty", Convert.ToDouble(used));
                allocation.ExecuteNonQuery();
            }
            totalCost += used * batch.Cost;
            remaining -= used;
        }
        if (remaining > 0)
            throw new InvalidOperationException($"بچ معتبر و غیرمنقضی برای «{item.ProductName}» کافی نیست.");
        StockQuantity.RefreshBatchCost(connection, transaction, item.ProductId);
        return totalCost / item.Quantity;
    }

    private static void Decrease(SqliteConnection connection, SqliteTransaction transaction,
        long saleId, long productId, string name, decimal quantity, decimal unitCost, string type)
    {
        StockQuantity.EnsureCompatible(connection, transaction, productId);
        using var stock = connection.CreateCommand();
        stock.Transaction = transaction;
        stock.CommandText = """
            UPDATE Inventory SET Quantity=ROUND(Quantity-$qty,6), UpdatedAt=CURRENT_TIMESTAMP
            WHERE ProductId=$product AND ROUND(Quantity,6) >= $qty;
            """;
        stock.Parameters.AddWithValue("$qty", Convert.ToDouble(quantity));
        stock.Parameters.AddWithValue("$product", productId);
        if (stock.ExecuteNonQuery() != 1)
            throw new InvalidOperationException($"موجودی «{name}» کافی نیست.");
        using var movement = connection.CreateCommand();
        movement.Transaction = transaction;
        movement.CommandText = """
            INSERT INTO InventoryTransactions(ProductId, TransactionType, Quantity, UnitCost, ReferenceType, ReferenceId)
            VALUES($product,$type,-$qty,$cost,'Sale',$sale);
            """;
        movement.Parameters.AddWithValue("$product", productId);
        movement.Parameters.AddWithValue("$type", type);
        movement.Parameters.AddWithValue("$qty", Convert.ToDouble(quantity));
        movement.Parameters.AddWithValue("$cost", Convert.ToDouble(unitCost));
        movement.Parameters.AddWithValue("$sale", saleId);
        movement.ExecuteNonQuery();
    }

    public void CancelSale(long saleId)
    {
        UserSession.Require("Admin");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var sale = connection.CreateCommand();
        sale.Transaction = transaction;
        sale.CommandText = "SELECT CustomerId, FinalAmount, DiscountAmount FROM Sales WHERE Id = $id AND Status = 'Completed'";
        sale.Parameters.AddWithValue("$id", saleId);
        long? customerId;
        long amount, discount;
        using (var reader = sale.ExecuteReader())
        {
            if (!reader.Read()) throw new InvalidOperationException("فاکتور فعال پیدا نشد.");
            customerId = reader.IsDBNull(0) ? null : reader.GetInt64(0);
            amount = Convert.ToInt64(reader.GetValue(1));
            discount = Convert.ToInt64(reader.GetValue(2));
        }
        using (var items = connection.CreateCommand())
        {
            items.Transaction = transaction;
            items.CommandText = """
                SELECT ProductId, -Quantity, UnitCost FROM InventoryTransactions
                WHERE ReferenceType='Sale' AND ReferenceId=$id AND TransactionType IN ('Sale','RecipeSale','BatchSale');
                """;
            items.Parameters.AddWithValue("$id", saleId);
            using var reader = items.ExecuteReader();
            var rows = new List<(long ProductId, decimal Quantity, decimal Cost)>();
            while (reader.Read()) rows.Add((reader.GetInt64(0), StockQuantity.Round(Convert.ToDecimal(reader.GetValue(1))), Convert.ToDecimal(reader.GetValue(2))));
            reader.Close();
            if (rows.Count == 0)
            {
                // Older versions reduced stock without writing InventoryTransactions.
                using var legacy = connection.CreateCommand();
                legacy.Transaction = transaction;
                legacy.CommandText = "SELECT ProductId, Quantity, CostPrice FROM SaleItems WHERE SaleId=$id";
                legacy.Parameters.AddWithValue("$id", saleId);
                using var oldItems = legacy.ExecuteReader();
                while (oldItems.Read())
                    rows.Add((oldItems.GetInt64(0), StockQuantity.Round(Convert.ToDecimal(oldItems.GetValue(1))), Convert.ToDecimal(oldItems.GetValue(2))));
            }
            foreach (var row in rows)
            {
                StockQuantity.EnsureCompatible(connection, transaction, row.ProductId);
                using var restore = connection.CreateCommand();
                restore.Transaction = transaction;
                restore.CommandText = """
                    UPDATE Inventory SET
                        AverageCost=(Quantity*AverageCost+$qty*$cost)/(ROUND(Quantity,6)+$qty),
                        Quantity = ROUND(Quantity + $qty,6), UpdatedAt = CURRENT_TIMESTAMP WHERE ProductId = $product;
                    INSERT INTO InventoryTransactions(ProductId, TransactionType, Quantity, UnitCost, ReferenceType, ReferenceId)
                    VALUES($product, 'SaleCancellation', $qty, $cost, 'Sale', $sale);
                    """;
                restore.Parameters.AddWithValue("$qty", Convert.ToDouble(row.Quantity));
                restore.Parameters.AddWithValue("$cost", Convert.ToDouble(row.Cost));
                restore.Parameters.AddWithValue("$product", row.ProductId);
                restore.Parameters.AddWithValue("$sale", saleId);
                restore.ExecuteNonQuery();
            }
        }
        using (var allocations = connection.CreateCommand())
        {
            allocations.Transaction = transaction;
            allocations.CommandText = "SELECT BatchId, Quantity FROM SaleBatchAllocations WHERE SaleId=$id";
            allocations.Parameters.AddWithValue("$id", saleId);
            using var reader = allocations.ExecuteReader();
            var rows = new List<(long BatchId, double Quantity)>();
            while (reader.Read()) rows.Add((reader.GetInt64(0), Convert.ToDouble(reader.GetValue(1))));
            reader.Close();
            foreach (var row in rows)
            {
                using var restore = connection.CreateCommand();
                restore.Transaction = transaction;
                restore.CommandText = "UPDATE ProductBatches SET Quantity=ROUND(Quantity+$qty,6) WHERE Id=$batch";
                restore.Parameters.AddWithValue("$qty", row.Quantity);
                restore.Parameters.AddWithValue("$batch", row.BatchId);
                if (restore.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("بچ فاکتور برای بازگشت موجودی پیدا نشد.");
            }
            using var products = connection.CreateCommand();
            products.Transaction = transaction;
            products.CommandText = "SELECT DISTINCT b.ProductId FROM SaleBatchAllocations a JOIN ProductBatches b ON b.Id=a.BatchId WHERE a.SaleId=$id";
            products.Parameters.AddWithValue("$id", saleId);
            using var productReader = products.ExecuteReader();
            var ids = new List<long>();
            while (productReader.Read()) ids.Add(productReader.GetInt64(0));
            productReader.Close();
            foreach (var id in ids) StockQuantity.RefreshBatchCost(connection, transaction, id);
        }
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE Sales SET Status = 'Cancelled' WHERE Id = $id AND Status = 'Completed'";
            update.Parameters.AddWithValue("$id", saleId);
            if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException("لغو فاکتور انجام نشد.");
        }
        if (customerId.HasValue)
        {
            using var customer = connection.CreateCommand();
            customer.Transaction = transaction;
            customer.CommandText = """
                UPDATE Customers SET TotalOrders = TotalOrders - 1,
                TotalPurchase = TotalPurchase - $amount, TotalDiscount = TotalDiscount - $discount WHERE Id = $id;
                """;
            customer.Parameters.AddWithValue("$amount", amount);
            customer.Parameters.AddWithValue("$discount", discount);
            customer.Parameters.AddWithValue("$id", customerId.Value);
            customer.ExecuteNonQuery();
        }
        using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = "INSERT INTO AuditLog(Action, ReferenceType, ReferenceId) VALUES('Cancel', 'Sale', $id)";
            audit.Parameters.AddWithValue("$id", saleId);
            audit.ExecuteNonQuery();
        }
        JournalService.Reverse(connection, transaction, "Sale", saleId);
        transaction.Commit();
    }

    private static void AddPayment(SqliteConnection connection, SqliteTransaction transaction,
        long saleId, string method, decimal amount, long? bankAccountId = null, long? posDeviceId = null)
    {
        if (amount == 0) return;
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO Payments(SaleId, PaymentMethodId, Amount, BankAccountId, PosDeviceId)
            SELECT $sale, Id, $amount, $bank, $pos FROM PaymentMethods
            WHERE Name = $method AND IsActive = 1
              AND ($bank IS NULL OR EXISTS(SELECT 1 FROM BankAccounts WHERE Id=$bank AND IsActive=1))
              AND ($pos IS NULL OR EXISTS(SELECT 1 FROM POSDevices WHERE Id=$pos AND IsActive=1));
            """;
        cmd.Parameters.AddWithValue("$sale", saleId);
        cmd.Parameters.AddWithValue("$method", method);
        cmd.Parameters.AddWithValue("$amount", (long)amount);
        cmd.Parameters.AddWithValue("$bank", (object?)bankAccountId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pos", (object?)posDeviceId ?? DBNull.Value);
        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException($"روش پرداخت یا حساب انتخاب‌شده برای «{method}» فعال نیست.");
    }

    private static bool Whole(decimal value) => value == decimal.Truncate(value);
}
