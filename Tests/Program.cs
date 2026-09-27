using CafeArian.Data;
using CafeArian.Models;
using CafeArian.Services;
using System.IO;
using System.Globalization;

var db = Path.Combine(Path.GetTempPath(), $"cafe-arian-test-{Guid.NewGuid():N}.db");
var backupPath = db + ".backup";
var autoBackupDirectory = db + ".auto-backups";
var legacyPath = db + ".legacy";
Environment.SetEnvironmentVariable("CAFEARIAN_DB_PATH", db);
var originalCulture = CultureInfo.CurrentCulture;
var persianCulture = (CultureInfo)CultureInfo.GetCultureInfo("fa-IR").Clone();
persianCulture.DateTimeFormat.Calendar = new PersianCalendar();
CultureInfo.CurrentCulture = persianCulture;
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
    var autoBackup = backups.AutoBackupIfNeeded(autoBackupDirectory);
    Check(File.Exists(autoBackup) && backups.AutoBackupIfNeeded(autoBackupDirectory) == autoBackup,
        "Daily automatic backup was not created idempotently.");
    Check(Path.GetFileName(autoBackup) == "CafeArian-auto-" +
          DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".db",
        "Automatic backup filename used the Persian calendar.");
    for (var day = 1; day <= 8; day++)
        File.Copy(backupPath, Path.Combine(autoBackupDirectory,
            "CafeArian-auto-" + DateTime.Today.AddDays(-day).ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".db"));
    var manualCopy = Path.Combine(autoBackupDirectory, "manual.db");
    File.Copy(backupPath, manualCopy);
    backups.AutoBackupIfNeeded(autoBackupDirectory);
    Check(Directory.GetFiles(autoBackupDirectory, "CafeArian-auto-????????.db").Length == 7 &&
          File.Exists(autoBackup) && File.Exists(manualCopy),
        "Automatic backup retention removed wrong files.");

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

    products.Save(null, "دسر یخچالی", "COLD-1", 100, 0, 0, 4);
    var cold = products.FindByBarcode("COLD-1") ?? throw new Exception("Cold product missing.");
    var batches = new BatchService();
    batches.Register(cold.Id, "OLD", "LOT-OLD", "آشپزخانه", DateTime.Today.AddDays(-3),
        DateTime.Today.AddDays(-1), 2, 8);
    batches.Register(cold.Id, "FIRST", "LOT-1", "آشپزخانه", DateTime.Today,
        DateTime.Today.AddDays(1), 2, 10);
    batches.Register(cold.Id, "SECOND", "LOT-2", "آشپزخانه", DateTime.Today,
        DateTime.Today.AddDays(5), 3, 20);
    Check(products.Search("").Single(x => x.Id == cold.Id).Stock == 5 &&
          products.FindByBarcode("LOT-OLD") is null,
        "Expired batch was treated as sellable.");
    Fails(() => operations.RecordPurchase(cold.Id, "تأمین‌کننده", null, 1, 20),
        "Ordinary purchase bypassed batch tracking.");
    Fails(() => operations.AdjustStock(cold.Id, 1, "اصلاح"),
        "Manual adjustment bypassed batch tracking.");
    Fails(() => products.Save(cold.Id, cold.Name, cold.Barcode, 100, 0, 0, 1),
        "Product with batches was converted to ordinary stock.");
    var coldSale = sales.CreateSale(new[] { new CartItem { ProductId = cold.Id,
        ProductName = cold.Name, Quantity = 3, UnitPrice = 100 } }, 0, null, 300, 0);
    Check(batches.All().Single(x => x.BatchNumber == "FIRST").Quantity == 0 &&
          batches.All().Single(x => x.BatchNumber == "SECOND").Quantity == 2 &&
          Scalar($"SELECT COUNT(*) FROM SaleBatchAllocations WHERE SaleId={coldSale}") == 2 &&
          Scalar($"SELECT SUM(-Quantity*UnitCost) FROM InventoryTransactions WHERE ReferenceType='Sale' AND ReferenceId={coldSale}") == 40,
        "FEFO batch allocation or cost tracking failed.");
    var selected = products.FindByBarcode("LOT-2") ?? throw new Exception("Valid batch barcode missing.");
    var selectedSale = sales.CreateSale(new[] { new CartItem { ProductId = cold.Id,
        BatchId = selected.BatchId, ProductName = cold.Name, Quantity = 1, UnitPrice = 100 } },
        0, null, 100, 0);
    Check(batches.All().Single(x => x.BatchNumber == "SECOND").Quantity == 1,
        "Barcode-selected batch was not consumed.");
    Fails(() => sales.CreateSale(new[] { new CartItem { ProductId = cold.Id,
        ProductName = cold.Name, Quantity = 2, UnitPrice = 100 } }, 0, null, 200, 0),
        "Batch oversell was accepted.");
    Check(batches.All().Single(x => x.BatchNumber == "SECOND").Quantity == 1,
        "Failed batch sale changed quantity.");
    sales.CancelSale(coldSale);
    sales.CancelSale(selectedSale);
    Check(batches.All().Single(x => x.BatchNumber == "FIRST").Quantity == 2 &&
          batches.All().Single(x => x.BatchNumber == "SECOND").Quantity == 3,
        "Cancellation did not restore the exact batches.");
    var mixedSale = sales.CreateSale(new[]
    {
        new CartItem { ProductId = cold.Id, BatchId = selected.BatchId, ProductName = cold.Name,
            Quantity = 1, UnitPrice = 100 },
        new CartItem { ProductId = cold.Id, ProductName = cold.Name,
            Quantity = 4, UnitPrice = 100 }
    }, 0, null, 500, 0);
    Check(products.Search("").Single(x => x.Id == cold.Id).Stock == 0 &&
          Scalar($"SELECT SUM(Quantity) FROM SaleBatchAllocations WHERE SaleId={mixedSale}") == 5,
        "Mixed barcode-selected and generic batch sale failed.");
    sales.CancelSale(mixedSale);
    batches.Discard(batches.All().Single(x => x.BatchNumber == "OLD").Id);
    Check(batches.All().Single(x => x.BatchNumber == "OLD").Quantity == 0 &&
          products.Search("").Single(x => x.Id == cold.Id).Stock == 5 &&
          operations.Dashboard().TodayInventoryAdjustmentCost >= 16,
        "Expired batch disposal did not adjust inventory and report.");

    products.Save(null, "کیک تولیدی", "CAKE-1", 700, 0, 0, 4);
    var cake = products.FindByBarcode("CAKE-1") ?? throw new Exception("Cake product missing.");
    recipes.SaveItem(cake.Id, rawCoffee.Id, 20);
    recipes.SaveItem(cake.Id, milk.Id, 100);
    batches.Register(cake.Id, "MADE-1", "CAKE-LOT-1", "آشپزخانه", DateTime.Today,
        DateTime.Today.AddDays(2), 2, 0, fromRecipe: true);
    Check(products.Search("").Single(x => x.Id == rawCoffee.Id).Stock == 60 &&
          products.Search("").Single(x => x.Id == milk.Id).Stock == 800 &&
          batches.All().Single(x => x.BatchNumber == "MADE-1").UnitCost == 400 &&
          Scalar("SELECT COUNT(*) FROM InventoryTransactions WHERE TransactionType='ProductionConsumption'") == 2,
        "Batch production did not consume recipe ingredients or calculate cost.");
    Fails(() => batches.Register(cake.Id, "MADE-FAILED", null, "آشپزخانه", DateTime.Today,
        DateTime.Today.AddDays(2), 4, 0, fromRecipe: true),
        "Production exceeded ingredient stock.");
    Check(batches.All().All(x => x.BatchNumber != "MADE-FAILED") &&
          products.Search("").Single(x => x.Id == rawCoffee.Id).Stock == 60,
        "Failed production was not rolled back.");
    var cakeSale = sales.CreateSale(new[] { new CartItem { ProductId = cake.Id,
        ProductName = cake.Name, Quantity = 1, UnitPrice = 700 } }, 0, null, 700, 0);
    Check(Scalar($"SELECT CostPrice FROM SaleItems WHERE SaleId={cakeSale}") == 400,
        "Produced batch cost was not carried into sale.");
    sales.CancelSale(cakeSale);
    Check(products.Search("").Single(x => x.Id == cake.Id).Stock == 2 &&
          products.Search("").Single(x => x.Id == rawCoffee.Id).Stock == 60,
        "Cancelling a batch sale changed production consumption.");

    var discounts = new DiscountService();
    discounts.Add("SAVE10", "Percent", 10, 500,
        DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1), 1);
    Check(discounts.Quote("save10", 700) == 70,
        "Percent coupon quote is incorrect.");
    Fails(() => discounts.Quote("SAVE10", 400), "Minimum purchase was ignored.");
    var couponSale = sales.CreateSale(new[] { new CartItem { ProductId = cake.Id,
        ProductName = cake.Name, Quantity = 1, UnitPrice = 700 } },
        0, null, 630, 0, discountCode: "SAVE10");
    Check(Scalar($"SELECT DiscountAmount FROM Sales WHERE Id={couponSale}") == 70 &&
          discounts.All().Single(x => x.Code == "SAVE10").UsedCount == 1,
        "Coupon was not applied or usage was not recorded.");
    Fails(() => sales.CreateSale(new[] { new CartItem { ProductId = cake.Id,
        ProductName = cake.Name, Quantity = 1, UnitPrice = 700 } },
        0, null, 630, 0, discountCode: "SAVE10"),
        "Coupon usage limit was ignored.");
    sales.CancelSale(couponSale);
    Check(discounts.All().Single(x => x.Code == "SAVE10").UsedCount == 0 &&
          products.Search("").Single(x => x.Id == cake.Id).Stock == 2,
        "Cancellation did not release coupon usage and stock.");
    Fails(() => sales.CreateSale(new[] { new CartItem { ProductId = cake.Id,
        ProductName = cake.Name, Quantity = 1, UnitPrice = 700 } },
        0, null, 700, 0, discountCode: "SAVE10"),
        "Incorrect payment for a discounted sale was accepted.");
    Check(discounts.All().Single(x => x.Code == "SAVE10").UsedCount == 0,
        "Rejected sale consumed a coupon use.");
    discounts.Add("FIXED", "Fixed", 100, 0, null, null, null);
    var fixedSale = sales.CreateSale(new[] { new CartItem { ProductId = cake.Id,
        ProductName = cake.Name, Quantity = 1, UnitPrice = 700 } },
        0, null, 600, 0, discountCode: "FIXED");
    Check(Scalar($"SELECT DiscountAmount FROM Sales WHERE Id={fixedSale}") == 100,
        "Fixed coupon was not applied.");
    sales.CancelSale(fixedSale);
    discounts.Deactivate(discounts.All().Single(x => x.Code == "FIXED").Id);
    Fails(() => discounts.Quote("FIXED", 700), "Inactive coupon was accepted.");
    discounts.Add("FUTURE", "Fixed", 10, 0,
        DateTime.Today.AddDays(1), null, null);
    Fails(() => discounts.Quote("FUTURE", 700), "Future coupon was accepted.");
    Check(operations.TopProducts().Any(x => x.Name == product.Name && x.Quantity == 2),
        "Top products report did not count completed sales correctly.");

    var suppliers = new SupplierService();
    var supplierId = suppliers.Add("تأمین‌کننده چندقلمی", "۰۹۱۲۱۱۱۱۱۱۱", "شرکت", "تهران", "آزمون");
    products.Save(null, "آرد", "FLOUR-1", 0, 0, 0, 2, "کیلوگرم");
    products.Save(null, "شربت", "SYRUP-1", 0, 0, 0, 2, "لیتر");
    var flour = products.Search("").Single(x => x.Barcode == "FLOUR-1");
    var syrup = products.Search("").Single(x => x.Barcode == "SYRUP-1");
    var multiPurchase = operations.RecordPurchase(new[]
    {
        new PurchaseLine { ProductId = flour.Id, ProductName = flour.Name, Quantity = 10, UnitCost = 2 },
        new PurchaseLine { ProductId = syrup.Id, ProductName = syrup.Name, Quantity = 4, UnitCost = 3 }
    }, supplierId, "MULTI-1");
    Check(Scalar($"SELECT COUNT(*) FROM PurchaseItems WHERE PurchaseId={multiPurchase}") == 2 &&
          Scalar($"SELECT TotalAmount FROM Purchases WHERE Id={multiPurchase}") == 32 &&
          products.Search("").Single(x => x.Id == flour.Id).Stock == 10 &&
          suppliers.All().Single().Mobile == "09121111111",
        "Multi-item purchase or supplier normalization failed.");
    Fails(() => operations.RecordPurchase(new[]
    {
        new PurchaseLine { ProductId = flour.Id, ProductName = flour.Name, Quantity = 1, UnitCost = 2 },
        new PurchaseLine { ProductId = cold.Id, ProductName = cold.Name, Quantity = 1, UnitCost = 1 }
    }, supplierId, "FAIL-MULTI"), "Invalid second purchase line was accepted.");
    Check(products.Search("").Single(x => x.Id == flour.Id).Stock == 10 &&
          Scalar("SELECT COUNT(*) FROM Purchases WHERE InvoiceNumber='FAIL-MULTI'") == 0,
        "Failed multi-item purchase was not rolled back.");
    suppliers.Deactivate(supplierId);
    Fails(() => operations.RecordPurchase(new[]
    {
        new PurchaseLine { ProductId = flour.Id, ProductName = flour.Name, Quantity = 1, UnitCost = 2 }
    }, supplierId, "INACTIVE"), "Inactive supplier was accepted.");

    var charges = new ChargeSettingsService();
    Check(charges.Quote(700, 100, true, true) == new ChargeQuote(0, 0),
        "Default charge settings were not disabled.");
    charges.Save(new ChargeSettings
    {
        TaxMode = "Percent", TaxValue = 10, TaxBase = "AfterDiscount",
        FeeMode = "Fixed", FeeValue = 5, FeeBase = "BeforeDiscount",
        RoundingMode = "HalfUp"
    });
    Check(charges.Quote(700, 100, true, true) == new ChargeQuote(60, 5) &&
          charges.Quote(700, 100, false, true) == new ChargeQuote(0, 5),
        "Configured charge calculation or per-sale choice failed.");
    var chargedSale = sales.CreateSale(new[] { new CartItem { ProductId = cake.Id,
        ProductName = cake.Name, Quantity = 1, UnitPrice = 700 } },
        100, null, 665, 0, applyConfiguredTax: true, applyConfiguredFee: true);
    Check(Scalar($"SELECT TaxAmount+FeeAmount FROM Sales WHERE Id={chargedSale}") == 65 &&
          Scalar($"SELECT FinalAmount FROM Sales WHERE Id={chargedSale}") == 665,
        "Sale did not use configured charges.");
    sales.CancelSale(chargedSale);
    charges.Save(new ChargeSettings
    {
        TaxMode = "Percent", TaxValue = 1, TaxBase = "BeforeDiscount",
        FeeMode = "Disabled", RoundingMode = "Ceiling"
    });
    Check(charges.Quote(101, 99, true, false) == new ChargeQuote(2, 0),
        "Before-discount base or ceiling rounding failed.");
    Fails(() => charges.Save(new ChargeSettings { TaxMode = "Percent", TaxValue = 101 }),
        "Invalid configured percentage was accepted.");

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
    Check(Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Discounts','DiscountUsages')") == 2,
        "Discount tables were not added to existing databases.");
    Check(Scalar("SELECT COUNT(*) FROM pragma_table_info('Purchases') WHERE name='SupplierId'") == 1,
        "Supplier migration was not applied to existing purchases.");
    Check(Scalar("SELECT COUNT(*) FROM SaleChargeSettings WHERE Id=1 AND TaxMode='Disabled'") == 1,
        "Default charge settings were not created on migration.");
    Console.WriteLine("Smoke checks passed: sales, charges, discounts, suppliers, multi-item purchases, batches, inventory, cancellation, backup.");
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    if (File.Exists(db)) File.Delete(db);
    if (File.Exists(backupPath)) File.Delete(backupPath);
    if (File.Exists(legacyPath)) File.Delete(legacyPath);
    if (Directory.Exists(autoBackupDirectory)) Directory.Delete(autoBackupDirectory, true);
    CultureInfo.CurrentCulture = originalCulture;
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
