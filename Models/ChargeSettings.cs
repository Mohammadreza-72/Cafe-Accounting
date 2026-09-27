namespace CafeArian.Models;

public sealed class ChargeSettings
{
    public string TaxMode { get; set; } = "Disabled";
    public decimal TaxValue { get; set; }
    public string TaxBase { get; set; } = "AfterDiscount";
    public string FeeMode { get; set; } = "Disabled";
    public decimal FeeValue { get; set; }
    public string FeeBase { get; set; } = "AfterDiscount";
    public string RoundingMode { get; set; } = "HalfUp";
}

public readonly record struct ChargeQuote(decimal Tax, decimal Fee);
