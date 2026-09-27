using CafeArian.Data;
using CafeArian.Models;

namespace CafeArian.Services;

public sealed class SupplierService
{
    public List<Supplier> All()
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id,Name,COALESCE(Mobile,''),Company,Address,Notes
            FROM Suppliers WHERE IsActive=1 ORDER BY Name,Id;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<Supplier>();
        while (reader.Read()) result.Add(new Supplier
        {
            Id = reader.GetInt64(0), Name = reader.GetString(1), Mobile = reader.GetString(2),
            Company = reader.GetString(3), Address = reader.GetString(4), Notes = reader.GetString(5)
        });
        return result;
    }

    public long Add(string name, string? mobile, string? company, string? address, string? notes)
    {
        name = name.Trim();
        if (name.Length == 0) throw new InvalidOperationException("نام تأمین‌کننده را وارد کنید.");
        var normalizedMobile = string.IsNullOrWhiteSpace(mobile) ? null : CustomerService.NormalizeMobile(mobile);
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO Suppliers(Name,Mobile,Company,Address,Notes)
            VALUES($name,$mobile,$company,$address,$notes);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$mobile", (object?)normalizedMobile ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$company", company?.Trim() ?? "");
        cmd.Parameters.AddWithValue("$address", address?.Trim() ?? "");
        cmd.Parameters.AddWithValue("$notes", notes?.Trim() ?? "");
        var id = Convert.ToInt64(cmd.ExecuteScalar());
        cmd.CommandText = "INSERT INTO AuditLog(Action,ReferenceType,ReferenceId) VALUES('SupplierCreated','Supplier',$id)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        transaction.Commit();
        return id;
    }

    public void Deactivate(long id)
    {
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "UPDATE Suppliers SET IsActive=0 WHERE Id=$id AND IsActive=1";
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("تأمین‌کنندهٔ فعال پیدا نشد.");
        cmd.CommandText = "INSERT INTO AuditLog(Action,ReferenceType,ReferenceId) VALUES('SupplierDeactivated','Supplier',$id)";
        cmd.ExecuteNonQuery();
        transaction.Commit();
    }
}
