using CafeArian.Data;
using CafeArian.Models;
using Microsoft.Data.Sqlite;

namespace CafeArian.Services;

public class SaleService
{
    public long CreateSale(
        IReadOnlyCollection<CartItem> items,
        decimal discount,
        long? customerId,
        decimal cashAmount,
        decimal cardAmount)
    {
        if (items.Count == 0)
            throw new InvalidOperationException("فاکتور خالی است.");

        var subtotal = items.Sum(x => x.Total);
        var finalAmount = subtotal - discount;

        if (finalAmount < 0)
            throw new InvalidOperationException("تخفیف نمی‌تواند از مبلغ فاکتور بیشتر باشد.");

        if (Math.Round(cashAmount + cardAmount, 0) != Math.Round(finalAmount, 0))
            throw new InvalidOperationException("مبلغ پرداختی با مبلغ فاکتور برابر نیست.");

        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            var invoiceNumber = $"AR-{DateTime.Now:yyyyMMddHHmmssfff}";

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = """
                INSERT INTO Sales(InvoiceNumber, CustomerId, SubTotal, DiscountAmount, FinalAmount)
                VALUES($invoice, $customer, $subtotal, $discount, $final);
                SELECT last_insert_rowid();
                """;

                cmd.Parameters.AddWithValue("$invoice", invoiceNumber);
                cmd.Parameters.AddWithValue("$customer", (object?)customerId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$subtotal", subtotal);
                cmd.Parameters.AddWithValue("$discount", discount);
                cmd.Parameters.AddWithValue("$final", finalAmount);

                var saleId = Convert.ToInt64(cmd.ExecuteScalar());

                foreach (var item in items)
                {
                    InsertSaleItem(connection, transaction, saleId, item);
                    DecreaseInventory(connection, transaction, item);
                }

                if (cashAmount > 0)
                    InsertPayment(connection, transaction, saleId, 1, cashAmount);

                if (cardAmount > 0)
                    InsertPayment(connection, transaction, saleId, 2, cardAmount);

                if (customerId.HasValue)
                    UpdateCustomer(connection, transaction, customerId.Value, finalAmount, discount);

                transaction.Commit();
                return saleId;
            }
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static void InsertSaleItem(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long saleId,
        CartItem item)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
        INSERT INTO SaleItems
        (SaleId, ProductId, Quantity, UnitPrice, CostPrice, TotalPrice)
        SELECT $sale, $product, $qty, $price, CostPrice, $total
        FROM Products WHERE Id = $product;
        """;

        cmd.Parameters.AddWithValue("$sale", saleId);
        cmd.Parameters.AddWithValue("$product", item.ProductId);
        cmd.Parameters.AddWithValue("$qty", item.Quantity);
        cmd.Parameters.AddWithValue("$price", item.UnitPrice);
        cmd.Parameters.AddWithValue("$total", item.Total);
        cmd.ExecuteNonQuery();
    }

    private static void DecreaseInventory(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CartItem item)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
        UPDATE Inventory
        SET Quantity = Quantity - $qty,
            UpdatedAt = CURRENT_TIMESTAMP
        WHERE ProductId = $product AND Quantity >= $qty;
        """;

        cmd.Parameters.AddWithValue("$qty", item.Quantity);
        cmd.Parameters.AddWithValue("$product", item.ProductId);

        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException($"موجودی محصول «{item.ProductName}» کافی نیست.");
    }

    private static void InsertPayment(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long saleId,
        long methodId,
        decimal amount)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
        INSERT INTO Payments(SaleId, PaymentMethodId, Amount)
        VALUES($sale, $method, $amount);
        """;

        cmd.Parameters.AddWithValue("$sale", saleId);
        cmd.Parameters.AddWithValue("$method", methodId);
        cmd.Parameters.AddWithValue("$amount", amount);
        cmd.ExecuteNonQuery();
    }

    private static void UpdateCustomer(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long customerId,
        decimal amount,
        decimal discount)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
        UPDATE Customers
        SET TotalOrders = TotalOrders + 1,
            TotalPurchase = TotalPurchase + $amount,
            TotalDiscount = TotalDiscount + $discount
        WHERE Id = $id;
        """;

        cmd.Parameters.AddWithValue("$amount", amount);
        cmd.Parameters.AddWithValue("$discount", discount);
        cmd.Parameters.AddWithValue("$id", customerId);
        cmd.ExecuteNonQuery();
    }
}
