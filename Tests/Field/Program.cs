using CafeArian.Data;
using CafeArian.Models;
using CafeArian.Services;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Security.Cryptography;

if (args.Length != 2) throw new ArgumentException("Usage: field-test <source-backup.db> <work-directory>");
var source = Path.GetFullPath(args[0]);
var workDirectory = Path.GetFullPath(args[1]);
Directory.CreateDirectory(workDirectory);
var runDirectory = Path.Combine(workDirectory,
    "field-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(runDirectory);
var testDb = Path.Combine(runDirectory, "field-copy.db");
if (string.Equals(source, testDb, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("The source backup must not be the test copy.");
File.Copy(source, testDb);
Environment.SetEnvironmentVariable("CAFEARIAN_DB_PATH", testDb);
var checks = 0;

void Check(bool condition, string label)
{
    if (!condition) throw new Exception($"FAIL: {label}");
    Console.WriteLine($"PASS: {label}");
    checks++;
}

long Scalar(string sql)
{
    using var connection = Database.OpenConnection();
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToInt64(command.ExecuteScalar());
}

void MustReject(Action action, string label)
{
    try { action(); }
    catch (InvalidOperationException) { Check(true, label); return; }
    Check(false, label);
}

try
{
    Database.Initialize();
    SeedData.Initialize();
    Check(Scalar("SELECT COUNT(*) FROM Products") == 4 &&
          Scalar("SELECT COUNT(*) FROM InventoryTransactions") == 0 &&
          Scalar("SELECT COUNT(*) FROM ProductBatches") == 0,
          "Backup baseline has products but no stock movements or batches");

    // This randomly generated administrator exists only in the disposable copy.
    var username = "field-" + Guid.NewGuid().ToString("N")[..12];
    var password = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    var salt = RandomNumberGenerator.GetBytes(32);
    var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
    using (var connection = Database.OpenConnection())
    using (var command = connection.CreateCommand())
    {
        command.CommandText = """
            INSERT INTO Users(Username,PasswordSalt,PasswordHash,Iterations,Role)
            VALUES($username,$salt,$hash,210000,'Admin');
            """;
        command.Parameters.AddWithValue("$username", username);
        command.Parameters.AddWithValue("$salt", salt);
        command.Parameters.AddWithValue("$hash", hash);
        command.ExecuteNonQuery();
    }
    var users = new UserService();
    Check(users.SignIn(username, password)?.Role == "Admin" &&
          users.Authenticate(username, "invalid") is null,
          "Administrator sign-in accepts only the generated credential");

    var products = new ProductService();
    var sales = new SaleService();
    var operations = new OperationsService();
    var customers = new CustomerService();
    var candidates = products.Search("").Where(x => x.ProductType == 4).OrderBy(x => x.Id).ToList();
    Check(candidates.Count == 2 && candidates.All(x => x.Stock == 0),
          "Packaged items reproduce the zero sellable stock complaint");
    Check(DiagnosticsService.RunChecks().Any(x => x.Name == "کالای بچ‌دار بدون بچ" && x.Status == "هشدار"),
          "Diagnostics explains the missing-batch condition");
    var first = candidates[0];
    var second = candidates[1];
    MustReject(() => sales.CreateSale(new[] { new CartItem
    {
        ProductId = first.Id, ProductName = first.Name, Quantity = 1, UnitPrice = first.SalePrice
    } }, 0, null, first.SalePrice, 0), "Sale without a batch is rejected atomically");
    Check(Scalar("SELECT COUNT(*) FROM Sales") == 0, "Failed sale leaves no invoice");

    products.Save(first.Id, first.Name, first.Barcode, first.SalePrice, first.CostPrice,
        first.MinimumStock, 1, first.UnitName, stockAddition: 5);
    products.Save(second.Id, second.Name, second.Barcode, second.SalePrice, second.CostPrice,
        second.MinimumStock, 1, second.UnitName, stockAddition: 4);
    first = products.Search("").Single(x => x.Id == first.Id);
    second = products.Search("").Single(x => x.Id == second.Id);
    Check(first.Stock == 5 && second.Stock == 4 && first.Sku is not null && second.Sku is not null &&
          Scalar("SELECT COUNT(*) FROM InventoryTransactions WHERE TransactionType='Opening'") == 2,
          "Simple stock conversion creates identifiers and traceable opening movements");
    Check(new RecipeService().GetItems(first.Id).Count == 0 &&
          DiagnosticsService.RunChecks().Any(x => x.Name == "کالای بچ‌دار بدون بچ" && x.Status == "سالم"),
          "Old recipe is inactive and batch warning clears after conversion");

    var mobile = customers.EnsureMobile("+۹۸ (۹۱۲) ۳۴۵-۶۷۸۹");
    customers.Save("Field customer", mobile);
    Check(mobile == "09123456789" && customers.Search(mobile).Count == 1,
          "Formatted Persian number creates one customer before checkout");
    var total = first.SalePrice + second.SalePrice;
    var saleId = sales.CreateSale(new[]
    {
        new CartItem { ProductId = first.Id, ProductName = first.Name, Quantity = 1, UnitPrice = first.SalePrice },
        new CartItem { ProductId = second.Id, ProductName = second.Name, Quantity = 1, UnitPrice = second.SalePrice }
    }, 0, mobile, total, 0);
    Check(products.Search("").Single(x => x.Id == first.Id).Stock == 4 &&
          products.Search("").Single(x => x.Id == second.Id).Stock == 3 &&
          customers.Search(mobile).Single().TotalOrders == 1,
          "Checkout decreases both stocks and updates the customer");
    Check(Scalar("SELECT COUNT(*) FROM (SELECT EntryId FROM JournalLines GROUP BY EntryId HAVING ABS(SUM(Debit-Credit))>0.001)") == 0 &&
          operations.Sales().Any(x => x.Id == saleId),
          "Sales and opening-stock journal entries balance");
    MustReject(() => sales.CreateSale(new[] { new CartItem
    {
        ProductId = first.Id, ProductName = first.Name, Quantity = 99, UnitPrice = first.SalePrice
    } }, 0, null, first.SalePrice * 99, 0), "Overselling is rejected");

    var supplier = new SupplierService().All().First();
    var purchaseId = operations.RecordPurchase(new[] { new PurchaseLine
    {
        ProductId = second.Id, ProductName = second.Name, Quantity = 3, UnitCost = 90_000
    } }, supplier.Id, "FIELD-1");
    Check(products.Search("").Single(x => x.Id == second.Id).Stock == 6 &&
          operations.Purchases().Single(x => x.Id == purchaseId).Due == 270_000,
          "Purchase increases stock and supplier debt once");
    operations.PayPurchase(purchaseId, 100_000, "Cash");
    Check(operations.Purchases().Single(x => x.Id == purchaseId).Due == 170_000,
          "Partial supplier settlement reduces debt");

    var milk = products.Search("").Single(x => x.ProductType == 2 && x.Id == 4);
    products.Save(milk.Id, milk.Name, milk.Barcode, milk.SalePrice, milk.CostPrice,
        milk.MinimumStock, 2, milk.UnitName, stockAddition: 100);
    var preparedId = products.Save(null, "Field prepared drink", null, 300_000, 0, 0, 3);
    var prepared = products.Search("").Single(x => x.Id == preparedId);
    Check(prepared.Stock == 0, "Prepared item is unavailable before recipe setup");
    var recipes = new RecipeService();
    recipes.SaveItem(preparedId, milk.Id, 2);
    Check(products.Search("").Single(x => x.Id == preparedId).Stock == 50,
          "Recipe availability follows ingredient stock");
    var recipeSale = sales.CreateSale(new[] { new CartItem
    {
        ProductId = preparedId, ProductName = prepared.Name, Quantity = 3, UnitPrice = prepared.SalePrice
    } }, 0, null, 900_000, 0);
    Check(products.Search("").Single(x => x.Id == milk.Id).Stock == 94,
          "Prepared sale consumes exact ingredient quantity");
    sales.CancelSale(recipeSale);
    Check(products.Search("").Single(x => x.Id == milk.Id).Stock == 100 &&
          Scalar("SELECT COUNT(*) FROM Sales WHERE Status='Cancelled'") == 1,
          "Cancellation restores ingredient stock and records invoice status");

    var charges = new ChargeSettingsService();
    charges.Save(new ChargeSettings
    {
        TaxMode = "Percent", TaxValue = 10, TaxBase = "AfterDiscount",
        FeeMode = "Fixed", FeeValue = 2_000, FeeBase = "AfterDiscount"
    });
    var discounts = new DiscountService();
    discounts.Add("FIELD5000", "Fixed", 5_000, 0, null, null, 1);
    var quotedDiscount = discounts.Quote("FIELD5000", second.SalePrice);
    var quotedCharges = charges.Quote(second.SalePrice, quotedDiscount, true, true);
    Check(quotedDiscount == 5_000 && quotedCharges.Tax == 11_500 && quotedCharges.Fee == 2_000,
          "Tax, fee and discount preview uses the configured post-discount basis");
    var chargedTotal = second.SalePrice - quotedDiscount + quotedCharges.Tax + quotedCharges.Fee;
    var chargedSale = sales.CreateSale(new[] { new CartItem
    {
        ProductId = second.Id, ProductName = second.Name, Quantity = 1, UnitPrice = second.SalePrice
    } }, 0, null, chargedTotal, 0, discountCode: "FIELD5000",
        applyConfiguredTax: true, applyConfiguredFee: true);
    Check(Scalar($"SELECT FinalAmount FROM Sales WHERE Id={chargedSale}") == chargedTotal &&
          Scalar($"SELECT COUNT(*) FROM DiscountUsages WHERE SaleId={chargedSale}") == 1,
          "Checkout recalculates charges and records one discount use");
    MustReject(() => discounts.Quote("FIELD5000", second.SalePrice),
        "Discount usage limit is enforced");
    sales.CancelSale(chargedSale);
    Check(discounts.Quote("FIELD5000", second.SalePrice) == 5_000,
          "Cancelling the invoice releases its discount use");

    var cashierName = "cash-" + Guid.NewGuid().ToString("N")[..12];
    var cashierPassword = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    users.Add(cashierName, cashierPassword, "Cashier");
    Check(users.SignIn(cashierName, cashierPassword)?.Role == "Cashier",
          "Cashier role can sign in");
    MustReject(() => products.Save(null, "Forbidden product", null, 100, 0, 0),
        "Cashier cannot create products");
    MustReject(() => operations.RecordPurchase(new[] { new PurchaseLine
    {
        ProductId = second.Id, ProductName = second.Name, Quantity = 1, UnitCost = 10
    } }, supplier.Id, "FORBIDDEN"), "Cashier cannot create purchases");
    var cashierMobile = customers.EnsureMobile("09120000000");
    var cashierSale = sales.CreateSale(new[] { new CartItem
    {
        ProductId = second.Id, ProductName = second.Name, Quantity = 1, UnitPrice = second.SalePrice
    } }, 0, cashierMobile, second.SalePrice, 0);
    Check(customers.Search(cashierMobile).Single().TotalOrders == 1,
          "Cashier can register a customer and complete a sale");
    MustReject(() => sales.CancelSale(cashierSale),
        "Cashier cannot cancel a completed invoice");
    users.SignIn(username, password);

    Check(Scalar("SELECT COUNT(*) FROM (SELECT EntryId FROM JournalLines GROUP BY EntryId HAVING ABS(SUM(Debit-Credit))>0.001)") == 0 &&
          DiagnosticsService.RunChecks().All(x => x.Status != "خطا"),
          "Role, coupon, cancellation and purchase flows leave balanced diagnostics");

    var backupPath = Path.Combine(runDirectory, "field-result-backup.db");
    new BackupService().Create(backupPath);
    var savedCustomers = Scalar("SELECT COUNT(*) FROM Customers");
    customers.Save("Temporary", "09129999999");
    new BackupService().Restore(backupPath);
    Check(File.Exists(backupPath) && Scalar("SELECT COUNT(*) FROM Customers") == savedCustomers &&
          Scalar("SELECT COUNT(*) FROM Sales WHERE Id=" + cashierSale) == 1,
          "Backup restores the copied database without losing recorded sales");
    Console.WriteLine($"Field checks passed: {checks}. Source backup was never modified. Test copy: {runDirectory}");
}
finally
{
    UserSession.Logout();
    SqliteConnection.ClearAllPools();
}
