using CafeArian.Data;
using CafeArian.Models;
using Microsoft.Data.Sqlite;

namespace CafeArian.Services;

public sealed class CustomerService
{
    public static string NormalizeMobile(string input)
    {
        var digits = new string(input.Trim().Select(c => c switch
        {
            >= '۰' and <= '۹' => (char)('0' + c - '۰'),
            >= '٠' and <= '٩' => (char)('0' + c - '٠'),
            _ when char.IsWhiteSpace(c) => '\0',
            '\u200c' or '\u200e' or '\u200f' or '-' or '–' or '—' or '(' or ')' => '\0',
            _ => c
        }).Where(c => c != '\0').ToArray());
        if (digits.StartsWith("+98")) digits = "0" + digits[3..];
        else if (digits.StartsWith("0098")) digits = "0" + digits[4..];
        else if (digits.StartsWith("98") && digits.Length == 12) digits = "0" + digits[2..];
        else if (digits.StartsWith('9') && digits.Length == 10) digits = "0" + digits;
        if (digits.Length != 11 || !digits.StartsWith("09") || !digits.All(char.IsAsciiDigit))
            throw new InvalidOperationException("شماره موبایل باید با ۰۹ شروع شود و ۱۱ رقم داشته باشد.");
        return digits;
    }

    public static bool TryNormalizeMobile(string input, out string mobile)
    {
        try { mobile = NormalizeMobile(input); return true; }
        catch (InvalidOperationException) { mobile = ""; return false; }
    }

    public string EnsureMobile(string mobile)
    {
        UserSession.Require("Admin", "Cashier");
        var normalized = NormalizeMobile(mobile);
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        FindOrCreate(connection, transaction, normalized);
        transaction.Commit();
        return normalized;
    }

    internal static long? FindOrCreate(SqliteConnection connection, SqliteTransaction transaction, string? mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile)) return null;
        var normalized = NormalizeMobile(mobile);
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT OR IGNORE INTO Customers(Mobile) VALUES($mobile);
            SELECT Id FROM Customers WHERE Mobile = $mobile;
            """;
        cmd.Parameters.AddWithValue("$mobile", normalized);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public List<Customer> Search(string text)
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, FullName, Mobile, TotalOrders, TotalPurchase, TotalDiscount
            FROM Customers WHERE FullName LIKE '%' || $text || '%' OR Mobile LIKE '%' || $text || '%'
            ORDER BY Id DESC LIMIT 200;
            """;
        cmd.Parameters.AddWithValue("$text", text.Trim());
        using var reader = cmd.ExecuteReader();
        var result = new List<Customer>();
        while (reader.Read())
            result.Add(new Customer
            {
                Id = reader.GetInt64(0), FullName = reader.GetString(1), Mobile = reader.GetString(2),
                TotalOrders = reader.GetInt64(3), TotalPurchase = Convert.ToDecimal(reader.GetValue(4)),
                TotalDiscount = Convert.ToDecimal(reader.GetValue(5))
            });
        return result;
    }

    public void Save(string name, string mobile)
    {
        UserSession.Require("Admin", "Cashier");
        var normalized = NormalizeMobile(mobile);
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Customers(FullName, Mobile) VALUES($name, $mobile)
            ON CONFLICT(Mobile) DO UPDATE SET FullName =
                CASE WHEN excluded.FullName <> '' THEN excluded.FullName ELSE Customers.FullName END;
            """;
        cmd.Parameters.AddWithValue("$name", name.Trim());
        cmd.Parameters.AddWithValue("$mobile", normalized);
        cmd.ExecuteNonQuery();
    }
}
