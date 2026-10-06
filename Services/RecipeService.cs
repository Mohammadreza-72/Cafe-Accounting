using CafeArian.Data;
using CafeArian.Models;

namespace CafeArian.Services;

public sealed class RecipeService
{
    public List<RecipeItem> GetItems(long productId)
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT ri.Id, ingredient.Id, ingredient.Name, ri.Quantity,
                   COALESCE(stock.AverageCost, ingredient.CostPrice), ingredient.UnitName
            FROM Recipes recipe JOIN RecipeItems ri ON ri.RecipeId=recipe.Id
            JOIN Products ingredient ON ingredient.Id=ri.IngredientProductId
            LEFT JOIN Inventory stock ON stock.ProductId=ingredient.Id
            WHERE recipe.ProductId=$product AND recipe.IsActive=1 ORDER BY ingredient.Name;
            """;
        cmd.Parameters.AddWithValue("$product", productId);
        using var reader = cmd.ExecuteReader();
        var result = new List<RecipeItem>();
        while (reader.Read())
            result.Add(new RecipeItem
            {
                Id = reader.GetInt64(0), IngredientProductId = reader.GetInt64(1),
                IngredientName = reader.GetString(2), Quantity = Convert.ToDecimal(reader.GetValue(3)),
                UnitCost = Convert.ToDecimal(reader.GetValue(4)),
                UnitName = reader.GetString(5)
            });
        return result;
    }

    public void SaveItem(long productId, long ingredientId, decimal quantity)
    {
        UserSession.Require("Admin", "Inventory");
        if (quantity <= 0 || productId == ingredientId)
            throw new InvalidOperationException("مقدار ماده اولیه باید مثبت باشد.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = """
                SELECT COUNT(*) FROM Products product CROSS JOIN Products ingredient
                WHERE product.Id=$product AND product.ProductType IN (3,4) AND product.IsActive=1
                  AND ingredient.Id=$ingredient AND ingredient.ProductType=2 AND ingredient.IsActive=1;
                """;
            check.Parameters.AddWithValue("$product", productId);
            check.Parameters.AddWithValue("$ingredient", ingredientId);
            if (Convert.ToInt32(check.ExecuteScalar()) != 1)
                throw new InvalidOperationException("محصول تولیدی یا ماده اولیه معتبر نیست.");
        }
        using (var recipe = connection.CreateCommand())
        {
            recipe.Transaction = transaction;
            recipe.CommandText = """
                INSERT INTO Recipes(ProductId) VALUES($product)
                ON CONFLICT(ProductId) DO UPDATE SET IsActive=1;
                """;
            recipe.Parameters.AddWithValue("$product", productId);
            recipe.ExecuteNonQuery();
        }
        using (var item = connection.CreateCommand())
        {
            item.Transaction = transaction;
            item.CommandText = """
                INSERT INTO RecipeItems(RecipeId, IngredientProductId, Quantity)
                SELECT Id, $ingredient, $qty FROM Recipes WHERE ProductId=$product
                ON CONFLICT(RecipeId, IngredientProductId) DO UPDATE SET Quantity=excluded.Quantity;
                """;
            item.Parameters.AddWithValue("$product", productId);
            item.Parameters.AddWithValue("$ingredient", ingredientId);
            item.Parameters.AddWithValue("$qty", Convert.ToDouble(quantity));
            item.ExecuteNonQuery();
        }
        using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = """
                INSERT INTO AuditLog(Action, ReferenceType, ReferenceId, Details)
                VALUES('RecipeUpdated','Product',$product,$details);
                """;
            audit.Parameters.AddWithValue("$product", productId);
            audit.Parameters.AddWithValue("$details", $"ماده {ingredientId}، مقدار {quantity}");
            audit.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void RemoveItem(long itemId)
    {
        UserSession.Require("Admin", "Inventory");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var item = connection.CreateCommand();
        item.Transaction = transaction;
        item.CommandText = """
            SELECT recipe.ProductId FROM RecipeItems ri JOIN Recipes recipe ON recipe.Id=ri.RecipeId WHERE ri.Id=$id;
            """;
        item.Parameters.AddWithValue("$id", itemId);
        var productId = item.ExecuteScalar();
        if (productId is null) throw new InvalidOperationException("ماده در دستور تهیه پیدا نشد.");
        using (var remove = connection.CreateCommand())
        {
            remove.Transaction = transaction;
            remove.CommandText = "DELETE FROM RecipeItems WHERE Id=$id";
            remove.Parameters.AddWithValue("$id", itemId);
            remove.ExecuteNonQuery();
        }
        using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = """
                INSERT INTO AuditLog(Action, ReferenceType, ReferenceId, Details)
                VALUES('RecipeItemRemoved','Product',$product,$details);
                """;
            audit.Parameters.AddWithValue("$product", Convert.ToInt64(productId));
            audit.Parameters.AddWithValue("$details", $"ردیف {itemId}");
            audit.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
