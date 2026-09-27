using CafeArian.Data;
using CafeArian.Models;
using CafeArian.Services;
using System.IO;

var db = Path.Combine(Path.GetTempPath(), $"cafe-arian-test-{Guid.NewGuid():N}.db");
var backupPath = db + ".backup";
var legacyPath = db + ".legacy";
Environment.SetEnvironmentVariable("CAFEARIAN_DB_PATH", db);
try
{
    Database.Initialize();
    SeedData.Initialize();
    var products = new ProductService();
    var operations = new OperationsService();
    var customers = new CustomerService();
    var sales = new SaleService();
    var backups = new BackupService();

    Check(products.Search("").Count == 0, "A new database must not contain demo products.");
    products.Save(null, "اسپرسو", "T-100", 100, 40, 2);
    var product = products.FindByBarcode("T-100") ?? throw new Exception("Barcode search failed.");
    operations.RecordPurchase(product.Id, "تأمین‌کننده", "P-1", 3, 30);
    Check(products.Search("").Single().Stock == 3, "Purchase did not update stock.");

    var cart = new[] { new CartItem { ProductId = product.Id, ProductName = product.Name, Quantity = 2, UnitPrice = 100 } };
    var id = sales.CreateSale(cart, 20, "۰۹۱۲۳۴۵۶۷۸۹", 80, 100);
    Check(products.Search("").Single().Stock == 1, "Sale did not reduce stock.");
    var customer = customers.Search("09123456789").Single();
    Check(customer.TotalOrders == 1 && customer.TotalPurchase == 180 && customer.TotalDiscount == 20,
        "Customer totals are incorrect.");
    Check(operations.Sales().Single().InvoiceNumber == $"AR-{id:D6}", "Invoice numbering failed.");
    Check(Scalar("SELECT COUNT(*) FROM Payments") == 2, "Split payment was not saved.");
    Check(Scalar("SELECT CostPrice FROM SaleItems") == 30, "Average purchase cost was not captured.");
    backups.Create(backupPath);
    Check(File.Exists(backupPath), "Backup was not created.");

    Fails(() => sales.CreateSale(cart, 201, null, 0, 0), "Oversized discount was accepted.");
    Fails(() => sales.CreateSale(cart, 0, null, 200, 0), "Oversell was accepted.");
    Check(Scalar("SELECT COUNT(*) FROM Sales") == 1 && products.Search("").Single().Stock == 1,
        "Failed sale was not rolled back.");

    sales.CancelSale(id);
    Check(products.Search("").Single().Stock == 3, "Cancellation did not restore stock.");
    Check(customers.Search("09123456789").Single().TotalOrders == 0, "Cancellation did not reverse customer totals.");
    Check(operations.Sales().Single().Status == "Cancelled", "Cancellation status was not saved.");
    Check(Scalar("SELECT COUNT(*) FROM AuditLog WHERE Action='Cancel'") == 1, "Cancellation was not audited.");
    Fails(() => sales.CancelSale(id), "Double cancellation was accepted.");
    backups.Restore(backupPath);
    Check(operations.Sales().Single().Status == "Completed" && products.Search("").Single().Stock == 1,
        "Restore did not recover the saved state.");
    var taxed = sales.CreateSale(new[] { new CartItem
    {
        ProductId = product.Id, ProductName = product.Name, Quantity = 1, UnitPrice = 100
    } }, 0, null, 115, 0, taxAmount: 10, feeAmount: 5);
    Check(Scalar($"SELECT TaxAmount + FeeAmount FROM Sales WHERE Id={taxed}") == 15,
        "Tax and fee fields were not persisted.");
    Check(operations.WeeklySales().Count == 7, "Weekly chart must have seven days.");
    Environment.SetEnvironmentVariable("CAFEARIAN_DB_PATH", legacyPath);
    using (var legacy = Database.OpenConnection())
    {
        using var create = legacy.CreateCommand();
        create.CommandText = """
            CREATE TABLE Sales(Id INTEGER PRIMARY KEY, InvoiceNumber TEXT, CustomerId INTEGER,
                SaleDate TEXT, SubTotal NUMERIC, DiscountAmount NUMERIC, FinalAmount NUMERIC,
                Status TEXT, CreatedAt TEXT);
            """;
        create.ExecuteNonQuery();
    }
    Database.Initialize();
    Check(Scalar("SELECT COUNT(*) FROM pragma_table_info('Sales') WHERE name IN ('TaxAmount','FeeAmount')") == 2,
        "Existing databases were not migrated.");
    Console.WriteLine("Smoke checks passed: sale, payment, customer, inventory, rollback, cancellation.");
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    if (File.Exists(db)) File.Delete(db);
    if (File.Exists(backupPath)) File.Delete(backupPath);
    if (File.Exists(legacyPath)) File.Delete(legacyPath);
    foreach (var safety in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(db) + ".before-restore-*.db"))
        if (safety.StartsWith(db + ".before-restore-", StringComparison.OrdinalIgnoreCase)) File.Delete(safety);
}

long Scalar(string sql)
{
    using var connection = Database.OpenConnection();
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToInt64(command.ExecuteScalar());
}

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

void Fails(Action action, string message)
{
    try { action(); }
    catch (InvalidOperationException) { return; }
    throw new Exception(message);
}
