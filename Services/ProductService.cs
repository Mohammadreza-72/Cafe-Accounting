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
               COALESCE(i.Quantity,0), p.MinimumStock, p.IsActive,
               COALESCE(i.AverageCost,p.CostPrice)
        FROM Products p
        LEFT JOIN Categories c ON c.Id = p.CategoryId
        LEFT JOIN Inventory i ON i.ProductId = p.Id
        WHERE p.IsActive = 1
          AND ($text = '' OR p.Name LIKE '%' || $text || '%' OR p.Barcode = $text)
        ORDER BY p.Name
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
                IsActive = reader.GetInt64(8) == 1,
                AverageCost = Convert.ToDecimal(reader.GetValue(9))
            });
        }

        return result;
    }

    public Product? FindByBarcode(string barcode)
    {
        return Search(barcode).FirstOrDefault(x => string.Equals(x.Barcode, barcode.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public void Save(long? id, string name, string? barcode, decimal salePrice, decimal costPrice, decimal minimumStock)
    {
        name = name.Trim();
        barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();
        if (name.Length == 0 || salePrice < 0 || costPrice < 0 || minimumStock < 0 ||
            salePrice != decimal.Truncate(salePrice) || costPrice != decimal.Truncate(costPrice))
            throw new InvalidOperationException("نام یا قیمت محصول معتبر نیست.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        string? previousPrice = null;
        if (id.HasValue)
        {
            using var previous = connection.CreateCommand();
            previous.Transaction = transaction;
            previous.CommandText = "SELECT CAST(SalePrice AS TEXT) || '/' || CAST(CostPrice AS TEXT) FROM Products WHERE Id=$id";
            previous.Parameters.AddWithValue("$id", id.Value);
            previousPrice = Convert.ToString(previous.ExecuteScalar());
        }
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = id.HasValue
            ? """
              UPDATE Products SET Name=$name, Barcode=$barcode, SalePrice=$sale,
                  CostPrice=$cost, MinimumStock=$minimum WHERE Id=$id;
              """
            : """
              INSERT INTO Products(Name, Barcode, SalePrice, CostPrice, MinimumStock)
              VALUES($name, $barcode, $sale, $cost, $minimum);
              """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$barcode", (object?)barcode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sale", (long)salePrice);
        cmd.Parameters.AddWithValue("$cost", (long)costPrice);
        cmd.Parameters.AddWithValue("$minimum", Convert.ToDouble(minimumStock));
        if (id.HasValue) cmd.Parameters.AddWithValue("$id", id.Value);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("محصول پیدا نشد.");
        var productId = id;
        if (!productId.HasValue)
        {
            using var lastId = connection.CreateCommand();
            lastId.Transaction = transaction;
            lastId.CommandText = "SELECT last_insert_rowid()";
            productId = Convert.ToInt64(lastId.ExecuteScalar());
        }
        if (!id.HasValue)
        {
            using var stock = connection.CreateCommand();
            stock.Transaction = transaction;
            stock.CommandText = "INSERT INTO Inventory(ProductId, Quantity, AverageCost) VALUES(last_insert_rowid(), 0, $cost)";
            stock.Parameters.AddWithValue("$cost", (long)costPrice);
            stock.ExecuteNonQuery();
        }
        using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = """
                INSERT INTO AuditLog(Action, ReferenceType, ReferenceId, Details)
                VALUES($action, 'Product', $id, $details);
                """;
            audit.Parameters.AddWithValue("$action", id.HasValue ? "ProductUpdated" : "ProductCreated");
            audit.Parameters.AddWithValue("$id", productId.Value);
            audit.Parameters.AddWithValue("$details", $"قیمت پیشین: {previousPrice ?? "—"}؛ قیمت جدید: {salePrice}/{costPrice}");
            audit.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void Deactivate(long id)
    {
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "UPDATE Products SET IsActive = 0 WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("محصول پیدا نشد.");
        using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = "INSERT INTO AuditLog(Action, ReferenceType, ReferenceId) VALUES('ProductDeactivated','Product',$id)";
        audit.Parameters.AddWithValue("$id", id);
        audit.ExecuteNonQuery();
        transaction.Commit();
    }
}
