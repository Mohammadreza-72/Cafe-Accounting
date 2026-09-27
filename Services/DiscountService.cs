using CafeArian.Data;
using CafeArian.Models;
using Microsoft.Data.Sqlite;

namespace CafeArian.Services;

public sealed class DiscountService
{
    public List<DiscountCode> All()
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT d.Id,d.Code,d.Type,d.Value,d.MinimumPurchase,
                   COALESCE(d.StartDate,''),COALESCE(d.EndDate,''),d.UsageLimit,d.IsActive,
                   (SELECT COUNT(*) FROM DiscountUsages u JOIN Sales s ON s.Id=u.SaleId
                    WHERE u.DiscountId=d.Id AND s.Status='Completed')
            FROM Discounts d ORDER BY d.Id DESC;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<DiscountCode>();
        while (reader.Read()) result.Add(new DiscountCode
        {
            Id = reader.GetInt64(0), Code = reader.GetString(1), Type = reader.GetString(2),
            Value = Convert.ToDecimal(reader.GetValue(3)),
            MinimumPurchase = Convert.ToDecimal(reader.GetValue(4)),
            StartDate = reader.GetString(5), EndDate = reader.GetString(6),
            UsageLimit = reader.IsDBNull(7) ? null : reader.GetInt64(7),
            IsActive = reader.GetInt64(8) == 1, UsedCount = reader.GetInt64(9)
        });
        return result;
    }

    public void Add(string code, string type, decimal value, decimal minimumPurchase,
        DateTime? startDate, DateTime? endDate, long? usageLimit)
    {
        code = code.Trim().ToUpperInvariant();
        if (code.Length < 3 || code.Length > 40 || code.Any(char.IsWhiteSpace) ||
            type is not ("Percent" or "Fixed") || value <= 0 ||
            value != decimal.Truncate(value) || (type == "Percent" && value > 100) ||
            minimumPurchase < 0 || minimumPurchase != decimal.Truncate(minimumPurchase) ||
            (usageLimit.HasValue && usageLimit.Value <= 0) ||
            (startDate.HasValue && endDate.HasValue && startDate.Value.Date > endDate.Value.Date))
            throw new InvalidOperationException("اطلاعات کد تخفیف معتبر نیست.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO Discounts(Code,Type,Value,MinimumPurchase,StartDate,EndDate,UsageLimit)
            VALUES($code,$type,$value,$minimum,$start,$end,$limit);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$code", code);
        cmd.Parameters.AddWithValue("$type", type);
        cmd.Parameters.AddWithValue("$value", (long)value);
        cmd.Parameters.AddWithValue("$minimum", (long)minimumPurchase);
        cmd.Parameters.AddWithValue("$start", startDate.HasValue ? startDate.Value.ToString("yyyy-MM-dd") : DBNull.Value);
        cmd.Parameters.AddWithValue("$end", endDate.HasValue ? endDate.Value.ToString("yyyy-MM-dd") : DBNull.Value);
        cmd.Parameters.AddWithValue("$limit", (object?)usageLimit ?? DBNull.Value);
        var id = Convert.ToInt64(cmd.ExecuteScalar());
        using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = "INSERT INTO AuditLog(Action,ReferenceType,ReferenceId,Details) VALUES('DiscountCreated','Discount',$id,$code)";
        audit.Parameters.AddWithValue("$id", id);
        audit.Parameters.AddWithValue("$code", code);
        audit.ExecuteNonQuery();
        transaction.Commit();
    }

    public void Deactivate(long id)
    {
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "UPDATE Discounts SET IsActive=0 WHERE Id=$id AND IsActive=1";
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("کد تخفیف فعال پیدا نشد.");
        cmd.CommandText = "INSERT INTO AuditLog(Action,ReferenceType,ReferenceId) VALUES('DiscountDeactivated','Discount',$id)";
        cmd.ExecuteNonQuery();
        transaction.Commit();
    }

    public decimal Quote(string code, decimal subtotal)
    {
        using var connection = Database.OpenConnection();
        return Resolve(connection, null, code, subtotal).Amount;
    }

    internal static (long Id, decimal Amount) Resolve(SqliteConnection connection,
        SqliteTransaction? transaction, string code, decimal subtotal)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT d.Id,d.Type,d.Value,d.MinimumPurchase,d.StartDate,d.EndDate,d.UsageLimit,
                   (SELECT COUNT(*) FROM DiscountUsages u JOIN Sales s ON s.Id=u.SaleId
                    WHERE u.DiscountId=d.Id AND s.Status='Completed')
            FROM Discounts d WHERE d.Code=$code AND d.IsActive=1;
            """;
        cmd.Parameters.AddWithValue("$code", code.Trim());
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("کد تخفیف فعال پیدا نشد.");
        var id = reader.GetInt64(0);
        var type = reader.GetString(1);
        var value = Convert.ToDecimal(reader.GetValue(2));
        var minimum = Convert.ToDecimal(reader.GetValue(3));
        var start = reader.IsDBNull(4) ? null : reader.GetString(4);
        var end = reader.IsDBNull(5) ? null : reader.GetString(5);
        long? limit = reader.IsDBNull(6) ? null : reader.GetInt64(6);
        var used = reader.GetInt64(7);
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (subtotal < minimum) throw new InvalidOperationException("حداقل خرید این کد تخفیف رعایت نشده است.");
        if (start is not null && string.CompareOrdinal(today, start) < 0 ||
            end is not null && string.CompareOrdinal(today, end) > 0)
            throw new InvalidOperationException("کد تخفیف در این تاریخ اعتبار ندارد.");
        if (limit.HasValue && used >= limit.Value)
            throw new InvalidOperationException("سقف استفاده از کد تخفیف پر شده است.");
        var amount = type == "Percent" ? Math.Floor(subtotal * value / 100) : value;
        return (id, Math.Min(amount, subtotal));
    }
}
