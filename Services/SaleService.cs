using CafeArian.Data;
using CafeArian.Models;
using Microsoft.Data.Sqlite;

namespace CafeArian.Services;

public sealed class SaleService
{
    public long CreateSale(IReadOnlyCollection<CartItem> items, decimal discount,
        string? customerMobile, decimal cashAmount, decimal cardAmount,
        decimal taxAmount = 0, decimal feeAmount = 0, decimal transferAmount = 0,
        long? bankAccountId = null, long? posDeviceId = null)
    {
        if (items.Count == 0 || items.Any(x => x.Quantity <= 0 || x.UnitPrice < 0 ||
            x.UnitPrice != decimal.Truncate(x.UnitPrice) || x.Total != decimal.Truncate(x.Total)))
            throw new InvalidOperationException("اقلام فاکتور معتبر نیستند.");
        if (items.GroupBy(x => x.ProductId).Any(x => x.Count() > 1))
            throw new InvalidOperationException("محصول تکراری در فاکتور وجود دارد.");
        var subtotal = items.Sum(x => x.Total);
        if (!Whole(discount) || discount < 0 || discount > subtotal)
            throw new InvalidOperationException("مبلغ تخفیف نامعتبر است.");
        if (!Whole(taxAmount) || !Whole(feeAmount) || taxAmount < 0 || feeAmount < 0)
            throw new InvalidOperationException("مالیات یا کارمزد نامعتبر است.");
        var finalAmount = subtotal - discount + taxAmount + feeAmount;
        if (!Whole(cashAmount) || !Whole(cardAmount) || !Whole(transferAmount) ||
            cashAmount < 0 || cardAmount < 0 || transferAmount < 0 ||
            cashAmount + cardAmount + transferAmount != finalAmount)
            throw new InvalidOperationException("جمع پرداخت‌ها باید دقیقاً برابر مبلغ نهایی باشد.");

        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var customerId = CustomerService.FindOrCreate(connection, transaction, customerMobile);
        using var sale = connection.CreateCommand();
        sale.Transaction = transaction;
        sale.CommandText = """
            INSERT INTO Sales(InvoiceNumber, CustomerId, SubTotal, DiscountAmount, TaxAmount, FeeAmount, FinalAmount)
            VALUES($number, $customer, $subtotal, $discount, $tax, $fee, $final);
            SELECT last_insert_rowid();
            """;
        sale.Parameters.AddWithValue("$number", Guid.NewGuid().ToString("N"));
        sale.Parameters.AddWithValue("$customer", (object?)customerId ?? DBNull.Value);
        sale.Parameters.AddWithValue("$subtotal", (long)subtotal);
        sale.Parameters.AddWithValue("$discount", (long)discount);
        sale.Parameters.AddWithValue("$tax", (long)taxAmount);
        sale.Parameters.AddWithValue("$fee", (long)feeAmount);
        sale.Parameters.AddWithValue("$final", (long)finalAmount);
        var saleId = Convert.ToInt64(sale.ExecuteScalar());
        using (var number = connection.CreateCommand())
        {
            number.Transaction = transaction;
            number.CommandText = "UPDATE Sales SET InvoiceNumber = $number WHERE Id = $id";
            number.Parameters.AddWithValue("$number", $"AR-{saleId:D6}");
            number.Parameters.AddWithValue("$id", saleId);
            number.ExecuteNonQuery();
        }

        foreach (var item in items)
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
                ingredient.Quantity * item.Quantity, ingredient.Cost, "RecipeSale");
        return ingredients.Sum(x => x.Quantity * x.Cost);
    }

    private static void Decrease(SqliteConnection connection, SqliteTransaction transaction,
        long saleId, long productId, string name, decimal quantity, decimal unitCost, string type)
    {
        using var stock = connection.CreateCommand();
        stock.Transaction = transaction;
        stock.CommandText = """
            UPDATE Inventory SET Quantity=Quantity-$qty, UpdatedAt=CURRENT_TIMESTAMP
            WHERE ProductId=$product AND Quantity >= $qty;
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
                SELECT ProductId, -Quantity FROM InventoryTransactions
                WHERE ReferenceType='Sale' AND ReferenceId=$id AND TransactionType IN ('Sale','RecipeSale');
                """;
            items.Parameters.AddWithValue("$id", saleId);
            using var reader = items.ExecuteReader();
            var rows = new List<(long ProductId, double Quantity)>();
            while (reader.Read()) rows.Add((reader.GetInt64(0), Convert.ToDouble(reader.GetValue(1))));
            reader.Close();
            if (rows.Count == 0)
            {
                // Older versions reduced stock without writing InventoryTransactions.
                using var legacy = connection.CreateCommand();
                legacy.Transaction = transaction;
                legacy.CommandText = "SELECT ProductId, Quantity FROM SaleItems WHERE SaleId=$id";
                legacy.Parameters.AddWithValue("$id", saleId);
                using var oldItems = legacy.ExecuteReader();
                while (oldItems.Read())
                    rows.Add((oldItems.GetInt64(0), Convert.ToDouble(oldItems.GetValue(1))));
            }
            foreach (var row in rows)
            {
                using var restore = connection.CreateCommand();
                restore.Transaction = transaction;
                restore.CommandText = """
                    UPDATE Inventory SET Quantity = Quantity + $qty, UpdatedAt = CURRENT_TIMESTAMP WHERE ProductId = $product;
                    INSERT INTO InventoryTransactions(ProductId, TransactionType, Quantity, ReferenceType, ReferenceId)
                    VALUES($product, 'SaleCancellation', $qty, 'Sale', $sale);
                    """;
                restore.Parameters.AddWithValue("$qty", row.Quantity);
                restore.Parameters.AddWithValue("$product", row.ProductId);
                restore.Parameters.AddWithValue("$sale", saleId);
                restore.ExecuteNonQuery();
            }
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
