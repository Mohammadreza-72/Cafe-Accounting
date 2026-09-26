using CafeArian.Data;
using CafeArian.Models;
using Microsoft.Data.Sqlite;

namespace CafeArian.Services;

public class ProductService
{
    public List<Product> Search(string text)
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
        SELECT p.Id, p.Name, COALESCE(p.Barcode,''), 
               COALESCE(c.Name,''), p.SalePrice, p.CostPrice,
               COALESCE(i.Quantity,0), p.MinimumStock, p.IsActive
        FROM Products p
        LEFT JOIN Categories c ON c.Id = p.CategoryId
        LEFT JOIN Inventory i ON i.ProductId = p.Id
        WHERE p.IsActive = 1
          AND ($text = '' OR p.Name LIKE '%' || $text || '%' OR p.Barcode = $text)
        ORDER BY p.Name
        LIMIT 100;
        """;

        cmd.Parameters.AddWithValue("$text", text.Trim());

        using var reader = cmd.ExecuteReader();
        var result = new List<Product>();

        while (reader.Read())
        {
            result.Add(new Product
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                Barcode = reader.GetString(2),
                Category = reader.GetString(3),
                SalePrice = Convert.ToDecimal(reader.GetValue(4)),
                CostPrice = Convert.ToDecimal(reader.GetValue(5)),
                Stock = Convert.ToDecimal(reader.GetValue(6)),
                MinimumStock = Convert.ToDecimal(reader.GetValue(7)),
                IsActive = reader.GetInt64(8) == 1
            });
        }

        return result;
    }

    public Product? FindByBarcode(string barcode)
    {
        return Search(barcode).FirstOrDefault(x =>
            string.Equals(x.Barcode, barcode.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
