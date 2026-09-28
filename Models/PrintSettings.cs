namespace CafeArian.Models;

public sealed class PrintSettings
{
    public string ReceiptPrinter { get; init; } = "";
    public string LabelPrinter { get; init; } = "";
    public int ReceiptWidthMm { get; init; } = 80;
    public int LabelWidthMm { get; init; } = 50;
}
