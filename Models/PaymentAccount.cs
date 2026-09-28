namespace CafeArian.Models;

public sealed class BankAccount
{
    public long Id { get; set; }
    public string BankName { get; set; } = "";
    public string AccountTitle { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string CardNumber { get; set; } = "";
}

public sealed class PosDevice
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long? BankAccountId { get; set; }
    public string BankName { get; set; } = "";
    public string TerminalNumber { get; set; } = "";
}
