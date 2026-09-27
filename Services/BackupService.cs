using CafeArian.Data;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Globalization;

namespace CafeArian.Services;

public sealed class BackupService
{
    public string AutoBackupIfNeeded(string? directory = null)
    {
        var backupDirectory = directory ?? Path.Combine(Path.GetDirectoryName(Database.DbPath)!, "Backups");
        Directory.CreateDirectory(backupDirectory);
        var target = Path.Combine(backupDirectory,
            "CafeArian-auto-" + DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".db");
        if (!File.Exists(target))
        {
            var temporary = Path.Combine(backupDirectory, ".auto-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                Create(temporary);
                SqliteConnection.ClearAllPools();
                File.Move(temporary, target);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        var backups = Directory.GetFiles(backupDirectory, "CafeArian-auto-????????.db")
            .Where(path => System.Text.RegularExpressions.Regex.IsMatch(
                Path.GetFileName(path), @"^CafeArian-auto-\d{8}\.db$"))
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(7);
        foreach (var old in backups) File.Delete(old);
        return target;
    }

    public void Create(string destinationPath)
    {
        if (SamePath(destinationPath, Database.DbPath))
            throw new InvalidOperationException("مسیر پشتیبان باید با پایگاه داده اصلی متفاوت باشد.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        using var source = Database.OpenConnection();
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath
        }.ToString());
        destination.Open();
        source.BackupDatabase(destination);
        CheckDatabase(destination);
    }

    public void Restore(string backupPath)
    {
        if (SamePath(backupPath, Database.DbPath))
            throw new InvalidOperationException("فایل انتخاب‌شده همان پایگاه داده اصلی است.");
        if (!File.Exists(backupPath)) throw new FileNotFoundException("فایل پشتیبان پیدا نشد.", backupPath);
        using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backupPath, Mode = SqliteOpenMode.ReadOnly
        }.ToString()))
        {
            backup.Open();
            CheckDatabase(backup);
        }
        var original = Database.DbPath;
        var temp = original + ".restore-" + Guid.NewGuid().ToString("N");
        var safety = original + ".before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
            "-" + Guid.NewGuid().ToString("N")[..8] + ".db";
        try
        {
            File.Copy(backupPath, temp);
            SqliteConnection.ClearAllPools();
            File.Replace(temp, original, safety);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static void CheckDatabase(SqliteConnection connection)
    {
        using var integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check";
        if (!string.Equals(Convert.ToString(integrity.ExecuteScalar()), "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("پایگاه داده پشتیبان سالم نیست.");
        using var schema = connection.CreateCommand();
        schema.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Products','Sales','Inventory','Customers')";
        if (Convert.ToInt32(schema.ExecuteScalar()) != 4)
            throw new InvalidOperationException("فایل انتخاب‌شده پشتیبان معتبر کافه آرین نیست.");
    }

    private static bool SamePath(string first, string second) =>
        Path.GetFullPath(first).Equals(Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
}
