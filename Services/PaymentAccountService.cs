using CafeArian.Data;
using CafeArian.Models;

namespace CafeArian.Services;

public sealed class PaymentAccountService
{
    public List<BankAccount> Banks()
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, BankName, AccountTitle, AccountNumber, CardNumber
            FROM BankAccounts WHERE IsActive=1 ORDER BY BankName, Id;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<BankAccount>();
        while (reader.Read()) result.Add(new BankAccount
        {
            Id = reader.GetInt64(0), BankName = reader.GetString(1), AccountTitle = reader.GetString(2),
            AccountNumber = reader.GetString(3), CardNumber = reader.GetString(4)
        });
        return result;
    }

    public List<PosDevice> Devices()
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT pos.Id, pos.Name, pos.BankAccountId, COALESCE(bank.BankName,''), pos.TerminalNumber
            FROM POSDevices pos LEFT JOIN BankAccounts bank ON bank.Id=pos.BankAccountId
            WHERE pos.IsActive=1 ORDER BY pos.Name, pos.Id;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<PosDevice>();
        while (reader.Read()) result.Add(new PosDevice
        {
            Id = reader.GetInt64(0), Name = reader.GetString(1),
            BankAccountId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
            BankName = reader.GetString(3), TerminalNumber = reader.GetString(4)
        });
        return result;
    }

    public void AddBank(string name, string title, string accountNumber, string cardNumber)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("نام بانک را وارد کنید.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO BankAccounts(BankName, AccountTitle, AccountNumber, CardNumber)
            VALUES($name,$title,$account,$card);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", name.Trim());
        cmd.Parameters.AddWithValue("$title", title.Trim());
        cmd.Parameters.AddWithValue("$account", accountNumber.Trim());
        cmd.Parameters.AddWithValue("$card", cardNumber.Trim());
        var id = Convert.ToInt64(cmd.ExecuteScalar());
        Audit(connection, transaction, "BankAccountCreated", "BankAccount", id);
        transaction.Commit();
    }

    public void AddDevice(string name, long? bankAccountId, string terminalNumber)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("نام کارتخوان را وارد کنید.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO POSDevices(Name, BankAccountId, TerminalNumber)
            SELECT $name, $bank, $terminal
            WHERE $bank IS NULL OR EXISTS(SELECT 1 FROM BankAccounts WHERE Id=$bank AND IsActive=1);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", name.Trim());
        cmd.Parameters.AddWithValue("$bank", (object?)bankAccountId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$terminal", terminalNumber.Trim());
        var id = Convert.ToInt64(cmd.ExecuteScalar());
        if (id == 0) throw new InvalidOperationException("حساب بانکی فعال پیدا نشد.");
        Audit(connection, transaction, "PosDeviceCreated", "POSDevice", id);
        transaction.Commit();
    }

    public void DeactivateBank(long id) => Deactivate("BankAccounts", "BankAccount", id);
    public void DeactivateDevice(long id) => Deactivate("POSDevices", "POSDevice", id);

    private static void Deactivate(string table, string referenceType, long id)
    {
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        if (table == "BankAccounts")
        {
            using var linked = connection.CreateCommand();
            linked.Transaction = transaction;
            linked.CommandText = "SELECT COUNT(*) FROM POSDevices WHERE BankAccountId=$id AND IsActive=1";
            linked.Parameters.AddWithValue("$id", id);
            if (Convert.ToInt32(linked.ExecuteScalar()) > 0)
                throw new InvalidOperationException("ابتدا کارتخوان‌های مرتبط با این حساب را غیرفعال کنید.");
        }
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"UPDATE {table} SET IsActive=0 WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("مورد انتخاب‌شده پیدا نشد.");
        Audit(connection, transaction, "Deactivated", referenceType, id);
        transaction.Commit();
    }

    private static void Audit(Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction, string action, string type, long id)
    {
        using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = """
            INSERT INTO AuditLog(Action, ReferenceType, ReferenceId) VALUES($action,$type,$id);
            """;
        audit.Parameters.AddWithValue("$action", action);
        audit.Parameters.AddWithValue("$type", type);
        audit.Parameters.AddWithValue("$id", id);
        audit.ExecuteNonQuery();
    }
}
