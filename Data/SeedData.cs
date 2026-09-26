using Microsoft.Data.Sqlite;

namespace CafeArian.Data;

public static class SeedData
{
    public static void Initialize()
    {
        using var connection = Database.OpenConnection();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
            INSERT OR IGNORE INTO Categories(Name) VALUES
            ('قهوه'), ('نوشیدنی'), ('کیک و دسر'), ('غذا');

            INSERT OR IGNORE INTO PaymentMethods(Name) VALUES
            ('نقدی'), ('کارتخوان'), ('کارت به کارت');
            """;
            cmd.ExecuteNonQuery();
        }

        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM Products";
        var productCount = Convert.ToInt64(count.ExecuteScalar());

        if (productCount > 0) return;

        AddProduct(connection, "اسپرسو", "قهوه", 85000, 30000, "100001");
        AddProduct(connection, "لاته", "قهوه", 145000, 65000, "100002");
        AddProduct(connection, "آمریکانو", "قهوه", 110000, 40000, "100003");
        AddProduct(connection, "چیزکیک", "کیک و دسر", 180000, 90000, "200001");
        AddProduct(connection, "آب معدنی", "نوشیدنی", 25000, 10000, "300001");
    }

    private static void AddProduct(
        SqliteConnection connection,
        string name,
        string category,
        decimal salePrice,
        decimal costPrice,
        string barcode)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
        INSERT INTO Products(CategoryId, Name, Barcode, SalePrice, CostPrice, MinimumStock)
        SELECT Id, $name, $barcode, $sale, $cost, 2
        FROM Categories WHERE Name = $category;

        INSERT INTO Inventory(ProductId, Quantity, AverageCost)
        SELECT Id, 20, $cost FROM Products WHERE Barcode = $barcode;
        """;

        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$category", category);
        cmd.Parameters.AddWithValue("$sale", salePrice);
        cmd.Parameters.AddWithValue("$cost", costPrice);
        cmd.Parameters.AddWithValue("$barcode", barcode);
        cmd.ExecuteNonQuery();
    }
}
