namespace CafeArian.Models;

public sealed class RecipeItem
{
    public long Id { get; set; }
    public long IngredientProductId { get; set; }
    public string IngredientName { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public string UnitName { get; set; } = "عدد";
    public string QuantityDisplay => $"{Quantity:0.###} {UnitName}";
    public decimal LineCost => Quantity * UnitCost;
}
