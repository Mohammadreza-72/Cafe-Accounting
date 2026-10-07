using Microsoft.Data.Sqlite;

namespace CafeArian.Services;

// Quantities use six decimal places in the product's base unit. SQL writes and
// comparisons use the same precision, avoiding binary floating-point residues.
public static class StockQuantity
{
    public static decimal Round(decimal value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);

    public static decimal Validate(decimal value)
    {
        if (value != Round(value) || Math.Abs(value) > 1_000_000_000m)
            throw new InvalidOperationException("مقدار باید حداکثر شش رقم اعشار و حداکثر یک میلیارد واحد باشد؛ واحد پایه را بررسی کنید.");
        return value;
    }

    public static void EnsureCompatible(SqliteConnection connection, SqliteTransaction transaction, long productId)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT COUNT(*) FROM Inventory WHERE ProductId=$id
              AND ABS(Quantity-ROUND(Quantity,6))>0.000000001;
            """;
        cmd.Parameters.AddWithValue("$id", productId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) != 0)
            throw new InvalidOperationException("موجودی قدیمی این کالا بیش از شش رقم اعشار دارد. برای حفظ داده‌ها عملیات متوقف شد؛ واحد و دقت موجودی را با پشتیبانی بررسی کنید.");
    }

    public static void RefreshBatchCost(SqliteConnection connection, SqliteTransaction transaction, long productId)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            UPDATE Inventory SET AverageCost=COALESCE((
                SELECT 1.0*SUM(Quantity*UnitCost)/NULLIF(SUM(Quantity),0)
                FROM ProductBatches WHERE ProductId=$id),0)
            WHERE ProductId=$id;
            """;
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }
}
