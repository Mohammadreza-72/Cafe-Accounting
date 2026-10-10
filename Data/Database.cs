using Microsoft.Data.Sqlite;
using System.IO;
using CafeArian.Services;

namespace CafeArian.Data;

public static class Database
{
    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CafeArian");

    public static string DbPath => Environment.GetEnvironmentVariable("CAFEARIAN_DB_PATH")
        ?? Path.Combine(Folder, "cafe-arian.db");

    public static SqliteConnection OpenConnection()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(DbPath))!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            ForeignKeys = true,
            DefaultTimeout = 5
        }.ToString());
        connection.CreateFunction<long?>("current_user_id", () => UserSession.Current?.Id);
        connection.Open();
        return connection;
    }

    public static void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = """
        PRAGMA foreign_keys = ON;

        CREATE TABLE IF NOT EXISTS Categories (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE,
            IsActive INTEGER NOT NULL DEFAULT 1,
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS Products (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            CategoryId INTEGER,
            Name TEXT NOT NULL,
            SKU TEXT,
            Barcode TEXT UNIQUE,
            ProductType INTEGER NOT NULL DEFAULT 1,
            SalePrice NUMERIC NOT NULL DEFAULT 0,
            CostPrice NUMERIC NOT NULL DEFAULT 0,
            UnitName TEXT NOT NULL DEFAULT 'عدد',
            MinimumStock NUMERIC NOT NULL DEFAULT 0,
            IsActive INTEGER NOT NULL DEFAULT 1,
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(CategoryId) REFERENCES Categories(Id)
        );

        CREATE TABLE IF NOT EXISTS Inventory (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProductId INTEGER NOT NULL UNIQUE,
            Quantity NUMERIC NOT NULL DEFAULT 0,
            AverageCost NUMERIC NOT NULL DEFAULT 0,
            UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(ProductId) REFERENCES Products(Id)
        );

        CREATE TABLE IF NOT EXISTS Customers (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            FullName TEXT NOT NULL DEFAULT '',
            Mobile TEXT NOT NULL UNIQUE,
            TotalOrders INTEGER NOT NULL DEFAULT 0,
            TotalPurchase NUMERIC NOT NULL DEFAULT 0,
            TotalDiscount NUMERIC NOT NULL DEFAULT 0,
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS Sales (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            InvoiceNumber TEXT NOT NULL UNIQUE,
            CustomerId INTEGER,
            SaleDate TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            SubTotal NUMERIC NOT NULL,
            DiscountAmount NUMERIC NOT NULL DEFAULT 0,
            TaxAmount NUMERIC NOT NULL DEFAULT 0,
            FeeAmount NUMERIC NOT NULL DEFAULT 0,
            FinalAmount NUMERIC NOT NULL,
            Status TEXT NOT NULL DEFAULT 'Completed',
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(CustomerId) REFERENCES Customers(Id)
        );

        CREATE TABLE IF NOT EXISTS SaleItems (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            SaleId INTEGER NOT NULL,
            ProductId INTEGER NOT NULL,
            Quantity NUMERIC NOT NULL,
            UnitPrice NUMERIC NOT NULL,
            CostPrice NUMERIC NOT NULL DEFAULT 0,
            Discount NUMERIC NOT NULL DEFAULT 0,
            TotalPrice NUMERIC NOT NULL,
            FOREIGN KEY(SaleId) REFERENCES Sales(Id),
            FOREIGN KEY(ProductId) REFERENCES Products(Id)
        );

        CREATE TABLE IF NOT EXISTS PaymentMethods (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE,
            IsActive INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS BankAccounts (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            BankName TEXT NOT NULL,
            AccountTitle TEXT NOT NULL DEFAULT '',
            AccountNumber TEXT NOT NULL DEFAULT '',
            CardNumber TEXT NOT NULL DEFAULT '',
            IsActive INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS POSDevices (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            BankAccountId INTEGER REFERENCES BankAccounts(Id),
            TerminalNumber TEXT NOT NULL DEFAULT '',
            IsActive INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS Payments (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            SaleId INTEGER NOT NULL,
            PaymentMethodId INTEGER NOT NULL,
            Amount NUMERIC NOT NULL,
            BankAccountId INTEGER REFERENCES BankAccounts(Id),
            PosDeviceId INTEGER REFERENCES POSDevices(Id),
            ReferenceNumber TEXT,
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(SaleId) REFERENCES Sales(Id),
            FOREIGN KEY(PaymentMethodId) REFERENCES PaymentMethods(Id)
        );

        CREATE TABLE IF NOT EXISTS InventoryTransactions (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProductId INTEGER NOT NULL,
            TransactionType TEXT NOT NULL,
            Quantity NUMERIC NOT NULL,
            UnitCost NUMERIC NOT NULL DEFAULT 0,
            ReferenceType TEXT,
            ReferenceId INTEGER,
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(ProductId) REFERENCES Products(Id)
        );

        CREATE TABLE IF NOT EXISTS Recipes (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProductId INTEGER NOT NULL UNIQUE REFERENCES Products(Id),
            IsActive INTEGER NOT NULL DEFAULT 1,
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS RecipeItems (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            RecipeId INTEGER NOT NULL REFERENCES Recipes(Id),
            IngredientProductId INTEGER NOT NULL REFERENCES Products(Id),
            Quantity NUMERIC NOT NULL CHECK(Quantity > 0),
            UNIQUE(RecipeId, IngredientProductId)
        );

        CREATE TABLE IF NOT EXISTS ProductBatches (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProductId INTEGER NOT NULL REFERENCES Products(Id),
            BatchNumber TEXT NOT NULL,
            Barcode TEXT UNIQUE,
            Source TEXT NOT NULL,
            ProducedAt TEXT NOT NULL,
            ExpiresAt TEXT NOT NULL,
            Quantity NUMERIC NOT NULL CHECK(Quantity >= 0),
            UnitCost NUMERIC NOT NULL CHECK(UnitCost >= 0),
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UNIQUE(ProductId, BatchNumber)
        );

        CREATE TABLE IF NOT EXISTS SaleBatchAllocations (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            SaleId INTEGER NOT NULL REFERENCES Sales(Id),
            BatchId INTEGER NOT NULL REFERENCES ProductBatches(Id),
            Quantity NUMERIC NOT NULL CHECK(Quantity > 0)
        );

        CREATE TABLE IF NOT EXISTS BarcodeLabels (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProductId INTEGER NOT NULL REFERENCES Products(Id),
            BatchId INTEGER REFERENCES ProductBatches(Id),
            Barcode TEXT NOT NULL UNIQUE,
            PrintedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PrintCount INTEGER NOT NULL DEFAULT 0 CHECK(PrintCount >= 0)
        );

        CREATE TABLE IF NOT EXISTS Discounts (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Code TEXT NOT NULL UNIQUE COLLATE NOCASE,
            Type TEXT NOT NULL CHECK(Type IN ('Percent','Fixed')),
            Value NUMERIC NOT NULL CHECK(Value > 0),
            MinimumPurchase NUMERIC NOT NULL DEFAULT 0 CHECK(MinimumPurchase >= 0),
            StartDate TEXT,
            EndDate TEXT,
            UsageLimit INTEGER CHECK(UsageLimit > 0),
            IsActive INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS DiscountUsages (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            DiscountId INTEGER NOT NULL REFERENCES Discounts(Id),
            CustomerId INTEGER REFERENCES Customers(Id),
            SaleId INTEGER NOT NULL UNIQUE REFERENCES Sales(Id),
            Amount NUMERIC NOT NULL CHECK(Amount >= 0),
            UsedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS Suppliers (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Mobile TEXT,
            Company TEXT NOT NULL DEFAULT '',
            Address TEXT NOT NULL DEFAULT '',
            Notes TEXT NOT NULL DEFAULT '',
            IsActive INTEGER NOT NULL DEFAULT 1,
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS SaleChargeSettings (
            Id INTEGER PRIMARY KEY CHECK(Id=1),
            TaxMode TEXT NOT NULL DEFAULT 'Disabled',
            TaxValue NUMERIC NOT NULL DEFAULT 0,
            TaxBase TEXT NOT NULL DEFAULT 'AfterDiscount',
            FeeMode TEXT NOT NULL DEFAULT 'Disabled',
            FeeValue NUMERIC NOT NULL DEFAULT 0,
            FeeBase TEXT NOT NULL DEFAULT 'AfterDiscount',
            RoundingMode TEXT NOT NULL DEFAULT 'HalfUp'
        );
        INSERT OR IGNORE INTO SaleChargeSettings(Id) VALUES(1);

        CREATE TABLE IF NOT EXISTS PrintSettings (
            Id INTEGER PRIMARY KEY CHECK(Id=1),
            ReceiptPrinter TEXT NOT NULL DEFAULT '',
            LabelPrinter TEXT NOT NULL DEFAULT '',
            ReceiptWidthMm INTEGER NOT NULL DEFAULT 80 CHECK(ReceiptWidthMm BETWEEN 40 AND 120),
            LabelWidthMm INTEGER NOT NULL DEFAULT 50 CHECK(LabelWidthMm BETWEEN 25 AND 120)
        );
        INSERT OR IGNORE INTO PrintSettings(Id) VALUES(1);

        CREATE TABLE IF NOT EXISTS Users (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Username TEXT NOT NULL UNIQUE COLLATE NOCASE,
            PasswordSalt BLOB NOT NULL,
            PasswordHash BLOB NOT NULL,
            Iterations INTEGER NOT NULL,
            Role TEXT NOT NULL CHECK(Role IN ('Admin','Cashier','Inventory')),
            IsActive INTEGER NOT NULL DEFAULT 1,
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS LocalProfile (
            Id INTEGER PRIMARY KEY CHECK(Id=1),
            UserId INTEGER NOT NULL REFERENCES Users(Id),
            DisplayName TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Purchases (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            SupplierName TEXT NOT NULL,
            InvoiceNumber TEXT,
            TotalAmount NUMERIC NOT NULL CHECK(TotalAmount >= 0),
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS JournalEntries (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ReferenceType TEXT NOT NULL,
            ReferenceId INTEGER NOT NULL,
            EntryType TEXT NOT NULL CHECK(EntryType IN ('Original','Reversal')),
            UserId INTEGER REFERENCES Users(Id),
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UNIQUE(ReferenceType, ReferenceId, EntryType)
        );
        CREATE TABLE IF NOT EXISTS JournalLines (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            EntryId INTEGER NOT NULL REFERENCES JournalEntries(Id),
            AccountCode TEXT NOT NULL,
            BankAccountId INTEGER REFERENCES BankAccounts(Id),
            Debit NUMERIC NOT NULL DEFAULT 0 CHECK(Debit >= 0),
            Credit NUMERIC NOT NULL DEFAULT 0 CHECK(Credit >= 0),
            CHECK((Debit > 0 AND Credit = 0) OR (Credit > 0 AND Debit = 0))
        );

        CREATE TABLE IF NOT EXISTS PurchaseItems (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            PurchaseId INTEGER NOT NULL REFERENCES Purchases(Id),
            ProductId INTEGER NOT NULL REFERENCES Products(Id),
            Quantity NUMERIC NOT NULL CHECK(Quantity > 0),
            UnitCost NUMERIC NOT NULL CHECK(UnitCost >= 0)
        );
        CREATE TABLE IF NOT EXISTS PurchasePayments (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            PurchaseId INTEGER NOT NULL REFERENCES Purchases(Id),
            Amount NUMERIC NOT NULL CHECK(Amount > 0),
            PaymentKind TEXT NOT NULL CHECK(PaymentKind IN ('Cash','Bank')),
            BankAccountId INTEGER REFERENCES BankAccounts(Id),
            UserId INTEGER REFERENCES Users(Id),
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );
        CREATE INDEX IF NOT EXISTS IX_PurchasePayments_PurchaseId ON PurchasePayments(PurchaseId);

        CREATE TABLE IF NOT EXISTS Expenses (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Description TEXT NOT NULL,
            Amount NUMERIC NOT NULL CHECK(Amount > 0),
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS ExpenseCategories (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE COLLATE NOCASE,
            IsActive INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS AuditLog (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Action TEXT NOT NULL,
            ReferenceType TEXT NOT NULL,
            ReferenceId INTEGER NOT NULL,
            Details TEXT,
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE INDEX IF NOT EXISTS IX_Customers_Mobile ON Customers(Mobile);
        CREATE INDEX IF NOT EXISTS IX_Products_Barcode ON Products(Barcode);
        CREATE INDEX IF NOT EXISTS IX_Sales_SaleDate ON Sales(SaleDate);
        CREATE INDEX IF NOT EXISTS IX_Sales_CustomerId ON Sales(CustomerId);
        CREATE INDEX IF NOT EXISTS IX_InventoryTransactions_ProductId ON InventoryTransactions(ProductId);
        CREATE INDEX IF NOT EXISTS IX_ProductBatches_ProductExpiry ON ProductBatches(ProductId, ExpiresAt);
        """;

        command.ExecuteNonQuery();
        EnsureColumn(connection, "Sales", "TaxAmount", "NUMERIC NOT NULL DEFAULT 0");
        EnsureColumn(connection, "RecipeItems", "IsUnmeasured", "INTEGER NOT NULL DEFAULT 0 CHECK(IsUnmeasured IN (0,1))");
        EnsureColumn(connection, "Sales", "FeeAmount", "NUMERIC NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Payments", "BankAccountId", "INTEGER REFERENCES BankAccounts(Id)");
        EnsureColumn(connection, "Payments", "PosDeviceId", "INTEGER REFERENCES POSDevices(Id)");
        EnsureColumn(connection, "Purchases", "SupplierId", "INTEGER REFERENCES Suppliers(Id)");
        EnsureColumn(connection, "Purchases", "PaymentKind", "TEXT NOT NULL DEFAULT 'Unpaid'");
        EnsureColumn(connection, "Purchases", "BankAccountId", "INTEGER REFERENCES BankAccounts(Id)");
        EnsureColumn(connection, "Expenses", "PaymentKind", "TEXT NOT NULL DEFAULT 'Cash'");
        EnsureColumn(connection, "Expenses", "BankAccountId", "INTEGER REFERENCES BankAccounts(Id)");
        EnsureColumn(connection, "Sales", "UserId", "INTEGER REFERENCES Users(Id)");
        EnsureColumn(connection, "Expenses", "UserId", "INTEGER REFERENCES Users(Id)");
        EnsureColumn(connection, "Expenses", "CategoryId", "INTEGER REFERENCES ExpenseCategories(Id)");
        EnsureColumn(connection, "AuditLog", "UserId", "INTEGER REFERENCES Users(Id)");
        using var actorTrigger = connection.CreateCommand();
        actorTrigger.CommandText = """
            CREATE TRIGGER IF NOT EXISTS AuditLogActor AFTER INSERT ON AuditLog
            WHEN NEW.UserId IS NULL AND current_user_id() IS NOT NULL
            BEGIN
                UPDATE AuditLog SET UserId=current_user_id() WHERE Id=NEW.Id;
            END;
            """;
        actorTrigger.ExecuteNonQuery();
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $column";
        check.Parameters.AddWithValue("$column", column);
        if (Convert.ToInt32(check.ExecuteScalar()) != 0) return;
        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }
}
