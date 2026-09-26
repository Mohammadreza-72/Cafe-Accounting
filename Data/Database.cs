using Microsoft.Data.Sqlite;
using System.IO;

namespace CafeArian.Data;

public static class Database
{
    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CafeArian");

    public static string DbPath => Path.Combine(Folder, "cafe-arian.db");

    public static SqliteConnection OpenConnection()
    {
        Directory.CreateDirectory(Folder);
        var connection = new SqliteConnection($"Data Source={DbPath};Foreign Keys=True;Busy Timeout=5000");
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

        CREATE TABLE IF NOT EXISTS Payments (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            SaleId INTEGER NOT NULL,
            PaymentMethodId INTEGER NOT NULL,
            Amount NUMERIC NOT NULL,
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

        CREATE INDEX IF NOT EXISTS IX_Customers_Mobile ON Customers(Mobile);
        CREATE INDEX IF NOT EXISTS IX_Products_Barcode ON Products(Barcode);
        CREATE INDEX IF NOT EXISTS IX_Sales_SaleDate ON Sales(SaleDate);
        """;

        command.ExecuteNonQuery();
    }
}
