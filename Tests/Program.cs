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
    operations.AdjustStock(product.Id, -1, "ضایعات");
    Check(products.Search("").Single().Stock == 2, "Waste adjustment did not reduce stock.");
    Fails(() => operations.AdjustStock(product.Id, -3, "ضایعات"),
        "Adjustment was allowed to make stock negative.");
    operations.AdjustStock(product.Id, 1, "اصلاح شمارش");
    Check(products.Search("").Single().Stock == 3, "Positive adjustment did not restore stock.");
    Check(operations.Dashboard().TodayInventoryAdjustmentCost == 0,
        "Reversed stock adjustments should net to zero.");
    Fails(() => sales.CreateSale(new[] { new CartItem { ProductId = product.Id,
        ProductName = product.Name, Quantity = 1, UnitPrice = 99 } }, 0, null, 99, 0),
        "Stale product price was accepted.");

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
    var accounts = new PaymentAccountService();
    accounts.AddBank("بانک نمونه", "کافه آرین", "123", "456");
    var bank = accounts.Banks().Single();
    accounts.AddDevice("کارتخوان صندوق", bank.Id, "T-1");
    var pos = accounts.Devices().Single();
    var taxed = sales.CreateSale(new[] { new CartItem
    {
        ProductId = product.Id, ProductName = product.Name, Quantity = 1, UnitPrice = 100
    } }, 0, null, 15, 50, taxAmount: 10, feeAmount: 5, transferAmount: 50,
        bankAccountId: bank.Id, posDeviceId: pos.Id);
    Check(Scalar($"SELECT TaxAmount + FeeAmount FROM Sales WHERE Id={taxed}") == 15,
        "Tax and fee fields were not persisted.");
    Check(Scalar($"SELECT COUNT(*) FROM Payments WHERE SaleId={taxed}") == 3 &&
          Scalar($"SELECT COUNT(*) FROM Payments WHERE SaleId={taxed} AND BankAccountId={bank.Id}") == 1 &&
          Scalar($"SELECT COUNT(*) FROM Payments WHERE SaleId={taxed} AND PosDeviceId={pos.Id}") == 1,
        "Cash, POS and bank transfer allocations were not saved.");
    Check(operations.TodayPayments().GetValueOrDefault("کارت به کارت") == 50,
        "Payment report did not include bank transfers.");
    Fails(() => accounts.DeactivateBank(bank.Id), "Bank with active POS was deactivated.");
    accounts.DeactivateDevice(pos.Id);
    accounts.DeactivateBank(bank.Id);
    Execute($"DELETE FROM InventoryTransactions WHERE ReferenceType='Sale' AND ReferenceId={taxed}");
    sales.CancelSale(taxed);
    Check(products.Search("").Single().Stock == 1, "Legacy sale cancellation did not restore stock.");
    Fails(() => sales.CreateSale(new[] { new CartItem { ProductId = product.Id,
        ProductName = product.Name, Quantity = 1, UnitPrice = 100 } }, 0, null, 0, 100,
        posDeviceId: pos.Id), "Inactive POS was accepted.");
    Check(products.Search("").Single().Stock == 1, "Rejected payment did not roll back stock.");
    Check(operations.WeeklySales().Count == 7, "Weekly chart must have seven days.");

    products.Save(null, "قهوه خام", "I-1", 0, 0, 0, 2, "گرم");
    products.Save(null, "شیر", "I-2", 0, 0, 0, 2, "میلی‌لیتر");
    products.Save(null, "لاته", "P-1", 1000, 0, 0, 3);
    var coffee = products.FindByBarcode("I-1");
    Check(coffee is null, "Raw ingredient must not be available for direct sale.");
    var rawCoffee = products.Search("").Single(x => x.Barcode == "I-1");
    var milk = products.Search("").Single(x => x.Barcode == "I-2");
    var latte = products.FindByBarcode("P-1") ?? throw new Exception("Prepared product missing.");
    operations.RecordPurchase(rawCoffee.Id, "تأمین‌کننده", "I-1", 100, 10);
    operations.RecordPurchase(milk.Id, "تأمین‌کننده", "I-2", 1000, 2);
    var recipes = new RecipeService();
    recipes.SaveItem(latte.Id, rawCoffee.Id, 18.5m);
    recipes.SaveItem(latte.Id, milk.Id, 200);
    Fails(() => products.Save(rawCoffee.Id, rawCoffee.Name, rawCoffee.Barcode, 0, 0, 0, 1, "گرم"),
        "Recipe ingredient was converted to a sellable product.");
    Check(recipes.GetItems(latte.Id).Count == 2 && products.FindByBarcode("P-1")?.Stock == 5,
        "Recipe availability is incorrect.");
    var latteCart = new[] { new CartItem { ProductId = latte.Id, ProductName = latte.Name,
        Quantity = 2, UnitPrice = 1000 } };
    var latteSale = sales.CreateSale(latteCart, 0, null, 2000, 0);
    Check(Scalar($"SELECT CostPrice FROM SaleItems WHERE SaleId={latteSale}") == 585,
        "Recipe cost was not captured.");
    Check(products.Search("").Single(x => x.Id == rawCoffee.Id).Stock == 63 &&
          products.Search("").Single(x => x.Id == milk.Id).Stock == 600,
        "Recipe sale did not consume ingredient stock.");
    Fails(() => sales.CreateSale(new[] { new CartItem { ProductId = latte.Id,
        ProductName = latte.Name, Quantity = 4, UnitPrice = 1000 } }, 0, null, 4000, 0),
        "Insufficient ingredients were accepted.");
    Check(products.Search("").Single(x => x.Id == rawCoffee.Id).Stock == 63,
        "Failed recipe sale was not rolled back.");
    recipes.SaveItem(latte.Id, rawCoffee.Id, 20);
    sales.CancelSale(latteSale);
    Check(products.Search("").Single(x => x.Id == rawCoffee.Id).Stock == 100 &&
          products.FindByBarcode("P-1")?.Stock == 5,
        "Cancellation did not restore the originally consumed ingredients.");

    Environment.SetEnvironmentVariable("CAFEARIAN_DB_PATH", legacyPath);
    using (var legacy = Database.OpenConnection())
    {
        using var create = legacy.CreateCommand();
        create.CommandText = """
            CREATE TABLE Sales(Id INTEGER PRIMARY KEY, InvoiceNumber TEXT, CustomerId INTEGER,
                SaleDate TEXT, SubTotal NUMERIC, DiscountAmount NUMERIC, FinalAmount NUMERIC,
                Status TEXT, CreatedAt TEXT);
            CREATE TABLE Payments(Id INTEGER PRIMARY KEY, SaleId INTEGER, PaymentMethodId INTEGER,
                Amount NUMERIC, ReferenceNumber TEXT, CreatedAt TEXT);
            """;
        create.ExecuteNonQuery();
    }
    Database.Initialize();
    Check(Scalar("SELECT COUNT(*) FROM pragma_table_info('Sales') WHERE name IN ('TaxAmount','FeeAmount')") == 2,
        "Existing databases were not migrated.");
    Check(Scalar("SELECT COUNT(*) FROM pragma_table_info('Payments') WHERE name IN ('BankAccountId','PosDeviceId')") == 2,
        "Existing payments were not migrated.");
    Console.WriteLine("Smoke checks passed: sale, payment, customer, recipe, inventory, rollback, cancellation, backup.");
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

void Execute(string sql)
{
    using var connection = Database.OpenConnection();
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.ExecuteNonQuery();
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
