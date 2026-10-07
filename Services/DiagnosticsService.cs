using CafeArian.Data;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace CafeArian.Services;

public sealed class DiagnosticEvent
{
    public string Id { get; set; } = "";
    public string Time { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Message { get; set; } = "";
    public string Details { get; set; } = "";
}

public sealed class DiagnosticCheck
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string Details { get; set; } = "";
    public string Advice { get; set; } = "";
}

public static class DiagnosticsService
{
    private static readonly object Sync = new();
    private static readonly List<DiagnosticEvent> Memory = new();
    public static string LogFolder => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Database.DbPath))!, "Logs");

    public static DiagnosticEvent Record(Exception error, string operation)
    {
        var now = DateTimeOffset.Now;
        var entry = new DiagnosticEvent
        {
            Id = "CA-" + now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture) + "-" +
                 Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            Time = now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            Operation = operation,
            Message = error.Message,
            Details = error.ToString()
        };
        lock (Sync)
        {
            Memory.Add(entry);
            if (Memory.Count > 100) Memory.RemoveAt(0);
            try
            {
                Directory.CreateDirectory(LogFolder);
                var file = Path.Combine(LogFolder, "errors-" + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".jsonl");
                File.AppendAllText(file, JsonSerializer.Serialize(entry) + Environment.NewLine, Encoding.UTF8);
                foreach (var old in Directory.GetFiles(LogFolder, "errors-????????.jsonl")
                    .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(30))
                    File.Delete(old);
            }
            catch (Exception)
            {
                // Keep the error in memory if the log directory is unavailable.
            }
        }
        return entry;
    }

    public static IReadOnlyList<DiagnosticEvent> RecentErrors()
    {
        UserSession.Require("Admin");
        lock (Sync)
        {
            var entries = new List<DiagnosticEvent>(Memory);
            try
            {
                if (Directory.Exists(LogFolder))
                {
                    foreach (var file in Directory.GetFiles(LogFolder, "errors-????????.jsonl")
                        .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Take(30))
                    {
                        foreach (var line in File.ReadLines(file).TakeLast(100))
                        {
                            try
                            {
                                var entry = JsonSerializer.Deserialize<DiagnosticEvent>(line);
                                if (entry is not null) entries.Add(entry);
                            }
                            catch (JsonException) { }
                        }
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return entries.DistinctBy(x => x.Id).OrderByDescending(x => x.Time)
                .ThenByDescending(x => x.Id).Take(100).ToList();
        }
    }

    public static IReadOnlyList<DiagnosticCheck> RunChecks()
    {
        UserSession.Require("Admin");
        var checks = new List<DiagnosticCheck>();
        Add(checks, "پایگاه داده", () =>
        {
            using var connection = Database.OpenConnection();
            var integrity = ScalarText(connection, "PRAGMA quick_check;");
            return Result("پایگاه داده", integrity == "ok" ? "سالم" : "خطا",
                integrity == "ok" ? "بررسی ساختار SQLite موفق بود." : integrity);
        });
        Add(checks, "کلیدهای ارتباطی", () =>
        {
            using var connection = Database.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_key_check;";
            using var reader = cmd.ExecuteReader();
            return Result("کلیدهای ارتباطی", reader.Read() ? "خطا" : "سالم",
                reader.HasRows ? "رکوردهای مرتبط ناسازگارند؛ پیش از تغییر داده‌ها پشتیبان بگیرید." :
                    "رکورد ناسازگار پیدا نشد.");
        });
        Add(checks, "رکورد موجودی", () => CountCheck("رکورد موجودی", """
            SELECT COUNT(*) FROM Products p LEFT JOIN Inventory i ON i.ProductId=p.Id
            WHERE p.IsActive=1 AND i.ProductId IS NULL;
            """, "محصول فعال بدون رکورد موجودی"));
        Add(checks, "موجودی منفی", () => CountCheck("موجودی منفی", """
            SELECT (SELECT COUNT(*) FROM Inventory WHERE Quantity < -0.000001) +
                   (SELECT COUNT(*) FROM ProductBatches WHERE Quantity < -0.000001);
            """, "رکورد با مقدار منفی"));
        Add(checks, "بارکد تکراری", () => CountCheck("بارکد تکراری", """
            SELECT COUNT(*) FROM (
                SELECT Code FROM (
                    SELECT lower(Barcode) AS Code FROM Products WHERE Barcode IS NOT NULL
                    UNION ALL SELECT lower(Barcode) FROM ProductBatches WHERE Barcode IS NOT NULL
                ) GROUP BY Code HAVING COUNT(*) > 1
            );
            """, "بارکد تکراری با تفاوت حروف بزرگ و کوچک"));
        Add(checks, "کالای فروشی بدون موجودی", () => ProductCheck("کالای فروشی بدون موجودی", """
            SELECT p.Id,p.Name FROM Products p LEFT JOIN Inventory i ON i.ProductId=p.Id
            WHERE p.IsActive=1 AND p.ProductType=1 AND COALESCE(i.Quantity,0)<=0;
            """, "کالای فروشی با موجودی صفر یا منفی؛ از «کالا و موجودی» مقدار ورود را ثبت کنید", "هشدار"));
        Add(checks, "کالای بچ‌دار بدون بچ", () => ProductCheck("کالای بچ‌دار بدون بچ", """
            SELECT p.Id,p.Name FROM Products p WHERE p.IsActive=1 AND p.ProductType=4
              AND NOT EXISTS(SELECT 1 FROM ProductBatches b WHERE b.ProductId=p.Id AND b.Quantity>0
                             AND b.ExpiresAt>=date('now','localtime'));
            """, "کالای بچ‌دار بدون بچ قابل فروش؛ اگر کالای بسته‌بندی معمولی است، نوع آن را به «فروشی ساده» تغییر دهید", "هشدار"));
        Add(checks, "شناسه SKU", () => CountCheck("شناسه SKU", """
            SELECT COUNT(*) FROM Products WHERE IsActive=1 AND (SKU IS NULL OR trim(SKU)='');
            """, "کالای فعال بدون شناسه SKU", "هشدار"));
        Add(checks, "تطبیق حرکت موجودی", () => ProductCheck("تطبیق حرکت موجودی", """
            SELECT p.Id,p.Name FROM Inventory i JOIN Products p ON p.Id=i.ProductId
            WHERE ABS(i.Quantity - COALESCE((SELECT SUM(t.Quantity) FROM InventoryTransactions t
                                             WHERE t.ProductId=i.ProductId),0)) > 0.000001;
            """, "محصول با اختلاف بین موجودی و جمع حرکت‌ها", "هشدار"));
        Add(checks, "ارزش موجودی", () => ProductCheck("ارزش موجودی", """
            SELECT p.Id,p.Name FROM Inventory i JOIN Products p ON p.Id=i.ProductId
            WHERE p.ProductType IN (1,2,4) AND ABS(i.Quantity*i.AverageCost-
                COALESCE((SELECT SUM(t.Quantity*t.UnitCost) FROM InventoryTransactions t WHERE t.ProductId=p.Id),0))>0.01;
            """, "اختلاف ارزش موجودی با گردش‌ها؛ ممکن است از لغوهای نسخهٔ قدیمی یا دادهٔ مهاجرت‌یافته باشد. قبل از اصلاح بها پشتیبان بگیرید", "هشدار"));
        Add(checks, "دقت مقدار", () => ProductCheck("دقت مقدار", """
            SELECT p.Id,p.Name FROM Products p JOIN Inventory i ON i.ProductId=p.Id
            WHERE ABS(i.Quantity-ROUND(i.Quantity,6))>0.000000001;
            """, "موجودی قدیمی با بیش از شش رقم اعشار؛ واحد و دقت را پیش از عملیات بررسی کنید", "هشدار"));
        Add(checks, "تطبیق بچ", () => ProductCheck("تطبیق بچ", """
            SELECT p.Id,p.Name FROM Products p JOIN Inventory i ON i.ProductId=p.Id
            WHERE p.ProductType=4 AND ABS(i.Quantity - COALESCE((SELECT SUM(b.Quantity)
                FROM ProductBatches b WHERE b.ProductId=p.Id),0)) > 0.000001;
            """, "محصول یخچالی با اختلاف موجودی و جمع بچ‌ها", "خطا"));
        Add(checks, "بچ منقضی", () =>
        {
            using var connection = Database.OpenConnection();
            var count = ScalarLong(connection, """
                SELECT COUNT(*) FROM ProductBatches
                WHERE Quantity>0 AND ExpiresAt<date('now','localtime');
                """);
            return Result("بچ منقضی", count == 0 ? "سالم" : "اطلاع",
                count == 0 ? "بچ منقضی دارای موجودی پیدا نشد." :
                    $"{count} بچ منقضی هنوز موجودی ثبت‌شده دارد و قابل فروش نیست.");
        });
        Add(checks, "دستور تهیه", () =>
        {
            using var connection = Database.OpenConnection();
            var count = ScalarLong(connection, """
                SELECT COUNT(*) FROM Products p WHERE p.IsActive=1 AND p.ProductType=3
                  AND NOT EXISTS(SELECT 1 FROM Recipes r JOIN RecipeItems ri ON ri.RecipeId=r.Id
                                 WHERE r.ProductId=p.Id AND r.IsActive=1);
                """);
            return Result("دستور تهیه", count == 0 ? "سالم" : "هشدار",
                count == 0 ? "همهٔ محصولات آماده‌شونده دستور تهیه دارند." :
                    $"{count} محصول آماده‌شونده بدون دستور تهیه است و قابل فروش نیست.");
        });
        Add(checks, "مقدار دستور تهیه", () => CountCheck("مقدار دستور تهیه", """
            SELECT COUNT(*) FROM RecipeItems WHERE Quantity <= 0;
            """, "قلم دستور تهیه با مقدار نامعتبر"));
        Add(checks, "فضای ذخیره‌سازی", () =>
        {
            var path = Path.GetPathRoot(Path.GetFullPath(Database.DbPath))!;
            var drive = new DriveInfo(path);
            var freeMb = drive.AvailableFreeSpace / 1024 / 1024;
            return Result("فضای ذخیره‌سازی", freeMb < 512 ? "هشدار" : "سالم",
                $"فضای آزاد درایو پایگاه داده: {freeMb:N0} مگابایت.");
        });
        Add(checks, "پوشهٔ خطاها", () =>
        {
            Directory.CreateDirectory(LogFolder);
            var probe = Path.Combine(LogFolder, ".probe-" + Guid.NewGuid().ToString("N"));
            try { File.WriteAllText(probe, "ok"); }
            finally { if (File.Exists(probe)) File.Delete(probe); }
            return Result("پوشهٔ خطاها", "سالم", "ثبت خطا در کنار پایگاه داده ممکن است.");
        });
        Add(checks, "پشتیبان خودکار", () =>
        {
            var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Database.DbPath))!, "Backups");
            var backups = Directory.Exists(folder) ? Directory.GetFiles(folder, "CafeArian-auto-????????.db") : [];
            DateTime? latest = backups.Length == 0 ? null : backups.Max(File.GetLastWriteTime);
            return Result("پشتیبان خودکار", latest is null || latest < DateTime.Now.AddDays(-2) ? "هشدار" : "سالم",
                latest is null ? "پشتیبان خودکار پیدا نشد." : $"آخرین پشتیبان: {latest:yyyy-MM-dd HH:mm}.");
        });
        return checks;
    }

    public static string BuildReport()
    {
        UserSession.Require("Admin");
        var text = new StringBuilder();
        text.AppendLine("Cafe Arian diagnostics");
        text.AppendLine($"Time: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        text.AppendLine($"Version: {typeof(DiagnosticsService).Assembly.GetName().Version}");
        text.AppendLine($"Windows: {Environment.OSVersion}");
        text.AppendLine($"Database: {Path.GetFullPath(Database.DbPath)}");
        text.AppendLine($"Logs: {LogFolder}");
        text.AppendLine("This report does not include the database, passwords, customer lists, or backups.");
        text.AppendLine();
        foreach (var check in RunChecks())
            text.AppendLine($"[{check.Status}] {check.Name}: {check.Details} {check.Advice}");
        text.AppendLine();
        foreach (var entry in RecentErrors().Take(50))
        {
            text.AppendLine($"[{entry.Time}] {entry.Id} {entry.Operation}: {entry.Message}");
            text.AppendLine(entry.Details);
            text.AppendLine();
        }
        return text.ToString();
    }

    private static DiagnosticCheck CountCheck(string name, string query, string label, string severity = "خطا")
    {
        using var connection = Database.OpenConnection();
        var count = ScalarLong(connection, query);
        return Result(name, count == 0 ? "سالم" : severity,
            count == 0 ? "موردی پیدا نشد." : $"{count} {label}. داده‌ها خودکار تغییر داده نشدند.");
    }

    private static DiagnosticCheck ProductCheck(string name, string query, string label, string severity)
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = query;
        using var reader = cmd.ExecuteReader();
        var count = 0;
        var samples = new List<string>();
        while (reader.Read())
        {
            count++;
            if (samples.Count < 8) samples.Add($"{reader.GetInt64(0)}: {reader.GetString(1)}");
        }
        return Result(name, count == 0 ? "سالم" : severity,
            count == 0 ? "موردی پیدا نشد." :
                $"{count} {label}. نمونه‌ها: {string.Join("، ", samples)}. داده‌ها خودکار تغییر داده نشدند.");
    }

    private static void Add(List<DiagnosticCheck> checks, string name, Func<DiagnosticCheck> run)
    {
        try { checks.Add(run()); }
        catch (Exception ex)
        {
            Record(ex, "بررسی " + name);
            checks.Add(Result(name, "خطا", "بررسی انجام نشد: " + ex.Message));
        }
    }

    private static DiagnosticCheck Result(string name, string status, string details) =>
        new()
        {
            Name = name, Status = status, Details = details,
            Advice = status == "سالم" ? "" : name switch
            {
                "پایگاه داده" or "کلیدهای ارتباطی" or "رکورد موجودی" or "موجودی منفی" =>
                    "پیش از هر تغییر پشتیبان بگیرید و گزارش را به پشتیبانی بدهید.",
                "بارکد تکراری" => "بارکد کالاها و بچ‌ها را بررسی و شناسهٔ تکراری را اصلاح کنید.",
                "تطبیق حرکت موجودی" =>
                    "خریدها، فروش‌ها و اصلاح‌های دستی را بررسی کنید. سوابق قدیمی ممکن است گردش کامل نداشته باشند.",
                "تطبیق بچ" => "بچ‌ها، فروش و ضایعات این کالا را بررسی کنید؛ قبل از اصلاح پشتیبان بگیرید.",
                "بچ منقضی" => "موجودی منقضی را در صفحهٔ بچ و انقضا به‌عنوان ضایعات ثبت کنید.",
                "دستور تهیه" or "مقدار دستور تهیه" => "دستور تهیه و مقدار مواد هر محصول را اصلاح کنید.",
                "فضای ذخیره‌سازی" => "برای ثبت فروش و پشتیبان‌گیری، در درایو داده‌ها فضا آزاد کنید.",
                "پوشهٔ خطاها" => "دسترسی نوشتن به پوشهٔ کنار دیتابیس را بررسی کنید.",
                "پشتیبان خودکار" => "از صفحهٔ پشتیبان‌گیری یک نسخهٔ دستی در محل امن ذخیره کنید.",
                _ => "شناسه و گزارش خطا را برای بررسی نگه دارید."
            }
        };

    private static long ScalarLong(SqliteConnection connection, string query)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = query;
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string ScalarText(SqliteConnection connection, string query)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = query;
        return Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "";
    }
}
