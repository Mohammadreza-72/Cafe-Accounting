using CafeArian.Data;
using CafeArian.Models;
using System.Globalization;

namespace CafeArian.Services;

public sealed class BatchService
{
    public List<ProductBatch> All()
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT batch.Id, batch.ProductId, product.Name, batch.BatchNumber,
                   COALESCE(batch.Barcode,''), batch.Source, batch.ProducedAt,
                   batch.ExpiresAt, batch.Quantity, batch.UnitCost
            FROM ProductBatches batch JOIN Products product ON product.Id=batch.ProductId
            ORDER BY batch.ExpiresAt, batch.Id;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<ProductBatch>();
        while (reader.Read()) result.Add(new ProductBatch
        {
            Id = reader.GetInt64(0), ProductId = reader.GetInt64(1),
            ProductName = reader.GetString(2), BatchNumber = reader.GetString(3),
            Barcode = reader.GetString(4), Source = reader.GetString(5),
            ProducedAt = reader.GetString(6), ExpiresAt = reader.GetString(7),
            Quantity = Convert.ToDecimal(reader.GetValue(8)), UnitCost = Convert.ToDecimal(reader.GetValue(9))
        });
        return result;
    }

    public void Register(long productId, string batchNumber, string? barcode, string source,
        DateTime producedAt, DateTime expiresAt, decimal quantity, decimal unitCost,
        bool fromRecipe = false)
    {
        UserSession.Require("Admin", "Inventory");
        if (string.IsNullOrWhiteSpace(batchNumber) || string.IsNullOrWhiteSpace(source) ||
            quantity <= 0 || unitCost < 0 || unitCost != decimal.Truncate(unitCost) ||
            producedAt.Date > expiresAt.Date)
            throw new InvalidOperationException("اطلاعات بچ، تاریخ‌ها یا مقدار و بهای واحد معتبر نیست.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        if (!string.IsNullOrWhiteSpace(barcode) &&
            BarcodeService.Exists(connection, transaction, barcode.Trim()))
            throw new InvalidOperationException("این بارکد قبلاً برای کالا یا بچ دیگری ثبت شده است.");
        var ingredients = new List<(long Id, string Name, decimal Quantity, decimal Cost)>();
        if (fromRecipe)
        {
            using var recipe = connection.CreateCommand();
            recipe.Transaction = transaction;
            recipe.CommandText = """
                SELECT ingredient.Id, ingredient.Name, ingredient.IsActive, ri.Quantity,
                       COALESCE(stock.AverageCost,ingredient.CostPrice)
                FROM Recipes r JOIN RecipeItems ri ON ri.RecipeId=r.Id
                JOIN Products ingredient ON ingredient.Id=ri.IngredientProductId
                LEFT JOIN Inventory stock ON stock.ProductId=ingredient.Id
                WHERE r.ProductId=$product AND r.IsActive=1;
                """;
            recipe.Parameters.AddWithValue("$product", productId);
            using var reader = recipe.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetInt64(2) != 1)
                    throw new InvalidOperationException("یکی از مواد دستور تهیه غیرفعال است.");
                ingredients.Add((reader.GetInt64(0), reader.GetString(1),
                    Convert.ToDecimal(reader.GetValue(3)) * quantity,
                    Convert.ToDecimal(reader.GetValue(4))));
            }
            if (ingredients.Count == 0)
                throw new InvalidOperationException("برای تولید این محصول دستور تهیه ثبت نشده است.");
            unitCost = ingredients.Sum(x => x.Quantity * x.Cost) / quantity;
        }
        using (var batch = connection.CreateCommand())
        {
            batch.Transaction = transaction;
            batch.CommandText = """
                INSERT INTO ProductBatches(ProductId, BatchNumber, Barcode, Source,
                    ProducedAt, ExpiresAt, Quantity, UnitCost)
                SELECT Id, $number, $barcode, $source, $produced, $expires, $qty, $cost
                FROM Products WHERE Id=$product AND IsActive=1 AND ProductType=4;
                SELECT last_insert_rowid();
                """;
            batch.Parameters.AddWithValue("$product", productId);
            batch.Parameters.AddWithValue("$number", batchNumber.Trim());
            batch.Parameters.AddWithValue("$barcode", string.IsNullOrWhiteSpace(barcode) ? DBNull.Value : barcode.Trim());
            batch.Parameters.AddWithValue("$source", source.Trim());
            batch.Parameters.AddWithValue("$produced", producedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            batch.Parameters.AddWithValue("$expires", expiresAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            batch.Parameters.AddWithValue("$qty", Convert.ToDouble(quantity));
            batch.Parameters.AddWithValue("$cost", Convert.ToDouble(unitCost));
            var batchId = Convert.ToInt64(batch.ExecuteScalar());
            if (batchId == 0) throw new InvalidOperationException("محصول یخچالی فعال پیدا نشد.");
            if (string.IsNullOrWhiteSpace(barcode))
            {
                var generatedBarcode = BarcodeService.Generate(connection, transaction,
                    $"AR-B-{batchId}", exceptBatchId: batchId);
                using var generated = connection.CreateCommand();
                generated.Transaction = transaction;
                generated.CommandText = "UPDATE ProductBatches SET Barcode=$barcode WHERE Id=$id";
                generated.Parameters.AddWithValue("$barcode", generatedBarcode);
                generated.Parameters.AddWithValue("$id", batchId);
                generated.ExecuteNonQuery();
            }
            foreach (var ingredient in ingredients)
            {
                using var consume = connection.CreateCommand();
                consume.Transaction = transaction;
                consume.CommandText = """
                    UPDATE Inventory SET Quantity=Quantity-$qty, UpdatedAt=CURRENT_TIMESTAMP
                    WHERE ProductId=$product AND Quantity>=$qty;
                    """;
                consume.Parameters.AddWithValue("$product", ingredient.Id);
                consume.Parameters.AddWithValue("$qty", Convert.ToDouble(ingredient.Quantity));
                if (consume.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException($"موجودی ماده «{ingredient.Name}» برای تولید کافی نیست.");
                consume.CommandText = """
                    INSERT INTO InventoryTransactions(ProductId,TransactionType,Quantity,UnitCost,ReferenceType,ReferenceId)
                    VALUES($product,'ProductionConsumption',-$qty,$cost,'Batch',$batch);
                    """;
                consume.Parameters.AddWithValue("$cost", Convert.ToDouble(ingredient.Cost));
                consume.Parameters.AddWithValue("$batch", batchId);
                consume.ExecuteNonQuery();
            }
            using (var stock = connection.CreateCommand())
            {
                stock.Transaction = transaction;
                stock.CommandText = """
                    INSERT INTO Inventory(ProductId, Quantity, AverageCost) VALUES($product, $qty, $cost)
                    ON CONFLICT(ProductId) DO UPDATE SET
                      AverageCost = CASE WHEN Inventory.Quantity+$qty=0 THEN $cost
                        ELSE (Inventory.Quantity*Inventory.AverageCost+$qty*$cost)/(Inventory.Quantity+$qty) END,
                      Quantity=Inventory.Quantity+$qty, UpdatedAt=CURRENT_TIMESTAMP;
                    """;
                stock.Parameters.AddWithValue("$product", productId);
                stock.Parameters.AddWithValue("$qty", Convert.ToDouble(quantity));
                stock.Parameters.AddWithValue("$cost", Convert.ToDouble(unitCost));
                stock.ExecuteNonQuery();
            }
            using (var movement = connection.CreateCommand())
            {
                movement.Transaction = transaction;
                movement.CommandText = """
                    INSERT INTO InventoryTransactions(ProductId, TransactionType, Quantity, UnitCost, ReferenceType, ReferenceId)
                    VALUES($product,$type,$qty,$cost,'Batch',$batch);
                    """;
                movement.Parameters.AddWithValue("$product", productId);
                movement.Parameters.AddWithValue("$qty", Convert.ToDouble(quantity));
                movement.Parameters.AddWithValue("$cost", Convert.ToDouble(unitCost));
                movement.Parameters.AddWithValue("$type", fromRecipe ? "ProductionReceipt" : "BatchReceipt");
                movement.Parameters.AddWithValue("$batch", batchId);
                movement.ExecuteNonQuery();
            }
            using (var audit = connection.CreateCommand())
            {
                audit.Transaction = transaction;
                audit.CommandText = """
                    INSERT INTO AuditLog(Action,ReferenceType,ReferenceId,Details)
                    VALUES($action,'Batch',$batch,$details);
                    """;
                audit.Parameters.AddWithValue("$batch", batchId);
                audit.Parameters.AddWithValue("$action", fromRecipe ? "BatchProduced" : "BatchRegistered");
                audit.Parameters.AddWithValue("$details", $"محصول {productId}، تعداد {quantity}، انقضا {expiresAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
                audit.ExecuteNonQuery();
            }
            var batchValue = Math.Round(quantity * unitCost, 2, MidpointRounding.AwayFromZero);
            if (batchValue > 0)
                JournalService.Post(connection, transaction, "Batch", batchId, "Original",
                    new JournalLine("1300", batchValue, 0),
                    new JournalLine(fromRecipe ? "1300" : "3900", 0, batchValue));
        }
        transaction.Commit();
    }

    public void Discard(long batchId)
    {
        UserSession.Require("Admin", "Inventory");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = "SELECT ProductId, Quantity, UnitCost FROM ProductBatches WHERE Id=$id AND Quantity>0";
        lookup.Parameters.AddWithValue("$id", batchId);
        long productId;
        decimal quantity;
        decimal unitCost;
        using (var reader = lookup.ExecuteReader())
        {
            if (!reader.Read()) throw new InvalidOperationException("بچ دارای موجودی پیدا نشد.");
            productId = reader.GetInt64(0);
            quantity = Convert.ToDecimal(reader.GetValue(1));
            unitCost = Convert.ToDecimal(reader.GetValue(2));
        }
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "UPDATE Inventory SET Quantity=Quantity-$qty, UpdatedAt=CURRENT_TIMESTAMP WHERE ProductId=$product AND Quantity>=$qty";
        cmd.Parameters.AddWithValue("$batch", batchId);
        cmd.Parameters.AddWithValue("$product", productId);
        cmd.Parameters.AddWithValue("$qty", Convert.ToDouble(quantity));
        cmd.Parameters.AddWithValue("$cost", Convert.ToDouble(unitCost));
        cmd.Parameters.AddWithValue("$details", $"محصول {productId}، ضایعات {quantity}");
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("موجودی کل برای خروج بچ کافی نیست.");
        cmd.CommandText = "UPDATE ProductBatches SET Quantity=0 WHERE Id=$batch AND Quantity=$qty";
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("موجودی بچ تغییر کرده است.");
        cmd.CommandText = """
            INSERT INTO InventoryTransactions(ProductId,TransactionType,Quantity,UnitCost,ReferenceType,ReferenceId)
              VALUES($product,'BatchDisposal',-$qty,$cost,'Batch',$batch);
            INSERT INTO AuditLog(Action,ReferenceType,ReferenceId,Details)
              VALUES('BatchDiscarded','Batch',$batch,$details);
            """;
        cmd.ExecuteNonQuery();
        var discardedValue = Math.Round(quantity * unitCost, 2, MidpointRounding.AwayFromZero);
        if (discardedValue > 0)
            JournalService.Post(connection, transaction, "BatchDisposal", batchId, "Original",
                new JournalLine("6100", discardedValue, 0),
                new JournalLine("1300", 0, discardedValue));
        transaction.Commit();
    }
}
