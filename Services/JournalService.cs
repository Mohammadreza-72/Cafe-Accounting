using CafeArian.Data;
using Microsoft.Data.Sqlite;

namespace CafeArian.Services;

public readonly record struct JournalLine(string AccountCode, decimal Debit, decimal Credit,
    long? BankAccountId = null);

public sealed class JournalService
{
    public static void Post(SqliteConnection connection, SqliteTransaction transaction,
        string referenceType, long referenceId, string entryType, params JournalLine[] lines)
    {
        if (lines.Length < 2 || lines.Any(x => x.AccountCode.Length == 0 ||
            (x.Debit <= 0 && x.Credit <= 0) || (x.Debit > 0 && x.Credit > 0)) ||
            lines.Sum(x => x.Debit) != lines.Sum(x => x.Credit))
            throw new InvalidOperationException("سند حسابداری متوازن نیست.");
        using var entry = connection.CreateCommand();
        entry.Transaction = transaction;
        entry.CommandText = """
            INSERT INTO JournalEntries(ReferenceType,ReferenceId,EntryType,UserId)
            VALUES($type,$id,$entry,$user); SELECT last_insert_rowid();
            """;
        entry.Parameters.AddWithValue("$type", referenceType);
        entry.Parameters.AddWithValue("$id", referenceId);
        entry.Parameters.AddWithValue("$entry", entryType);
        entry.Parameters.AddWithValue("$user", (object?)UserSession.Current?.Id ?? DBNull.Value);
        var entryId = Convert.ToInt64(entry.ExecuteScalar());
        foreach (var line in lines)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO JournalLines(EntryId,AccountCode,BankAccountId,Debit,Credit)
                VALUES($entry,$account,$bank,$debit,$credit);
                """;
            cmd.Parameters.AddWithValue("$entry", entryId);
            cmd.Parameters.AddWithValue("$account", line.AccountCode);
            cmd.Parameters.AddWithValue("$bank", (object?)line.BankAccountId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$debit", line.Debit);
            cmd.Parameters.AddWithValue("$credit", line.Credit);
            cmd.ExecuteNonQuery();
        }
    }

    public static void Reverse(SqliteConnection connection, SqliteTransaction transaction,
        string referenceType, long referenceId)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT line.AccountCode,line.BankAccountId,line.Credit,line.Debit
            FROM JournalLines line JOIN JournalEntries entry ON entry.Id=line.EntryId
            WHERE entry.ReferenceType=$type AND entry.ReferenceId=$id AND entry.EntryType='Original';
            """;
        cmd.Parameters.AddWithValue("$type", referenceType);
        cmd.Parameters.AddWithValue("$id", referenceId);
        using var reader = cmd.ExecuteReader();
        var lines = new List<JournalLine>();
        while (reader.Read()) lines.Add(new JournalLine(reader.GetString(0),
            Convert.ToDecimal(reader.GetValue(2)), Convert.ToDecimal(reader.GetValue(3)),
            reader.IsDBNull(1) ? null : reader.GetInt64(1)));
        if (lines.Count > 0) Post(connection, transaction, referenceType, referenceId, "Reversal", lines.ToArray());
    }

    public decimal Balance(string accountCode)
    {
        UserSession.Require("Admin");
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(Debit-Credit),0) FROM JournalLines WHERE AccountCode=$code";
        cmd.Parameters.AddWithValue("$code", accountCode);
        return Convert.ToDecimal(cmd.ExecuteScalar());
    }
}
