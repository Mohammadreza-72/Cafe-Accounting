using CafeArian.Data;
using CafeArian.Models;
using CafeArian.Services;
using ClosedXML.Excel;
using System.IO;
using System.Reflection;

internal static class Program
{
    private static int _checks;
    private static readonly ProductService Products = new();
    private static readonly SaleService Sales = new();
    private static readonly OperationsService Operations = new();
    private static Product Item(long id) => Products.Search("").Single(x => x.Id == id);
    private static long Sell(long id) => Sales.CreateSale([new CartItem
        { ProductId = id, ProductName = Item(id).Name, UnitPrice = Item(id).SalePrice }], 0, null, Item(id).SalePrice, 0);
    private static decimal Scalar(string sql)
    {
        using var connection = Database.OpenConnection(); using var cmd = connection.CreateCommand();
        cmd.CommandText = sql; return Convert.ToDecimal(cmd.ExecuteScalar());
    }
    private static void Check(bool value, string label)
    {
        if (!value) throw new Exception("FAIL: " + label);
        _checks++; Console.WriteLine("PASS: " + label);
    }
    private static void Reject(Action action, string label)
    {
        try { action(); } catch (InvalidOperationException) { Check(true, label); return; }
        throw new Exception("FAIL: " + label);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Usage: workflow-test <work-directory>");
        var folder = Path.Combine(Path.GetFullPath(args[0]), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("CAFEARIAN_DB_PATH", Path.Combine(folder, "workflow.db"));
        try
        {
            Database.Initialize(); SeedData.Initialize();
            new UserService().CreateInitialAdmin("workflow-admin", Guid.NewGuid().ToString("N"));
            var journal = new JournalService();
            var repeat = Products.Save(null, "Repeated stock", null, 1000, 100, 0, stockAddition: 1);
            Products.Save(repeat, "Repeated stock", null, 1000, 100, 0, stockAddition: 2);
            Check(Item(repeat).Stock == 3 && Scalar("SELECT COUNT(*) FROM JournalEntries WHERE ReferenceType='StockEntry'") == 2,
                "Repeated positive stock entries have independent journals");
            Products.Save(repeat, "Repeated stock", null, 1000, 100, 0);
            Check(Item(repeat).Stock == 3 && Scalar("SELECT COUNT(*) FROM JournalEntries WHERE ReferenceType='StockEntry'") == 2,
                "Editing without stock does not create another entry");

            var before = journal.Balance("1300");
            var returned = Products.Save(null, "Returned stock", null, 1000, 100, 0, stockAddition: 1);
            var sale = Sell(returned);
            Operations.RecordPurchase(returned, "Test supplier", "cost-new", 1, 300);
            Sales.CancelSale(sale);
            Check(Item(returned).Stock == 2 && Item(returned).AverageCost == 200 && journal.Balance("1300") - before == 400,
                "Cancellation after a price change restores weighted cost and ledger value");
            Check(Scalar($"SELECT UnitCost FROM InventoryTransactions WHERE ReferenceType='Sale' AND ReferenceId={sale} AND TransactionType='SaleCancellation'") == 100,
                "Cancellation movement preserves the original unit cost");

            var raw = Products.Save(null, "Recipe material", null, 0, 100, 0, 2, "liter", stockAddition: 1);
            var prepared = Products.Save(null, "Recipe drink", null, 1000, 0, 0, 3);
            new RecipeService().SaveItem(prepared, raw, 1);
            var preparedSale = Sell(prepared);
            Operations.RecordPurchase(raw, "Test supplier", "raw-new", 1, 300);
            Sales.CancelSale(preparedSale);
            Check(Item(raw).Stock == 2 && Item(raw).AverageCost == 200, "Recipe cancellation restores ingredient value");
            Reject(() => Products.Save(raw, "Recipe material", null, 0, 100, 0, 2, "milliliter"),
                "Changing a used base unit is rejected");
            Check(Item(raw).UnitName == "liter" && new RecipeService().GetItems(prepared).Single().Quantity == 1,
                "Rejected unit edit preserves inventory and recipe");
            var unused = Products.Save(null, "Unused", null, 0, 0, 0);
            Products.Save(unused, "Unused", null, 0, 0, 0, unitName: "box");
            Check(Item(unused).UnitName == "box", "An unused product can correct its unit");

            var batches = new BatchService();
            var cold = Products.Save(null, "Dated stock", null, 1000, 0, 0, 4);
            batches.Register(cold, "early", null, "Test", DateTime.Today, DateTime.Today.AddDays(1), 1, 100);
            batches.Register(cold, "later", null, "Test", DateTime.Today, DateTime.Today.AddDays(2), 1, 300);
            var coldSale = Sell(cold);
            Check(Item(cold).Stock == 1 && Item(cold).AverageCost == 300, "FEFO consumption updates remaining batch value");
            Sales.CancelSale(coldSale);
            Check(Item(cold).Stock == 2 && Item(cold).AverageCost == 200, "Batch cancellation restores quantity and value");
            batches.Discard(batches.All().Single(x => x.ProductId == cold && x.BatchNumber == "early").Id);
            Check(Item(cold).AverageCost == 300, "Discard updates remaining batch value");

            var returnedBatch = Products.Save(null, "Returned batch", null, 1000, 0, 0, 4);
            batches.Register(returnedBatch, "return-discard", null, "Test", DateTime.Today, DateTime.Today.AddDays(1), 2, 101);
            var returnedBatchSale = Sell(returnedBatch);
            var returnedBatchId = batches.All().Single(x => x.ProductId == returnedBatch).Id;
            batches.Discard(returnedBatchId);
            Sales.CancelSale(returnedBatchSale);
            batches.Discard(returnedBatchId);
            Check(Item(returnedBatch).OnHand == 0, "A returned batch can be discarded again with a separate journal");

            var decimalBatch = Products.Save(null, "Decimal cost", null, 1000, 0, 0, 4);
            batches.Register(decimalBatch, "decimal-first", null, "Test", DateTime.Today, DateTime.Today.AddDays(1), 1, 100);
            batches.Register(decimalBatch, "decimal-second", null, "Test", DateTime.Today, DateTime.Today.AddDays(2), 1, 301);
            Sales.CancelSale(Sell(decimalBatch));
            Check(Item(decimalBatch).AverageCost == 200.5m, "Batch average preserves a fractional cost after cancellation");

            var fractional = Products.Save(null, "Fractional", null, 0, 1000, 0, 2, stockAddition: .3m);
            var portion = Products.Save(null, "Portion", null, 1000, 0, 0, 3);
            new RecipeService().SaveItem(portion, fractional, .1m);
            var first = Sell(portion); Sell(portion); Sell(portion);
            Check(Item(fractional).Stock == 0 && Item(portion).Stock == 0, "Three 0.1 portions consume exactly 0.3");
            Reject(() => Sell(portion), "A fourth portion cannot oversell");
            Sales.CancelSale(first); Sell(portion);
            Check(Item(fractional).Stock == 0, "Fractional stock can be cancelled and sold again");
            Reject(() => Products.Save(null, "Too precise", null, 0, 0, 0, stockAddition: .0000001m),
                "Unsupported precision is rejected instead of silently rounded");
            var fractionBatch = Products.Save(null, "Fraction production", null, 1000, 0, 0, 4);
            new RecipeService().SaveItem(fractionBatch, fractional, .1m);
            Operations.RecordPurchase(fractional, "Test supplier", "fraction-receipt", .3m, 1000);
            batches.Register(fractionBatch, "three-portions", null, "Kitchen", DateTime.Today, DateTime.Today.AddDays(1), 3, 0, true);
            Check(Item(fractional).Stock == 0 && Item(fractionBatch).Stock == 3, "Production uses the same quantity precision");

            var shared = Products.Save(null, "Shared material", null, 0, 100, 0, 2, stockAddition: 1);
            var other = Products.Save(null, "Other drink", null, 1000, 0, 0, 3);
            var another = Products.Save(null, "Another drink", null, 1000, 0, 0, 3);
            new RecipeService().SaveItem(other, shared, 1); new RecipeService().SaveItem(another, shared, 1);
            Reject(() => Sales.ValidateCartStock([new CartItem { ProductId = other }, new CartItem { ProductId = another }]),
                "Shared ingredients are validated across the whole cart");
            Check(Item(shared).Stock == 1, "Cart preview never consumes stock");

            var supplier = new SupplierService().Add("Unique supplier", null, "", "", "");
            var lines = new[] { new PurchaseLine { ProductId = repeat, Quantity = 1, UnitCost = 100 } };
            Operations.RecordPurchase(lines, supplier, "INV-۱۲");
            var stockBefore = Item(repeat).Stock;
            Reject(() => Operations.RecordPurchase(lines, supplier, "inv-12"), "Repeated supplier reference rejects normalized digits and case");
            Check(Item(repeat).Stock == stockBefore, "Duplicate rejection does not add stock");
            Operations.RecordPurchase(lines, supplier, "inv-12", allowDuplicate: true);
            Check(Item(repeat).Stock == stockBefore + 1 && Scalar("SELECT COUNT(*) FROM AuditLog WHERE Action='DuplicatePurchaseAccepted'") == 1,
                "An explicit duplicate override is audited");

            var fractionReport = Products.Save(null, "Fraction report", null, 1000, 100, 0, stockAddition: .25m);
            var reportPath = Path.Combine(folder, "report.xlsx");
            new ReportService().ExportExcel(reportPath);
            using (var workbook = new XLWorkbook(reportPath))
            {
                var stock = workbook.Worksheet("موجودی");
                var row = stock.RowsUsed().Single(x => x.Cell(1).GetString() == "Fraction report");
                Check(row.Cell(4).GetValue<decimal>() == .25m && row.Cell(5).GetValue<decimal>() == .25m,
                    "Inventory export preserves fractional recorded and sellable stock");
                var recipeRow = stock.RowsUsed().Single(x => x.Cell(1).GetString() == "Recipe drink");
                Check(recipeRow.Cell(4).GetValue<decimal>() == 0 && recipeRow.Cell(5).GetValue<decimal>() == 2,
                    "Prepared stock export distinguishes recorded and available portions");
                var payment = workbook.Worksheet("پرداخت").RowsUsed().Single(x => x.Cell(1).GetString() == $"AR-{sale:D6}");
                Check(payment.Cell(4).GetValue<decimal>() == 1000 && payment.Cell(5).GetString().Contains("لغوشده") && payment.Cell(6).GetValue<decimal>() == 0,
                    "Cancelled payments retain original amount and show zero accounting net");
            }
            var display = (string)typeof(ReportService).GetMethod("Display", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [.25m, true])!;
            Check(display.Contains("25"), "PDF quantity formatter retains fractional digits");
            new ReportService().ExportPdf(Path.Combine(folder, "report.pdf"));
            Check(File.ReadAllBytes(Path.Combine(folder, "report.pdf")).Take(4).SequenceEqual("%PDF"u8.ToArray()), "PDF report still renders");
            Check(DiagnosticsService.RunChecks().Single(x => x.Name == "ارزش موجودی").Status == "سالم",
                "Stock movement values reconcile after all workflows");
            Check(Scalar("SELECT COUNT(*) FROM (SELECT EntryId FROM JournalLines GROUP BY EntryId HAVING ABS(SUM(Debit-Credit))>0.001)") == 0,
                "Every new financial entry balances");
            // Model an old database with finer precision without modifying any real backup.
            using (var connection = Database.OpenConnection())
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"UPDATE Inventory SET Quantity=1.0000001, AverageCost=777 WHERE ProductId={unused}";
                cmd.ExecuteNonQuery();
            }
            Reject(() => Operations.AdjustStock(unused, 1, "Precision test"), "Legacy fine precision is blocked before writing");
            Check(Scalar($"SELECT Quantity FROM Inventory WHERE ProductId={unused}") == 1.0000001m,
                "Rejected operation does not round or rewrite legacy stock");
            var diagnostics = DiagnosticsService.RunChecks();
            Check(diagnostics.Single(x => x.Name == "دقت مقدار").Status == "هشدار" &&
                diagnostics.Single(x => x.Name == "ارزش موجودی").Status == "هشدار",
                "Diagnostics flags old precision and inconsistent stock value");
            Console.WriteLine($"Workflow checks passed: {_checks}. Evidence: {folder}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
