using Microsoft.Data.Sqlite;

namespace CafeArian.Services;

internal static class BarcodeService
{
    public static bool Exists(SqliteConnection connection, SqliteTransaction transaction, string barcode,
        long? exceptProductId = null, long? exceptBatchId = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT EXISTS(SELECT 1 FROM Products WHERE Barcode=$barcode COLLATE NOCASE AND Id<>$product)
                OR EXISTS(SELECT 1 FROM ProductBatches WHERE Barcode=$barcode COLLATE NOCASE AND Id<>$batch);
            """;
        cmd.Parameters.AddWithValue("$barcode", barcode);
        cmd.Parameters.AddWithValue("$product", exceptProductId ?? -1);
        cmd.Parameters.AddWithValue("$batch", exceptBatchId ?? -1);
        return Convert.ToInt32(cmd.ExecuteScalar()) != 0;
    }

    public static string Generate(SqliteConnection connection, SqliteTransaction transaction, string prefix,
        long? exceptProductId = null, long? exceptBatchId = null)
    {
        var barcode = prefix;
        for (var suffix = 2; Exists(connection, transaction, barcode, exceptProductId, exceptBatchId); suffix++)
            barcode = $"{prefix}-{suffix}";
        return barcode;
    }
}
