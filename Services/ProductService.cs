using CafeArian.Data;
using CafeArian.Models;
using Microsoft.Data.Sqlite;

namespace CafeArian.Services;

public class ProductService
{
    public List<Product> Search(string text, bool sellableOnly = false)
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = """
        SELECT p.Id, p.Name, COALESCE(p.Barcode,''), 
               COALESCE(c.Name,''), p.SalePrice, p.CostPrice,
               COALESCE(i.Quantity,0), p.MinimumStock, p.IsActive,
               COALESCE(i.AverageCost,p.CostPrice), p.ProductType, p.UnitName
        FROM Products p
        LEFT JOIN Categories c ON c.Id = p.CategoryId
        LEFT JOIN Inventory i ON i.ProductId = p.Id
        WHERE p.IsActive = 1 AND ($sellable = 0 OR p.ProductType != 2)
          AND ($text = '' OR p.Name LIKE '%' || $text || '%' OR p.Barcode = $text)
        ORDER BY p.Name
        """;

        cmd.Parameters.AddWithValue("$text", text.Trim());
        cmd.Parameters.AddWithValue("$sellable", sellableOnly ? 1 : 0);

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
                AverageCost = Convert.ToDecimal(reader.GetValue(9)),
                ProductType = Convert.ToInt32(reader.GetValue(10)),
                UnitName = reader.GetString(11)
            });
        }

        reader.Close();
        var available = new Dictionary<long, List<decimal>>();
        using (var recipe = connection.CreateCommand())
        {
            recipe.CommandText = """
                SELECT r.ProductId, ri.Quantity,
                       CASE WHEN ingredient.IsActive = 1 THEN COALESCE(stock.Quantity,0) ELSE 0 END
                FROM Recipes r JOIN RecipeItems ri ON ri.RecipeId = r.Id
                JOIN Products ingredient ON ingredient.Id = ri.IngredientProductId
                LEFT JOIN Inventory stock ON stock.ProductId = ingredient.Id
                WHERE r.IsActive = 1;
                """;
            using var rows = recipe.ExecuteReader();
            while (rows.Read())
            {
                var productId = rows.GetInt64(0);
                if (!available.TryGetValue(productId, out var amounts))
                    available[productId] = amounts = new List<decimal>();
                var required = Convert.ToDecimal(rows.GetValue(1));
                var stock = Convert.ToDecimal(rows.GetValue(2));
                amounts.Add(Math.Floor(stock / required));
            }
        }
        foreach (var product in result.Where(x => x.ProductType == 3))
            product.Stock = available.TryGetValue(product.Id, out var amounts) && amounts.Count > 0
                ? amounts.Min() : 0;
        var batchStock = new Dictionary<long, decimal>();
        using (var batches = connection.CreateCommand())
        {
            batches.CommandText = """
                SELECT ProductId, SUM(Quantity) FROM ProductBatches
                WHERE ExpiresAt >= date('now','localtime') AND Quantity > 0
                GROUP BY ProductId;
                """;
            using var rows = batches.ExecuteReader();
            while (rows.Read()) batchStock[rows.GetInt64(0)] = Convert.ToDecimal(rows.GetValue(1));
        }
        foreach (var product in result.Where(x => x.ProductType == 4))
            product.Stock = batchStock.GetValueOrDefault(product.Id);
        return result;
    }

    public Product? FindByBarcode(string barcode)
    {
        var code = barcode.Trim();
        var product = Search(code, true).FirstOrDefault(x =>
            string.Equals(x.Barcode, code, StringComparison.OrdinalIgnoreCase));
        if (product is not null) return product;
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT batch.Id, batch.ProductId, batch.BatchNumber, batch.Quantity
            FROM ProductBatches batch JOIN Products product ON product.Id=batch.ProductId
            WHERE batch.Barcode=$barcode AND product.IsActive=1 AND product.ProductType=4
              AND batch.ExpiresAt>=date('now','localtime') AND batch.Quantity>0;
            """;
        cmd.Parameters.AddWithValue("$barcode", code);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var batchId = reader.GetInt64(0);
        var productId = reader.GetInt64(1);
        var batchNumber = reader.GetString(2);
        var quantity = Convert.ToDecimal(reader.GetValue(3));
        reader.Close();
        product = Search("", true).FirstOrDefault(x => x.Id == productId);
        if (product is null) return null;
        product.BatchId = batchId;
        product.BatchNumber = batchNumber;
        product.Stock = quantity;
        return product;
    }

    public void Save(long? id, string name, string? barcode, decimal salePrice, decimal costPrice,
        decimal minimumStock, int productType = 1, string unitName = "عدد")
    {
        name = name.Trim();
        unitName = unitName.Trim();
        barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();
        if (name.Length == 0 || unitName.Length == 0 || salePrice < 0 || costPrice < 0 || minimumStock < 0 ||
            salePrice != decimal.Truncate(salePrice) || costPrice != decimal.Truncate(costPrice) ||
            productType is < 1 or > 4)
            throw new InvalidOperationException("نام یا قیمت محصول معتبر نیست.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        if (barcode is not null)
        {
            using var collision = connection.CreateCommand();
            collision.Transaction = transaction;
            collision.CommandText = "SELECT COUNT(*) FROM ProductBatches WHERE Barcode=$barcode";
            collision.Parameters.AddWithValue("$barcode", barcode);
            if (Convert.ToInt32(collision.ExecuteScalar()) > 0)
                throw new InvalidOperationException("این بارکد برای یک بچ یخچالی ثبت شده است.");
        }
        string? previousPrice = null;
        if (id.HasValue)
        {
            using var kind = connection.CreateCommand();
            kind.Transaction = transaction;
            kind.CommandText = """
                SELECT ProductType, COALESCE((SELECT Quantity FROM Inventory WHERE ProductId=p.Id),0),
                       (SELECT COUNT(*) FROM ProductBatches WHERE ProductId=p.Id)
                FROM Products p WHERE p.Id=$id;
                """;
            kind.Parameters.AddWithValue("$id", id.Value);
            using (var row = kind.ExecuteReader())
            {
                if (!row.Read()) throw new InvalidOperationException("محصول پیدا نشد.");
                if (Convert.ToInt32(row.GetValue(0)) != productType &&
                    (Convert.ToDecimal(row.GetValue(1)) != 0 || Convert.ToInt64(row.GetValue(2)) != 0))
                    throw new InvalidOperationException("نوع محصول دارای موجودی یا سابقهٔ بچ را نمی‌توان تغییر داد.");
            }
            if (productType != 2)
            {
                using var dependency = connection.CreateCommand();
                dependency.Transaction = transaction;
                dependency.CommandText = "SELECT COUNT(*) FROM RecipeItems WHERE IngredientProductId=$id";
                dependency.Parameters.AddWithValue("$id", id.Value);
                if (Convert.ToInt32(dependency.ExecuteScalar()) > 0)
                    throw new InvalidOperationException("این ماده در دستور تهیه استفاده شده است؛ ابتدا آن را از دستورها حذف کنید.");
            }
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
                  CostPrice=$cost, MinimumStock=$minimum, ProductType=$type, UnitName=$unit WHERE Id=$id;
              """
            : """
              INSERT INTO Products(Name, Barcode, SalePrice, CostPrice, MinimumStock, ProductType, UnitName)
              VALUES($name, $barcode, $sale, $cost, $minimum, $type, $unit);
              """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$barcode", (object?)barcode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sale", (long)salePrice);
        cmd.Parameters.AddWithValue("$cost", (long)costPrice);
        cmd.Parameters.AddWithValue("$minimum", Convert.ToDouble(minimumStock));
        cmd.Parameters.AddWithValue("$type", productType);
        cmd.Parameters.AddWithValue("$unit", unitName);
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
