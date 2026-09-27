namespace CafeArian.Models;

public sealed class Supplier
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Mobile { get; set; } = "";
    public string Company { get; set; } = "";
    public string Address { get; set; } = "";
    public string Notes { get; set; } = "";
}
