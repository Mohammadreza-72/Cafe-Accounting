using CafeArian.Data;
using CafeArian.Models;

namespace CafeArian.Services;

public sealed class ExpenseCategoryService
{
    public List<ExpenseCategory> All()
    {
        UserSession.Require("Admin");
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id,Name FROM ExpenseCategories WHERE IsActive=1 ORDER BY Name";
        using var reader = cmd.ExecuteReader();
        var result = new List<ExpenseCategory>();
        while (reader.Read()) result.Add(new ExpenseCategory { Id = reader.GetInt64(0), Name = reader.GetString(1) });
        return result;
    }

    public long Add(string name)
    {
        UserSession.Require("Admin");
        name = name.Trim();
        if (name.Length is < 1 or > 80) throw new InvalidOperationException("نام دستهٔ هزینه معتبر نیست.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "INSERT INTO ExpenseCategories(Name) VALUES($name); SELECT last_insert_rowid()";
        cmd.Parameters.AddWithValue("$name", name);
        var id = Convert.ToInt64(cmd.ExecuteScalar());
        using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = "INSERT INTO AuditLog(Action,ReferenceType,ReferenceId,Details) VALUES('ExpenseCategoryCreated','ExpenseCategory',$id,$name)";
        audit.Parameters.AddWithValue("$id", id);
        audit.Parameters.AddWithValue("$name", name);
        audit.ExecuteNonQuery();
        transaction.Commit();
        return id;
    }
}
