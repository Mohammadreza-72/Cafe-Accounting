using CafeArian.Data;
using CafeArian.Models;

namespace CafeArian.Services;

public sealed class CategoryService
{
    public List<Category> All()
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Name FROM Categories WHERE IsActive=1 ORDER BY Name";
        using var reader = cmd.ExecuteReader();
        var result = new List<Category>();
        while (reader.Read()) result.Add(new Category { Id = reader.GetInt64(0), Name = reader.GetString(1) });
        return result;
    }

    public long Add(string name)
    {
        UserSession.Require("Admin", "Inventory");
        name = name.Trim();
        if (name.Length is < 1 or > 80) throw new InvalidOperationException("نام دسته‌بندی باید بین ۱ تا ۸۰ نویسه باشد.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "SELECT COUNT(*) FROM Categories WHERE Name=$name COLLATE NOCASE";
        check.Parameters.AddWithValue("$name", name);
        if (Convert.ToInt32(check.ExecuteScalar()) != 0)
            throw new InvalidOperationException("این دسته‌بندی قبلاً ثبت شده است.");
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "INSERT INTO Categories(Name) VALUES($name); SELECT last_insert_rowid()";
        cmd.Parameters.AddWithValue("$name", name);
        var id = Convert.ToInt64(cmd.ExecuteScalar());
        using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = "INSERT INTO AuditLog(Action,ReferenceType,ReferenceId,Details) VALUES('CategoryCreated','Category',$id,$name)";
        audit.Parameters.AddWithValue("$id", id);
        audit.Parameters.AddWithValue("$name", name);
        audit.ExecuteNonQuery();
        transaction.Commit();
        return id;
    }
}
