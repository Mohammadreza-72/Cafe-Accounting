using Microsoft.Data.Sqlite;
using System.IO;

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

        CREATE TABLE IF NOT EXISTS Purchases (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            SupplierName TEXT NOT NULL,
            InvoiceNumber TEXT,
            TotalAmount NUMERIC NOT NULL CHECK(TotalAmount >= 0),
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS PurchaseItems (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            PurchaseId INTEGER NOT NULL REFERENCES Purchases(Id),
            ProductId INTEGER NOT NULL REFERENCES Products(Id),
            Quantity NUMERIC NOT NULL CHECK(Quantity > 0),
            UnitCost NUMERIC NOT NULL CHECK(UnitCost >= 0)
        );

        CREATE TABLE IF NOT EXISTS Expenses (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Description TEXT NOT NULL,
            Amount NUMERIC NOT NULL CHECK(Amount > 0),
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
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
        """;

        command.ExecuteNonQuery();
        EnsureColumn(connection, "Sales", "TaxAmount", "NUMERIC NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Sales", "FeeAmount", "NUMERIC NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Payments", "BankAccountId", "INTEGER REFERENCES BankAccounts(Id)");
        EnsureColumn(connection, "Payments", "PosDeviceId", "INTEGER REFERENCES POSDevices(Id)");
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
